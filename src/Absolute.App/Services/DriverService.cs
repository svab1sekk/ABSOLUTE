using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using Absolute.Core;

namespace Absolute.App.Services;

internal static class DriverService
{
    private static readonly HttpClient Client=new(new HttpClientHandler{AutomaticDecompression=DecompressionMethods.All}){Timeout=TimeSpan.FromSeconds(22),MaxResponseContentBufferSize=8*1024*1024};
    static DriverService()=>Client.DefaultRequestHeaders.UserAgent.ParseAdd("ABSOLUTE/0.5 (+https://github.com/svab1sekk/ABSOLUTE)");

    public static async Task<VendorDriverResult> SearchAsync(HardwarePart part,IReadOnlyList<DriverDevice> installed,CancellationToken cancellationToken)
    {
        var source=SupportUrl(part);var checkedAt=DateTimeOffset.UtcNow;
        try
        {
            if(part.Category=="Monitor"&&part.Name.StartsWith("TITAN ARMY ",StringComparison.OrdinalIgnoreCase))
                source=await MonitorSupportResolver.ResolveAsync(part.Name,GetAsync,cancellationToken);
            if(part.Category=="Processor")return new(part.Id,source,"CPU support is included in motherboard chipset packages",[],checkedAt);
            if(part.Category=="Graphics"&&part.Name.Contains("NVIDIA",StringComparison.OrdinalIgnoreCase))
            {
                if(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture!=System.Runtime.InteropServices.Architecture.X64)
                    return new(part.Id,source,"Choose the matching architecture on NVIDIA support",[],checkedAt);
                // Match NVIDIA's own product-family IDs, including desktop/notebook variants.
                var lookups=await GetAsync("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3",cancellationToken);
                var values=XDocument.Parse(lookups).Descendants("LookupValue").ToArray();
                var name=NormalizeGpu(part.Name);
                var matches=values.Where(v=>NormalizeGpu(v.Element("Name")?.Value??"")==name).ToArray();
                if(matches.Length>1)
                {
                    var notebook=HardwareDiscovery.Query("SELECT PCSystemType FROM Win32_ComputerSystem").Any(x=>Convert.ToInt32(x.GetValueOrDefault("PCSystemType"))==2);
                    var series=XDocument.Parse(await GetAsync("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=2",cancellationToken)).Descendants("LookupValue")
                        .Where(v=>Regex.IsMatch(v.Element("Name")?.Value??"","notebook|laptop|Quadro Blade",RegexOptions.IgnoreCase)).Select(v=>v.Element("Value")?.Value).ToHashSet();
                    matches=matches.Where(v=>series.Contains(v.Attribute("ParentID")?.Value)==notebook).ToArray();
                }
                if(matches.Length!=1||!int.TryParse(matches[0].Element("Value")?.Value,out var family))return new(part.Id,source,"NVIDIA could not match this model automatically",[],checkedAt);
                var os=Environment.OSVersion.Version.Build>=22000?135:57;
                var json=await GetAsync($"https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&pfid={family}&osID={os}&dch=1&numberOfResults=10&languageCode=1033",cancellationToken);
                var driver=installed.FirstOrDefault(d=>d.Provider.Contains("NVIDIA",StringComparison.OrdinalIgnoreCase));
                var packages=VendorDrivers.ReadNvidia(json,driver is null?null:VendorDrivers.NvidiaVersion(driver.Version));
                return new(part.Id,source,packages.Count>0?"Checked NVIDIA catalog":"No matching Game Ready package returned",packages,checkedAt);
            }
            if(part.Category=="Motherboard"&&part.Name.StartsWith("ASUS ",StringComparison.OrdinalIgnoreCase))
            {
                var model=Uri.EscapeDataString(part.Name[5..]);
                var root="https://www.asus.com/support/api/product.asmx/";
                var osJson=await GetAsync(root+"GetPDOS?website=global&model="+model,cancellationToken);
                using var osDoc=JsonDocument.Parse(osJson);
                var windows=Environment.OSVersion.Version.Build>=22000?"Windows 11":"Windows 10";
                var os=osDoc.RootElement.GetProperty("Result").GetProperty("Obj").EnumerateArray().FirstOrDefault(v=>VendorDrivers.Value(v,"Name").Contains(windows,StringComparison.OrdinalIgnoreCase));
                if(os.ValueKind==JsonValueKind.Undefined)return new(part.Id,source,"ASUS lists no packages for this Windows version",[],checkedAt);
                var osId=VendorDrivers.Value(os,"Id");
                var json=await GetAsync(root+"GetPDDrivers?website=global&model="+model+"&osid="+Uri.EscapeDataString(osId),cancellationToken);
                var packages=VendorDrivers.ReadAsus(json,installed,InstalledChipsetBundles());
                return new(part.Id,source,packages.Count>0?"Latest motherboard packages · ASUS":"No matching packages returned by ASUS",packages,checkedAt);
            }
            // Some vendors expose versioned downloads in the product page. Do not label
            // a driver current unless a comparable version and a model-specific package exist.
            if(source.Length==0)return new(part.Id,"","Driver link unavailable",[],checkedAt);
            var html=await GetAsync(source,cancellationToken);
            if(part.Category=="Graphics")
            {
                var vendor=part.Name.Contains("Radeon",StringComparison.OrdinalIgnoreCase)?"AMD":part.Name.Contains("Intel",StringComparison.OrdinalIgnoreCase)?"Intel":"";
                var model=Regex.Replace(part.Name,@"\b(?:AMD|Intel|Radeon|Graphics)\b|\s+","",RegexOptions.IgnoreCase);
                var pageText=Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]+>"," ")),@"\s+|[®™]","");
                // Only a model-specific product response may advertise a direct GPU package.
                if(model.Length>3&&pageText.Contains(model,StringComparison.OrdinalIgnoreCase))
                {
                    var current=installed.FirstOrDefault(d=>d.Category.Equals("DISPLAY",StringComparison.OrdinalIgnoreCase)&&!d.Provider.Equals("Microsoft",StringComparison.OrdinalIgnoreCase))?.Version;
                    var gpuPackages=VendorDrivers.ReadGpuPage(html,vendor,current);
                    if(gpuPackages.Count>0)return new(part.Id,source,"Checked "+vendor+" product downloads",gpuPackages,checkedAt);
                }
            }
            var downloads=ReadProductDownloads(html,source,part,installed);
            return new(part.Id,source,downloads.Count>0?"Downloads found":"",downloads,checkedAt);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception error)when(error is HttpRequestException or TaskCanceledException or JsonException or System.Xml.XmlException or InvalidOperationException or KeyNotFoundException or ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {return new(part.Id,source,"Driver check unavailable",[],checkedAt);}
    }

    private static async Task<string> GetAsync(string url,CancellationToken cancellationToken)
    {
        using var response=await Client.GetAsync(url,cancellationToken);response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<string,string> InstalledChipsetBundles()
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);
            using var root=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach(var id in root?.GetSubKeyNames()??[])
            {
                try
                {
                    using var key=root?.OpenSubKey(id);var name=HardwareNames.Clean(key?.GetValue("DisplayName") as string??"");
                    var vendor=name.Equals("AMD Chipset Software",StringComparison.OrdinalIgnoreCase)?"AMD":name.Equals("Intel Chipset Device Software",StringComparison.OrdinalIgnoreCase)?"Intel":null;
                    if(vendor is not null&&key?.GetValue("DisplayVersion") is string version)result.TryAdd(vendor,version);
                }
                catch(Exception error)when(error is UnauthorizedAccessException or System.Security.SecurityException or IOException){}
            }
        }
        return result;
    }

    private static string NormalizeGpu(string name)=>Regex.Replace(HardwareNames.Clean(name),@"\bNVIDIA\b|\s+with Max-Q Design|\s+\d+GB|\s*\(.*?\)|\s+(Laptop|Notebook) GPU", "",RegexOptions.IgnoreCase).Split('/')[0].Trim().ToUpperInvariant();

    private static IReadOnlyList<VendorPackage> ReadProductDownloads(string html,string page,HardwarePart part,IReadOnlyList<DriverDevice> installed)
    {
        // JSON-LD SoftwareApplication data belongs to the vendor page, not an aggregator.
        var result=new List<VendorPackage>();
        foreach(Match script in Regex.Matches(html,"<script[^>]+type=[\"']application/ld\\+json[\"'][^>]*>(.*?)</script>",RegexOptions.IgnoreCase|RegexOptions.Singleline))
        {
            try{using var doc=JsonDocument.Parse(WebUtility.HtmlDecode(script.Groups[1].Value));Visit(doc.RootElement);}catch(JsonException){}
        }
        return result.DistinctBy(p=>p.DownloadUrl).ToArray();
        void Visit(JsonElement element)
        {
            if(element.ValueKind==JsonValueKind.Array){foreach(var child in element.EnumerateArray())Visit(child);return;}
            if(element.ValueKind!=JsonValueKind.Object)return;
            var name=VendorDrivers.Value(element,"name");var version=VendorDrivers.Value(element,"softwareVersion");var download=VendorDrivers.Value(element,"downloadUrl");
            var model=part.Name.Split(' ',2).Last();
            var origin=new Uri(page).Host;origin=origin.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?origin[4..]:origin;
            if(name.Contains(model,StringComparison.OrdinalIgnoreCase)&&version.Length>0&&VendorDrivers.OfficialUrl(download,origin))
                result.Add(new(name,version,download,installed.Select(d=>d.Version).Distinct().Take(2).ToArray() is [var current]?current:null));
            foreach(var child in element.EnumerateObject())if(child.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)Visit(child.Value);
        }
    }

    public static string SupportUrl(HardwarePart part)
    {
        var name=part.Name;var query=Uri.EscapeDataString(name);
        if(part.Category=="Motherboard")
        {
            var model=Regex.Replace(name,@"^(ASUS|MSI|Gigabyte|ASRock)\s+","",RegexOptions.IgnoreCase);
            if(name.StartsWith("ASUS",StringComparison.OrdinalIgnoreCase))return "https://www.asus.com/supportonly/"+Uri.EscapeDataString(model)+"/helpdesk_download/";
            if(name.StartsWith("MSI",StringComparison.OrdinalIgnoreCase))return "https://www.msi.com/Motherboard/"+Uri.EscapeDataString(Regex.Replace(model,@"\s*\(MS-[^)]*\)","").Trim().Replace(' ','-'))+"/support#driver";
            if(name.Contains("Gigabyte",StringComparison.OrdinalIgnoreCase))return "https://www.gigabyte.com/Search?kw="+query;
            if(name.Contains("ASRock",StringComparison.OrdinalIgnoreCase))return "https://www.asrock.com/support/index.asp?Model="+Uri.EscapeDataString(model);
        }
        if(name.Contains("NVIDIA",StringComparison.OrdinalIgnoreCase))return "https://www.nvidia.com/en-us/drivers/";
        if(part.Category=="Graphics"&&Regex.Match(name,@"Radeon\s+RX\s+(\d{4})\s*(XTX|XT|GRE)?",RegexOptions.IgnoreCase) is {Success:true} radeon)
        {
            var number=radeon.Groups[1].Value;var model="amd-radeon-rx-"+number+(radeon.Groups[2].Success?"-"+radeon.Groups[2].Value.ToLowerInvariant():"");
            return $"https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-{number[0]}000-series/{model}.html";
        }
        if(part.Category=="Graphics"&&Regex.IsMatch(name,@"Intel.*(?:Arc|Iris\s*Xe)",RegexOptions.IgnoreCase))return "https://www.intel.com/content/www/us/en/download/785597/intel-arc-iris-xe-graphics-windows.html";
        if(name.Contains("AMD",StringComparison.OrdinalIgnoreCase)||name.Contains("Radeon",StringComparison.OrdinalIgnoreCase))return "https://www.amd.com/en/support/download/drivers.html";
        if(name.Contains("Intel",StringComparison.OrdinalIgnoreCase))return "https://www.intel.com/content/www/us/en/search.html?ws=text#q="+query+"&sort=relevancy&f:@tabfilter=[Downloads]";
        if(name.Contains("TITAN ARMY",StringComparison.OrdinalIgnoreCase))return "";
        foreach(var (brand,url) in Vendors)if(name.Contains(brand,StringComparison.OrdinalIgnoreCase))return url+query;
        return "";
    }
    private static readonly (string,string)[] Vendors=
    [
        ("Dell","https://www.dell.com/support/search/en-us#q="),("Lenovo","https://pcsupport.lenovo.com/us/en/search?query="),("HP ","https://support.hp.com/us-en/search?q="),
        ("Samsung","https://www.samsung.com/us/support/search/?query="),("BenQ","https://www.benq.com/en-us/search.html?q="),("LG ","https://www.lg.com/us/search?q="),
        ("ASUS","https://www.asus.com/support/search-result.aspx?searchType=products&searchKey="),("MSI","https://www.msi.com/search/"),("AOC ","https://aoc.com/search?query="),
        ("Acer","https://www.acer.com/us-en/support/product-support?search="),("ViewSonic","https://www.viewsonic.com/global/search.php?f_search="),("Philips","https://www.philips.com/c-w/search.html#q="),("iiyama","https://iiyama.com/gl_en/search/?q=")
    ];
}
