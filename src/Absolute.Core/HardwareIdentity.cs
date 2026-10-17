using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Absolute.Core;

public sealed record MemoryModuleInfo(string Manufacturer, string PartNumber, ulong Capacity, int Type, int Speed);
public sealed record MonitorEdid(string Manufacturer, string Product, string Name, string Serial, int Width, int Height, double RefreshRate);

public static class HardwareIdentity
{
    public static MonitorEdid? ReadEdid(byte[] bytes)
    {
        ReadOnlySpan<byte> header = [0, 255, 255, 255, 255, 255, 255, 0];
        if (bytes.Length < 128 || !bytes.AsSpan(0, 8).SequenceEqual(header) || bytes.Take(128).Sum(b => (int)b) % 256 != 0) return null;
        var vendor = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8));
        var manufacturer = new string([Letter((vendor >> 10) & 31), Letter((vendor >> 5) & 31), Letter(vendor & 31)]);
        var product = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)).ToString("X4");
        var serial = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        var name = ""; var textSerial = ""; var width = 0; var height = 0; var refresh = 0d;
        for (var offset = 54; offset + 18 <= 126; offset += 18)
        {
            var block = bytes.AsSpan(offset, 18);
            if (block[0] == 0 && block[1] == 0)
            {
                var text = Encoding.ASCII.GetString(block[5..]).Trim('\0', '\r', '\n', ' ');
                if (block[3] == 0xFC) name = text;
                if (block[3] == 0xFF) textSerial = text;
            }
            else if (width == 0)
            {
                width = block[2] + ((block[4] & 0xF0) << 4); height = block[5] + ((block[7] & 0xF0) << 4);
                var horizontalTotal = width + block[3] + ((block[4] & 15) << 8);
                var verticalTotal = height + block[6] + ((block[7] & 15) << 8);
                if (horizontalTotal > 0 && verticalTotal > 0) refresh = BinaryPrimitives.ReadUInt16LittleEndian(block) * 10000d / horizontalTotal / verticalTotal;
            }
        }
        return new(manufacturer, product, MonitorName(name, manufacturer), textSerial.Length > 0 ? textSerial : serial is 0 or uint.MaxValue ? "" : serial.ToString(), width, height, refresh);
    }

    private static char Letter(int value) => value is >= 1 and <= 26 ? (char)('A' + value - 1) : '?';

    public static string MonitorName(string model, string manufacturer)
    {
        var clean = HardwareNames.Clean(model);
        if (string.IsNullOrWhiteSpace(clean) || Regex.IsMatch(clean, @"generic|pnp monitor|unknown|unavailable", RegexOptions.IgnoreCase)) return "";
        if (Regex.Replace(clean, "[^a-zA-Z0-9]", "").Equals("P25SPLUS", StringComparison.OrdinalIgnoreCase)) return "TITAN ARMY P25S Plus";
        if (Regex.Replace(clean, "[^a-zA-Z0-9]", "").Equals("P2510SPLUS", StringComparison.OrdinalIgnoreCase)) return "TITAN ARMY P2510S Plus";
        var brand = manufacturer.ToUpperInvariant() switch
        {
            "DEL" => "Dell", "SAM" => "Samsung", "GSM" or "GSE" => "LG", "ACI" or "AUS" => "ASUS", "BNQ" => "BenQ", "ACR" => "Acer", "AOC" => "AOC", "MSI" => "MSI", "IVM" => "iiyama", "LEN" => "Lenovo", "HWP" or "HPN" => "HP", "PHL" => "Philips", "VSC" => "ViewSonic", "GBT" => "Gigabyte", _ => ""
        };
        return brand.Length > 0 && !clean.StartsWith(brand, StringComparison.OrdinalIgnoreCase) ? brand + " " + clean : clean;
    }

    public static bool IsSpecificPeripheral(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var value = HardwareNames.Clean(name);
        var generic=Regex.Replace(value,@"^(?:Logitech|Logi|Razer|Microsoft|Dell|HP|Corsair|SteelSeries)\s+","",RegexOptions.IgnoreCase);
        if (Regex.IsMatch(generic, @"^(?:(?:USB|HID|Bluetooth|Generic|Standard|Wireless|Gaming|Mechanical|Optical|Multimedia|2\.4G|PS/2|PnP)[-\s]*)*(?:keyboard|mouse|monitor|input device|audio device|device)$", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(value, @"^(?:(?:Logitech|Logi|Razer)\s+)?(?:(?:USB|Unifying|LIGHTSPEED|Bolt|Wireless|2\.4G)\s+)*Receiver$", RegexOptions.IgnoreCase)) return false;
        return !Regex.IsMatch(value, @"(?:^Unknown|^Unavailable|compliant|human interface|high definition audio|audio endpoint|advanced audio device|Realtek|NVIDIA.*Audio|Intel.*Audio|Microsoft.*Audio|DisplayPort|HDMI|host controller|root hub|composite device|virtual|hyper.?v|^Speakers$|^Microphone$)", RegexOptions.IgnoreCase);
    }

    public static string PeripheralCategory(string name,string reported)
    {
        if(name.Contains("keyboard",StringComparison.OrdinalIgnoreCase))return "Keyboard";
        if(Regex.IsMatch(name,@"mouse|superlight|deathadder|basilisk|Razer\s+Viper|naga|aerox|rival|sensei|\bG(?:102|203|305|403|502|503|603|703|903)\b|Keychron\s+M\d",RegexOptions.IgnoreCase))return "Mouse";
        if(Regex.IsMatch(name,@"keyboard|wooting|keychron|blackwidow|huntsman|deathstalker|alloy|apex.?pro|\bK\d{2,3}\b|\bG(?:413|513|613|715|815|915)\b",RegexOptions.IgnoreCase))return "Keyboard";
        return reported;
    }

    public static HardwarePart CombineMemory(IEnumerable<MemoryModuleInfo> source, ulong usable)
    {
        var modules = source.ToArray(); var total = modules.Aggregate(0UL, (sum, module) => sum + module.Capacity);
        var types = modules.Select(m => m.Type switch { 26 => "DDR4", 34 => "DDR5", 24 => "DDR3", 30 => "LPDDR4", 35 => "LPDDR5", _ => "" }).Where(t => t.Length > 0).Distinct().ToArray();
        var brands = modules.Select(MemoryBrand).Where(b => b.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var brand = brands.Length == 1 ? brands[0] + " · " : "";
        var details = new List<KeyValuePair<string, string>>();
        if (usable > 0) details.Add(new("Usable", $"{usable / 1073741824d:0.#} GB"));
        var speeds = modules.Select(m => m.Speed).Where(s => s > 0).Distinct().ToArray();
        if (speeds.Length == 1) details.Add(new("Speed", $"{speeds[0]} MT/s"));
        return new("Memory", $"{brand}{(total > 0 ? total : usable) / 1073741824d:0.#} GB {string.Join(" / ", types)}".Trim(), details, "memory");
    }

    public static string MemoryBrand(MemoryModuleInfo module)
    {
        var part = module.PartNumber.Trim().ToUpperInvariant(); var maker = module.Manufacturer.Trim();
        if (part.StartsWith("PVV5")) return "Patriot Viper Venom";
        if (part.StartsWith("PVXR5")) return "Patriot Viper Xtreme 5";
        if (part.StartsWith("PVS") || part.StartsWith("PVSR")) return "Patriot Viper Steel";
        if (part.StartsWith("PSD")) return "Patriot Signature";
        if (maker.Contains("Patriot", StringComparison.OrdinalIgnoreCase) || maker.Contains("PDP", StringComparison.OrdinalIgnoreCase)) return "Patriot";
        if (part.StartsWith("CMK") || part.StartsWith("CMH")) return "Corsair Vengeance";
        if (part.StartsWith("CMT")) return "Corsair Dominator";
        if (part.StartsWith("F5-") || part.StartsWith("F4-")) return "G.Skill";
        if (part.StartsWith("KF")) return "Kingston Fury";
        if (Regex.IsMatch(maker, @"^(?:Unknown|Undefined|Unavailable|Not Specified|0x[0-9A-F]+|[0-9A-F]{4}|\d+)$", RegexOptions.IgnoreCase)) return "";
        return maker;
    }

    public static string BoardName(string manufacturer, string model)
    {
        var maker = manufacturer.Contains("ASUST", StringComparison.OrdinalIgnoreCase) ? "ASUS" : manufacturer.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) ? "MSI" : manufacturer;
        maker = Regex.Replace(maker, @"\s*(?:Technology|International|Co\.,?\s*Ltd\.?|Corporation|Inc\.?)\s*", " ", RegexOptions.IgnoreCase).Trim(' ', '.');
        var product = HardwareNames.Clean(model);
        return product.StartsWith(maker, StringComparison.OrdinalIgnoreCase) ? product : (maker + " " + product).Trim();
    }

    public static bool IsMotherboardDriver(DriverDevice device)
    {
        var category = device.Category.ToUpperInvariant();
        if (category is "BLUETOOTH") return device.Id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) || device.Id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase);
        return category is "SYSTEM" or "NET" or "HDC" or "SCSIADAPTER" or "USB" && device.Id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(device.Name, "audio|sound|high definition", RegexOptions.IgnoreCase);
    }
}
