using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Absolute.Core;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Absolute.App.Services;

internal static class TemperatureHost
{
    internal const string DriverHash="a3a46226c5e2824f4cdd42be0eecbabfc672c86f7889710f5ab1e6ad385b47a0";
    public static string DriverPath=>Path.Combine(AppContext.BaseDirectory,"Dependencies","PawnIO_setup.exe");
    public static bool BundledDriverIsValid()=>File.Exists(DriverPath)&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(DriverPath))).Equals(DriverHash,StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(int parentId,long parentStarted,string token)
    {
        if(token.Length!=32||!Guid.TryParseExact(token,"N",out _))return 2;
        using var identity=WindowsIdentity.GetCurrent();if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))return 5;
        using var parent=Process.GetProcessById(parentId);using var current=Process.GetCurrentProcess();
        if(parent.StartTime.ToUniversalTime().Ticks!=parentStarted||parent.SessionId!=current.SessionId||!string.Equals(parent.MainModule?.FileName,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))return 5;
        using var pipe=new NamedPipeClientStream(".",$"ABSOLUTE.Sensors.{parentId}.{token}",PipeDirection.Out,PipeOptions.Asynchronous);
        using var connectTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));await pipe.ConnectAsync(connectTimeout.Token);
        if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var serverId)||serverId!=parentId)return 5;
        using var writer=new StreamWriter(pipe){AutoFlush=true};
        try
        {
            await Send(new(null,null,[],"Preparing sensors…"));
            InstallDriverIfNeeded();
            var computer=new Computer{IsCpuEnabled=true,IsGpuEnabled=true};
            try
            {
                computer.Open();
                while(!parent.HasExited&&pipe.IsConnected)
                {
                    var readings=new List<SensorReading>();double? cpu=null,gpu=null;
                    foreach(var hardware in computer.Hardware)Read(hardware);
                    cpu=SensorTemperatures.Read(readings,"cpu");gpu=SensorTemperatures.Read(readings,"gpu");
                    await Send(new(cpu,gpu,readings,cpu is not null||gpu is not null?"Built-in sensors":"No compatible temperature sensors",true));
                    await Task.Delay(1800);
                    void Read(IHardware hardware)
                    {
                        hardware.Update();
                        foreach(var sensor in hardware.Sensors)
                        {
                            if(sensor.Value is not float value||!float.IsFinite(value)||sensor.SensorType!=SensorType.Temperature||value is <=0 or >=150)continue;
                            readings.Add(new(sensor.Name,hardware.Identifier.ToString(),"Temperature",value));
                        }
                        foreach(var child in hardware.SubHardware)Read(child);
                    }
                }
            }
            finally{computer.Close();}
            return 0;
        }
        catch(Exception ex)
        {
            try{await Send(new(null,null,[],ex.Message));}catch(IOException){}
            return 1;
        }
        Task Send(TemperaturePacket packet)=>writer.WriteLineAsync(JsonSerializer.Serialize(packet));
    }

    private static void InstallDriverIfNeeded()
    {
        using var registry=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);
        using var installed=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        if(Version.TryParse(installed?.GetValue("DisplayVersion") as string,out var version)&&version>=new Version(2,0,0))return;
        if(!BundledDriverIsValid())throw new InvalidOperationException("The bundled sensor driver is missing or failed verification.");
        var start=new ProcessStartInfo(DriverPath){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("-install");
        using var process=Process.Start(start)??throw new InvalidOperationException("The sensor driver could not start.");
        if(!process.WaitForExit(90000)){process.Kill(true);throw new TimeoutException("Sensor driver setup did not finish.");}
        if(process.ExitCode!=0)throw new InvalidOperationException("Windows could not install the sensor driver.");
    }
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out int processId);
}
