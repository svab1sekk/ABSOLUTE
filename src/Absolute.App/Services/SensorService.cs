using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Absolute.Core;

namespace Absolute.App.Services;

internal sealed record SensorSnapshot(double? CpuTemperature, double? GpuTemperature, double? GpuLoad, IReadOnlyList<SensorReading> Readings);

internal sealed class SensorService : IDisposable
{
    private readonly List<string> providers=[];
    private readonly List<PerformanceCounter> gpuCounters=[];
    private DateTimeOffset nextGpuDiscovery;
    private bool probed;
    private IntPtr nvml;
    private NvmlHandle? getHandle;
    private NvmlTemperature? getTemperature;

    public SensorSnapshot Read(bool reprobe=false,TemperaturePacket? builtIn=null)
    {
        if(!probed||reprobe)
        {
            providers.Clear();
            foreach(var scope in new[]{@"root\LibreHardwareMonitor",@"root\OpenHardwareMonitor"})
                try{var connection=new ManagementScope(scope);connection.Connect();providers.Add(scope);}catch(Exception ex) when(ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException){}
            if(!probed) InitializeNvml();
            probed=true;
        }
        var readings=new List<SensorReading>();
        foreach(var provider in providers)
            try
            {
                foreach(var sensor in HardwareDiscovery.Query("SELECT Name,Parent,SensorType,Value FROM Sensor",provider))
                    if(float.TryParse(sensor.GetValueOrDefault("Value")?.ToString(),out var value)&&float.IsFinite(value))
                        readings.Add(new(sensor.GetValueOrDefault("Name")?.ToString()??"Sensor",sensor.GetValueOrDefault("Parent")?.ToString()??"",sensor.GetValueOrDefault("SensorType")?.ToString()??"",value));
            }
            catch(Exception ex) when(ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException){}
        double? Temperature(string hardware)
        {
            return SensorTemperatures.Read(readings,hardware);
        }
        var gpuTemperature=Temperature("gpu");
        if(gpuTemperature is null&&getHandle is not null&&getTemperature is not null&&getHandle(0,out var handle)==0&&getTemperature(handle,0,out var temperature)==0&&temperature is >0 and <150)gpuTemperature=temperature;
        if(builtIn is not null)readings.AddRange(builtIn.Readings);
        return new(builtIn?.Cpu??Temperature("cpu"),builtIn?.Gpu??gpuTemperature,GpuLoad()??readings.Where(r=>r.Type.Equals("Load",StringComparison.OrdinalIgnoreCase)&&r.Hardware.Contains("gpu",StringComparison.OrdinalIgnoreCase)&&r.Name.Contains("Core",StringComparison.OrdinalIgnoreCase)).Select(r=>(double?)Math.Clamp(r.Value,0,100)).FirstOrDefault(),readings);
    }

    private double? GpuLoad()
    {
        try
        {
            if(DateTimeOffset.UtcNow>nextGpuDiscovery)
            {
                foreach(var counter in gpuCounters)counter.Dispose();gpuCounters.Clear();
                if(PerformanceCounterCategory.Exists("GPU Engine"))
                    foreach(var name in new PerformanceCounterCategory("GPU Engine").GetInstanceNames().Where(n=>n.Contains("_engtype_",StringComparison.OrdinalIgnoreCase)))
                    {var counter=new PerformanceCounter("GPU Engine","Utilization Percentage",name,true);counter.NextValue();gpuCounters.Add(counter);}
                nextGpuDiscovery=DateTimeOffset.UtcNow.AddSeconds(30);
                return null;
            }
            if(gpuCounters.Count==0)return null;
            return Math.Clamp(gpuCounters.GroupBy(c=>Regex.Match(c.InstanceName,@"luid_.*?_phys_\d+_eng_\d+").Value).Max(g=>g.Sum(c=>(double)c.NextValue())),0,100);
        }
        catch(Exception ex) when(ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){return null;}
    }

    private void InitializeNvml()
    {
        foreach(var path in new[]{Path.Combine(Environment.SystemDirectory,"nvml.dll"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","NVSMI","nvml.dll")})
        {
            if(!NativeLibrary.TryLoad(path,out nvml))continue;
            try
            {
                var init=Marshal.GetDelegateForFunctionPointer<NvmlInit>(NativeLibrary.GetExport(nvml,"nvmlInit_v2"));
                if(init()!=0)break;
                getHandle=Marshal.GetDelegateForFunctionPointer<NvmlHandle>(NativeLibrary.GetExport(nvml,"nvmlDeviceGetHandleByIndex_v2"));
                getTemperature=Marshal.GetDelegateForFunctionPointer<NvmlTemperature>(NativeLibrary.GetExport(nvml,"nvmlDeviceGetTemperature"));
                return;
            }
            catch(EntryPointNotFoundException){}
            NativeLibrary.Free(nvml);nvml=IntPtr.Zero;
        }
    }
    public void Dispose(){foreach(var c in gpuCounters)c.Dispose();if(nvml!=IntPtr.Zero)NativeLibrary.Free(nvml);}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int NvmlInit();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int NvmlHandle(uint index,out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int NvmlTemperature(IntPtr device,uint sensor,out uint temperature);
}
