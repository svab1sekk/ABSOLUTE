using System.IO;
using System.Management;
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
        Read("Processor", "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,DeviceID FROM Win32_Processor", x => Part("Processor", S(x,"Name"),
            ("Cores",S(x,"NumberOfCores")),("Threads",S(x,"NumberOfLogicalProcessors"))) with { Id=S(x,"DeviceID") });
        Read("Graphics", "SELECT Name,PNPDeviceID FROM Win32_VideoController", x => Part("Graphics",GpuProductNames.Resolve(S(x,"Name"),S(x,"PNPDeviceID"))) with { Id=S(x,"PNPDeviceID") });
        try
        {
            var modules=Query("SELECT Capacity,Manufacturer,PartNumber,ConfiguredClockSpeed,SMBIOSMemoryType FROM Win32_PhysicalMemory")
                .Select(x=>new MemoryModuleInfo(S(x,"Manufacturer"),S(x,"PartNumber"),U(x,"Capacity"),I(x,"SMBIOSMemoryType"),I(x,"ConfiguredClockSpeed"))).ToArray();
            installed=modules.Aggregate(0UL,(sum,module)=>sum+module.Capacity);
            parts.Add(HardwareIdentity.CombineMemory(modules,WindowsSystemService.UsableMemory()));
        }
        catch(Exception ex) when(IsUnavailable(ex)){unavailable.Add("Memory");}
        var bios="";
        try{bios=Query("SELECT SMBIOSBIOSVersion FROM Win32_BIOS").Select(x=>S(x,"SMBIOSBIOSVersion")).FirstOrDefault()??"";}catch(Exception ex) when(IsUnavailable(ex)){}
        Read("Motherboard", "SELECT Manufacturer,Product FROM Win32_BaseBoard", x=>Part("Motherboard",HardwareIdentity.BoardName(S(x,"Manufacturer"),S(x,"Product")),("BIOS",bios)) with{Id="motherboard"});
        try
        {
            foreach(var disk in Query("SELECT FriendlyName,Model,MediaType,BusType,Size,DeviceId FROM MSFT_PhysicalDisk",@"root\Microsoft\Windows\Storage"))
            {
                var type=I(disk,"BusType")==17?"NVMe SSD":I(disk,"MediaType") switch{3=>"HDD",4=>"SSD",_=>""};
                var model=S(disk,"Model");if(model=="Unavailable"||string.IsNullOrWhiteSpace(model))model=S(disk,"FriendlyName");
                parts.Add(Part("Storage",model,("Capacity",$"{U(disk,"Size")/1073741824d:0.#} GB"),("Drive",type)) with{Id="disk-"+S(disk,"DeviceId")});
            }
        }
        catch(Exception ex) when(IsUnavailable(ex)){}
        if(!parts.Any(p=>p.Category=="Storage"))Read("Storage","SELECT Model,Size,PNPDeviceID FROM Win32_DiskDrive",x=>Part("Storage",S(x,"Model"),("Capacity",$"{U(x,"Size")/1073741824d:0.#} GB")) with{Id=S(x,"PNPDeviceID")});
        try{parts.AddRange(WindowsDeviceIdentity.ReadMonitors());}catch(Exception ex) when(IsUnavailable(ex)){unavailable.Add("Monitors");}
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
    private static int I(Dictionary<string,object?> x,string key)=>int.TryParse(S(x,key),out var value)?value:0;
    private static bool IsUnavailable(Exception ex)=>ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or TimeoutException or IOException or System.ComponentModel.Win32Exception;
    private static string Date(string value)=>value.Length>=8&&DateTime.TryParseExact(value[..8],"yyyyMMdd",null,System.Globalization.DateTimeStyles.None,out var date)?date.ToString("yyyy-MM-dd"):value;
}
