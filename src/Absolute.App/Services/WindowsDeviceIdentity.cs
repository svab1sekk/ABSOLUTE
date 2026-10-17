using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Absolute.Core;
using HidSharp;
using Microsoft.Win32;

namespace Absolute.App.Services;

internal static class WindowsDeviceIdentity
{
    public static IReadOnlyList<HardwarePart> ReadMonitors()
    {
        var result=new Dictionary<string,HardwarePart>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in HardwareDiscovery.Query("SELECT Active,InstanceName,ManufacturerName,UserFriendlyName FROM WmiMonitorID",@"root\wmi"))
        {
            if(item.GetValueOrDefault("Active") is not true)continue;
            var instance=item.GetValueOrDefault("InstanceName")?.ToString()??"";
            instance=Regex.Replace(instance,@"_\d+$","");
            using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\"+instance+@"\Device Parameters");
            var edid=key?.GetValue("EDID") is byte[] bytes?HardwareIdentity.ReadEdid(bytes):null;
            var model=Decode(item.GetValueOrDefault("UserFriendlyName"));
            var manufacturer=Decode(item.GetValueOrDefault("ManufacturerName"));
            var name=HardwareIdentity.MonitorName(model.Length>0?model:edid?.Name??"",manufacturer);
            if(!HardwareIdentity.IsSpecificPeripheral(name)||name.Length<3)continue;
            var mode=CurrentDisplayMode(instance);
            var details=new List<KeyValuePair<string,string>>();
            if(mode is not null)details.Add(new("Display",mode));
            else if(edid is {Width:>0,Height:>0})details.Add(new("Native",$"{edid.Width} × {edid.Height}"+(edid.RefreshRate>0?$" · {edid.RefreshRate:0.#} Hz":"")));
            // Only active display instances participate. Serial is used internally to merge duplicate EDID reports.
            var physical=edid is {Serial.Length:>0}?edid.Manufacturer+edid.Product+edid.Serial:instance;
            var code=instance.Split('\\').ElementAtOrDefault(1)??"";
            result.TryAdd(physical,new("Monitor",name,details,instance,["MONITOR\\"+code]));
        }
        return result.Values.ToArray();
    }

    public static IReadOnlyList<HardwarePart> ReadPeripherals()
    {
        uint count=0;var size=(uint)Marshal.SizeOf<RawInputDevice>();
        if(GetRawInputDeviceList(null,ref count,size)==uint.MaxValue)throw new Win32Exception();
        if(count==0)return [];
        var raw=new RawInputDevice[count];
        if(GetRawInputDeviceList(raw,ref count,size)==uint.MaxValue)throw new Win32Exception();
        var hid=DeviceList.Local.GetHidDevices().ToArray();var parts=new Dictionary<string,HardwarePart>(StringComparer.OrdinalIgnoreCase);
        foreach(var device in raw.Take((int)count).Where(d=>d.Type is 0 or 1))
        {
            uint length=0;GetRawInputDeviceInfo(device.Handle,0x20000007,null,ref length);
            if(length is 0 or >32768)continue;
            var path=new StringBuilder((int)length+1);if(GetRawInputDeviceInfo(device.Handle,0x20000007,path,ref length)==uint.MaxValue)continue;
            var devicePath=path.ToString();var instance=ToInstance(devicePath);if(instance.Length==0)continue;
            var match=hid.FirstOrDefault(h=>h.DevicePath.Equals(devicePath,StringComparison.OrdinalIgnoreCase));
            var name="";
            try{if(match is not null)name=match.GetProductName();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
            var node=Locate(instance);
            if(!HardwareIdentity.IsSpecificPeripheral(name))name=BusName(node);
            if(!HardwareIdentity.IsSpecificPeripheral(name))continue;
            name=HardwareNames.Clean(Regex.Replace(name,@"\s*\((?:HID|USB)\)\s*","",RegexOptions.IgnoreCase));
            var vendor=Regex.Match(devicePath,@"VID_([0-9A-F]{4})",RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
            var brand=vendor switch{"046D"=>"Logitech","1532"=>"Razer","1B1C"=>"Corsair","1038"=>"SteelSeries","31E3"=>"Wooting","3434"=>"Keychron",_=>""};
            if(brand.Length>0&&!name.Contains(brand,StringComparison.OrdinalIgnoreCase))name=brand+" "+name;
            var category=device.Type==1?"Keyboard":"Mouse";
            var container=Container(node);var identity=container.Length>0?container:instance;
            var product=Regex.Match(devicePath,@"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}",RegexOptions.IgnoreCase).Value;
            var part=new HardwarePart(category,name,[],instance,product.Length>0?["HID\\"+product,"USB\\"+product]:[]);
            if(parts.TryGetValue(identity,out var previous))
            {
                // Gaming keyboards can expose an extra mouse interface for macros.
                if(category=="Keyboard"&&!name.Contains("mouse",StringComparison.OrdinalIgnoreCase))parts[identity]=part;
                else if(previous.Category!="Keyboard")parts[identity]=part;
            }
            else parts.Add(identity,part);
        }
        return parts.Values.ToArray();
    }

    public static IReadOnlyList<HardwarePart> ReadAudio()
    {
        var parts=new Dictionary<string,HardwarePart>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in HardwareDiscovery.Query("SELECT Name,DeviceID,HardwareID FROM Win32_PnPEntity WHERE PNPClass='MEDIA'"))
        {
            var id=item.GetValueOrDefault("DeviceID")?.ToString()??"";
            if(!id.StartsWith("USB\\",StringComparison.OrdinalIgnoreCase)&&!id.StartsWith("BTHENUM\\",StringComparison.OrdinalIgnoreCase))continue;
            var name=item.GetValueOrDefault("Name")?.ToString()??"";var node=Locate(id);
            if(!HardwareIdentity.IsSpecificPeripheral(name))name=BusName(node);
            if(!HardwareIdentity.IsSpecificPeripheral(name))continue;
            if(Regex.IsMatch(name,@"monitor|display|TITAN\s*ARMY|\bDell\b|\bBenQ\b|\bAOC\b|ViewSonic|\bPhilips\b|\bASUS\b|\bLG\b",RegexOptions.IgnoreCase))continue;
            var container=Container(node);var identity=container.Length>0?container:id;
            parts.TryAdd(identity,new("Audio",HardwareNames.Clean(name),[],id,item.GetValueOrDefault("HardwareID") as string[]??[]));
        }
        return parts.Values.ToArray();
    }

    private static string Decode(object? value)=>value is ushort[] chars?new string(chars.TakeWhile(c=>c!=0).Select(c=>(char)c).ToArray()).Trim():"";
    private static string ToInstance(string path)
    {
        var value=path.StartsWith(@"\\?\",StringComparison.Ordinal)?path[4..]:path;
        var end=value.IndexOf("#{",StringComparison.Ordinal);if(end>=0)value=value[..end];
        return value.Replace('#','\\');
    }
    private static uint Locate(string id)=>CM_Locate_DevNode(out var node,id,0)==0?node:0;
    private static string BusName(uint node)
    {
        for(var depth=0;node!=0&&depth<4;depth++)
        {
            var key=new DevicePropertyKey(new("540B947E-8B40-45BC-A8A2-6A0B894CBDA2"),4);
            var buffer=new byte[4096];uint length=(uint)buffer.Length;
            if(CM_Get_DevNode_Property(node,ref key,out _,buffer,ref length,0)==0)
            {
                var value=Encoding.Unicode.GetString(buffer,0,(int)length).TrimEnd('\0');
                if(HardwareIdentity.IsSpecificPeripheral(value))return value;
            }
            if(CM_Get_Parent(out var parent,node,0)!=0)break;node=parent;
        }
        return "";
    }
    private static string Container(uint node)
    {
        if(node==0)return "";
        var key=new DevicePropertyKey(new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),2);var buffer=new byte[16];uint length=16;
        return CM_Get_DevNode_Property(node,ref key,out _,buffer,ref length,0)==0&&length==16&&new Guid(buffer)!=Guid.Empty?new Guid(buffer).ToString():"";
    }
    private static string? CurrentDisplayMode(string instance)
    {
        var model=instance.Split('\\').ElementAtOrDefault(1);if(string.IsNullOrEmpty(model))return null;
        for(uint index=0;index<32;index++)
        {
            var adapter=new DisplayDevice{Size=Marshal.SizeOf<DisplayDevice>()};if(!EnumDisplayDevices(null,index,ref adapter,0))break;
            if((adapter.State&1)==0)continue;
            for(uint monitor=0;monitor<16;monitor++)
            {
                var display=new DisplayDevice{Size=Marshal.SizeOf<DisplayDevice>()};if(!EnumDisplayDevices(adapter.Name,monitor,ref display,1))break;
                if(!ToInstance(display.Id).Equals(instance,StringComparison.OrdinalIgnoreCase))continue;
                var mode=new DisplayMode{Size=(ushort)Marshal.SizeOf<DisplayMode>()};
                if(EnumDisplaySettings(adapter.Name,-1,ref mode))return $"{mode.Width} × {mode.Height}"+(mode.Frequency>1?$" · {mode.Frequency} Hz":"");
            }
        }
        return null;
    }
    [StructLayout(LayoutKind.Sequential)]private readonly struct DevicePropertyKey(Guid format,uint id){public readonly Guid Format=format;public readonly uint Id=id;}
    [StructLayout(LayoutKind.Sequential)]private struct RawInputDevice{public IntPtr Handle;public uint Type;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct DisplayDevice{public int Size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;public uint State;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct DisplayMode
    {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string DeviceName;public ushort SpecVersion,DriverVersion,Size,DriverExtra;public uint Fields;public int X,Y;public uint Orientation,FixedOutput;public short Color,Duplex,YResolution,TtOption,Collate;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string FormName;public ushort LogPixels;public uint Bits,Width,Height,Flags,Frequency,IcmMethod,IcmIntent,MediaType,DitherType,Reserved1,Reserved2,PanningWidth,PanningHeight;
    }
    [DllImport("user32.dll",SetLastError=true)]private static extern uint GetRawInputDeviceList([In,Out]RawInputDevice[]? devices,ref uint count,uint size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,StringBuilder? data,ref uint size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool EnumDisplayDevices(string? name,uint index,ref DisplayDevice device,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool EnumDisplaySettings(string name,int mode,ref DisplayMode settings);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]private static extern uint CM_Locate_DevNode(out uint node,string deviceId,uint flags);
    [DllImport("cfgmgr32.dll")]private static extern uint CM_Get_Parent(out uint parent,uint node,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,EntryPoint="CM_Get_DevNode_PropertyW")]private static extern uint CM_Get_DevNode_Property(uint node,ref DevicePropertyKey key,out uint type,byte[] data,ref uint size,uint flags);
}
