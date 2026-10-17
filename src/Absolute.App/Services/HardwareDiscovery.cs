using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using Absolute.Core;

namespace Absolute.App.Services;

internal sealed record HardwareDiscoveryResult(IReadOnlyList<HardwarePart> Parts, ulong InstalledMemory, IReadOnlyList<string> Unavailable);

internal static class HardwareDiscovery
{
    internal static IEnumerable<Dictionary<string,object?>> Query(string query, string scope = @"root\cimv2")
    {
        using var search = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query), new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(12), ReturnImmediately = false });
        using var results = search.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
                yield return item.Properties.Cast<PropertyData>().ToDictionary(p => p.Name, p => (object?)p.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static HardwareDiscoveryResult ReadParts()
    {
        var parts = new List<HardwarePart>(); var unavailable = new List<string>(); ulong installed = 0;
        void Read(string category, string query, Func<Dictionary<string,object?>, HardwarePart> convert)
        {
            try { parts.AddRange(Query(query).Select(convert)); }
            catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or TimeoutException) { unavailable.Add(category); }
        }
        Read("Processor", "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,SocketDesignation,L2CacheSize,L3CacheSize,AddressWidth FROM Win32_Processor", x => Part("Processor", S(x,"Name"),
            ("Cores",S(x,"NumberOfCores")),("Threads",S(x,"NumberOfLogicalProcessors")),("Maximum clock",S(x,"MaxClockSpeed")+" MHz"),("Socket",S(x,"SocketDesignation")),("L3 cache",S(x,"L3CacheSize")+" KB"),("Architecture",S(x,"AddressWidth")+" bit")));
        Read("Graphics", "SELECT Name,VideoProcessor,DriverVersion,DriverDate,VideoModeDescription,PNPDeviceID FROM Win32_VideoController", x => Part("Graphics",S(x,"Name"),
            ("Chip",S(x,"VideoProcessor")),("Driver",S(x,"DriverVersion")),("Display mode",S(x,"VideoModeDescription"))));
        Read("Memory", "SELECT Capacity,Manufacturer,PartNumber,Speed,ConfiguredClockSpeed,DeviceLocator,SMBIOSMemoryType FROM Win32_PhysicalMemory", x =>
        {
            var capacity=U(x,"Capacity"); installed+=capacity;
            var type=Convert.ToInt32(x.GetValueOrDefault("SMBIOSMemoryType")??0) switch {26=>"DDR4",34=>"DDR5",24=>"DDR3",30=>"LPDDR4",35=>"LPDDR5",_=>"RAM"};
            return Part("Memory", $"{S(x,"DeviceLocator")} · {capacity/1073741824d:0.#} GB {type}", ("Manufacturer",S(x,"Manufacturer")),("Part",S(x,"PartNumber")),("Configured speed",S(x,"ConfiguredClockSpeed")+" MT/s"),("Rated speed",S(x,"Speed")+" MT/s"));
        });
        Read("Motherboard", "SELECT Manufacturer,Product,Version,SerialNumber FROM Win32_BaseBoard", x=>Part("Motherboard",S(x,"Manufacturer")+" "+S(x,"Product"),("Revision",S(x,"Version")),("Serial",S(x,"SerialNumber"))));
        Read("Firmware", "SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS",x=>Part("Firmware",S(x,"Manufacturer"),("Version",S(x,"SMBIOSBIOSVersion")),("Released",Date(S(x,"ReleaseDate")))));
        Read("Storage", "SELECT Model,Size,InterfaceType,MediaType,FirmwareRevision,Status FROM Win32_DiskDrive",x=>Part("Storage",S(x,"Model"),("Capacity",$"{U(x,"Size")/1073741824d:N0} GB"),("Interface",S(x,"InterfaceType")),("Type",S(x,"MediaType")),("Firmware",S(x,"FirmwareRevision")),("Status",S(x,"Status"))));
        Read("Peripherals", "SELECT Name,Manufacturer,PNPClass,Status FROM Win32_PnPEntity WHERE PNPClass='Mouse' OR PNPClass='Keyboard' OR PNPClass='Monitor' OR PNPClass='MEDIA' OR PNPClass='AudioEndpoint' OR PNPClass='Battery'",x=>Part(S(x,"PNPClass"),S(x,"Name"),("Manufacturer",S(x,"Manufacturer")),("Status",S(x,"Status"))));
        Read("Cooling", "SELECT Name,Description,Status FROM Win32_Fan",x=>Part("Cooling",S(x,"Name"),("Description",S(x,"Description")),("Status",S(x,"Status"))));
        foreach(var disk in DriveInfo.GetDrives().Where(d=>d.IsReady&&d.DriveType==DriveType.Fixed))
            parts.Add(Part("Volume",disk.Name+(string.IsNullOrWhiteSpace(disk.VolumeLabel)?"":" · "+disk.VolumeLabel),("Capacity",$"{disk.TotalSize/1073741824d:N0} GB"),("Available",$"{disk.AvailableFreeSpace/1073741824d:N0} GB"),("File system",disk.DriveFormat)));
        foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.NetworkInterfaceType!=NetworkInterfaceType.Loopback))
            parts.Add(Part("Network",nic.Description,("Connection",nic.OperationalStatus.ToString()),("Link speed",$"{Math.Max(0,nic.Speed)/1_000_000:N0} Mbps"),("MAC",nic.GetPhysicalAddress().ToString()),("Adapter",nic.Name)));
        return new(parts,installed,unavailable);
    }

    public static IReadOnlyList<DriverDevice> ReadDrivers()
    {
        var pnp=Query("SELECT DeviceID,Name,PNPClass,Manufacturer,HardwareID,ConfigManagerErrorCode FROM Win32_PnPEntity").GroupBy(x=>S(x,"DeviceID"),StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
        var devices=Query("SELECT DeviceID,DeviceName,DeviceClass,Manufacturer,DriverProviderName,DriverVersion,DriverDate,HardWareID,IsSigned FROM Win32_PnPSignedDriver")
            .Select(x=>new DriverDevice(S(x,"DeviceID"),HardwareNames.Clean(S(x,"DeviceName")),S(x,"DeviceClass"),S(x,"Manufacturer"),S(x,"DriverProviderName"),S(x,"DriverVersion"),Date(S(x,"DriverDate")),
                (pnp.GetValueOrDefault(S(x,"DeviceID"))?.GetValueOrDefault("HardwareID") as string[]??[]).Concat(x.GetValueOrDefault("HardWareID") switch{string[] ids=>ids,string id=>[id],_=>Array.Empty<string>()}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),x.GetValueOrDefault("IsSigned") is bool signed?signed:null,
                pnp.GetValueOrDefault(S(x,"DeviceID"))?.GetValueOrDefault("ConfigManagerErrorCode") is object code?Convert.ToInt32(code):null)).ToList();
        var known=devices.Select(d=>d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var (id,device) in pnp.Where(p=>!known.Contains(p.Key)))
            devices.Add(new(id,HardwareNames.Clean(S(device,"Name")),S(device,"PNPClass"),S(device,"Manufacturer"),"Not reported","Not reported","Not reported",device.GetValueOrDefault("HardwareID") as string[]??[],null,device.GetValueOrDefault("ConfigManagerErrorCode") is object code?Convert.ToInt32(code):null));
        return devices.OrderBy(x=>x.Category).ThenBy(x=>x.Name).ToArray();
    }

    private static HardwarePart Part(string category,string name,params (string Key,string Value)[] details) => new(category,HardwareNames.Clean(name),details.Where(x=>!string.IsNullOrWhiteSpace(x.Value)&&x.Value!="Unavailable").Select(x=>new KeyValuePair<string,string>(x.Key,HardwareNames.Clean(x.Value))).ToArray());
    private static string S(Dictionary<string,object?> x,string key)=>x.GetValueOrDefault(key)?.ToString()?.Trim()??"Unavailable";
    private static ulong U(Dictionary<string,object?> x,string key)=>ulong.TryParse(S(x,key),out var value)?value:0;
    private static string Date(string value)=>value.Length>=8&&DateTime.TryParseExact(value[..8],"yyyyMMdd",null,System.Globalization.DateTimeStyles.None,out var date)?date.ToString("yyyy-MM-dd"):value;
}
