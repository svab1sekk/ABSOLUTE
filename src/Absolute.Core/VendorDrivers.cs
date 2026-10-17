using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Absolute.Core;

public sealed record VendorPackage(string Name, string Version, string DownloadUrl, string? InstalledVersion = null, string? Date = null)
{
    public int? Comparison => VendorDrivers.CompareVersions(Version, InstalledVersion);
    public string Status => Comparison switch { > 0 => "Update available", <= 0 => "Installed version is current", _ => "Package version · compare in vendor installer" };
}
public sealed record VendorDriverResult(string PartId, string Source, string Status, IReadOnlyList<VendorPackage> Packages, DateTimeOffset CheckedAt);

public static class VendorDrivers
{
    public static string NvidiaVersion(string version)
    {
        if (!Version.TryParse(version, out var value) || value.Build < 0 || value.Revision < 0) return version;
        var number = (value.Build % 10 * 10000 + value.Revision).ToString("D5", CultureInfo.InvariantCulture);
        return number[..^2] + "." + number[^2..];
    }

    // Marketing package versions and Windows INF versions are different namespaces.
    // Only compare numeric versions with the same number of components.
    public static int? CompareVersions(string? latest, string? installed)
    {
        static string? Numeric(string? value)
        {
            var match = Regex.Match(value?.Trim() ?? "", @"^v?(\d+(?:\.\d+){1,3})$", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
        var a = Numeric(latest); var b = Numeric(installed);
        if (a is null || b is null || a.Count(c => c == '.') != b.Count(c => c == '.')) return null;
        return Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) ? va.CompareTo(vb) : null;
    }

    public static bool OfficialUrl(string? value, params string[] domains) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && domains.Any(domain => uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<VendorPackage> ReadNvidia(string json, string? installed)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("IDS", out var ids) || ids.ValueKind != JsonValueKind.Array) return [];
        var result = new List<VendorPackage>();
        foreach (var item in ids.EnumerateArray())
        {
            if (!item.TryGetProperty("downloadInfo", out var info)) continue;
            var url = Value(info, "DownloadURL"); var version = Value(info, "Version");
            if (!OfficialUrl(url, "nvidia.com") || string.IsNullOrWhiteSpace(version) || Value(info, "IsCRD") == "1" || Value(info, "IsBeta") == "1") continue;
            result.Add(new("NVIDIA Game Ready", version, url, installed, Value(info, "ReleaseDateTime")));
        }
        return result.OrderByDescending(p => DateTimeOffset.TryParse(p.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateTimeOffset.MinValue).Take(1).ToArray();
    }

    public static IReadOnlyList<VendorPackage> ReadAsus(string json, IReadOnlyList<DriverDevice> installed, IReadOnlyDictionary<string,string>? bundles = null)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Result", out var result) || !result.TryGetProperty("Obj", out var categories) || categories.ValueKind != JsonValueKind.Array) return [];
        var packages = new List<VendorPackage>();
        foreach (var category in categories.EnumerateArray())
        {
            var categoryName = Value(category, "Name");
            if (!Regex.IsMatch(categoryName, @"chipset|LAN|wireless|Bluetooth|SATA|RAID|serial|management engine", RegexOptions.IgnoreCase) || Regex.IsMatch(categoryName, @"audio|utility|BIOS|firmware", RegexOptions.IgnoreCase)) continue;
            if (!category.TryGetProperty("Files", out var files) || files.ValueKind != JsonValueKind.Array) continue;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files.EnumerateArray().OrderByDescending(f => Value(f, "ReleaseDate")))
            {
                if (Value(file, "IsRelease") == "0" || !file.TryGetProperty("DownloadUrl", out var download)) continue;
                var url = Value(download, "Global"); if (!OfficialUrl(url, "asus.com")) continue;
                var title = WebUtility.HtmlDecode(Regex.Replace(Value(file, "Title"), "<[^>]*>", "")).Trim();
                var family = Regex.Match(title, @"Intel|AMD|Realtek|MediaTek|Qualcomm|Marvell|Broadcom", RegexOptions.IgnoreCase).Value;
                // Keep the newest package for each vendor in a category; boards often have alternative Wi-Fi modules.
                if (!seen.Add(categoryName + family)) continue;
                var version = Value(file, "Version").TrimStart('V', 'v');
                var wireless = Regex.IsMatch(categoryName + " " + title, @"wireless|Wi-?Fi|WLAN", RegexOptions.IgnoreCase);
                var candidates = installed.Where(d => family.Length > 0 && (d.Name + " " + d.Provider + " " + d.Manufacturer).Contains(family, StringComparison.OrdinalIgnoreCase))
                    .Where(d => categoryName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ? d.Category.Equals("BLUETOOTH", StringComparison.OrdinalIgnoreCase) : Regex.IsMatch(categoryName, "LAN|wireless", RegexOptions.IgnoreCase) ? d.Category.Equals("NET", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(d.Name, @"wireless|Wi-?Fi|WLAN|802\.11", RegexOptions.IgnoreCase) == wireless : d.Category.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)).ToArray();
                // Package bundles (especially chipsets) cannot be compared to an arbitrary device INF version.
                var exact = candidates.Where(d => title.Contains(d.Name, StringComparison.OrdinalIgnoreCase)).Select(d => d.Version).Distinct().ToArray();
                var chipset = categoryName.Contains("chipset", StringComparison.OrdinalIgnoreCase);
                var knownVersions = candidates.Select(d => d.Version).Distinct().ToArray();
                var installedVersion = chipset ? bundles?.GetValueOrDefault(family) : exact.Length == 1 ? exact[0] : knownVersions.Length == 1 ? knownVersions[0] : null;
                packages.Add(new(title.Length > 0 ? title : categoryName, version, url, installedVersion, Value(file, "ReleaseDate")));
            }
        }
        return packages;
    }

    public static IReadOnlyList<VendorPackage> ReadGpuPage(string html, string vendor, string? installed)
    {
        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        if (vendor == "AMD")
        {
            var store = Regex.Match(text, @"Windows\s+Driver\s+Store\s+Version\s*[:(]?\s*(\d+(?:\.\d+){3})", RegexOptions.IgnoreCase).Groups[1].Value;
            var urls = Regex.Matches(html, "https://drivers\\.amd\\.com/[^\\s\"'<>]+\\.exe", RegexOptions.IgnoreCase).Select(m => WebUtility.HtmlDecode(m.Value.Replace("\\/", "/", StringComparison.Ordinal)))
                .Where(u => OfficialUrl(u, "amd.com") && Regex.IsMatch(u, @"win10|win11|win-10|win-11", RegexOptions.IgnoreCase)).Distinct().ToArray();
            if (store.Length > 0 && urls.Length == 1) return [new("AMD Software: Adrenalin Edition", store, urls[0], installed)];
        }
        if (vendor == "Intel")
        {
            var version = Regex.Match(html, "(?:dc:version|DCTERMS\\.version)[\"'][^>]*content=[\"'](?<version>\\d+(?:\\.\\d+){3})", RegexOptions.IgnoreCase).Groups["version"].Value;
            if (version.Length == 0) version = Regex.Match(text, @"Version\s*[:]?\s*(\d+(?:\.\d+){3})", RegexOptions.IgnoreCase).Groups[1].Value;
            var urls = Regex.Matches(html, "https://downloadmirror\\.intel\\.com/[^\\s\"'<>]+\\.exe", RegexOptions.IgnoreCase).Select(m => WebUtility.HtmlDecode(m.Value)).Where(u => OfficialUrl(u, "intel.com")).Distinct().ToArray();
            if (version.Length > 0 && urls.Length == 1) return [new("Intel Graphics", version, urls[0], installed)];
        }
        return [];
    }

    public static string Value(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ToString() : "";
}
