using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Absolute.Core;

namespace Absolute.App.Services;

internal sealed record TemperaturePacket(double? Cpu,double? Gpu,IReadOnlyList<SensorReading> Readings,string Status,bool Ready=false);

internal sealed class TemperatureMonitor:IDisposable
{
    private readonly CancellationTokenSource lifetime=new();
    private NamedPipeServerStream? pipe;
    private volatile TemperaturePacket? snapshot;
    private bool started;
    private long received;
    private TaskCompletionSource<bool> firstReading=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TemperaturePacket? Snapshot=>snapshot is {} value&&(value.Cpu is not null||value.Gpu is not null)&&Environment.TickCount64-Interlocked.Read(ref received)>10000?new(null,null,[],"Sensor connection lost"):snapshot;
    public string Status=>Snapshot?.Status??"Starting sensors…";
    public Task FirstReading=>firstReading.Task;

    public async Task StartAsync(bool allowElevation=true)
    {
        if(started||lifetime.IsCancellationRequested)return;started=true;
        try
        {
            using var identity=WindowsIdentity.GetCurrent();
            var administrator=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            if(!administrator&&!allowElevation){snapshot=new(null,null,[],"Administrator access required");firstReading.TrySetResult(false);return;}
            var token=Guid.NewGuid().ToString("N");using var current=Process.GetCurrentProcess();
            var name=$"ABSOLUTE.Sensors.{current.Id}.{token}";
            var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);
            security.AddAccessRule(new PipeAccessRule(identity.User!,PipeAccessRights.FullControl,AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
            pipe=NamedPipeServerStreamAcl.Create(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,65536,security);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var connection=pipe.WaitForConnectionAsync(timeout.Token);
            var start=new ProcessStartInfo(Environment.ProcessPath??throw new InvalidOperationException("Cannot find ABSOLUTE.")){UseShellExecute=!administrator,CreateNoWindow=administrator};
            if(!administrator)start.Verb="runas";
            foreach(var argument in new[]{"--sensor-host",current.Id.ToString(),current.StartTime.ToUniversalTime().Ticks.ToString(),token})start.ArgumentList.Add(argument);
            using var helper=Process.Start(start)??throw new InvalidOperationException("The sensor helper did not start.");
            while(!connection.IsCompleted)
            {
                if(helper.HasExited)throw new InvalidOperationException($"The sensor helper exited before connecting (code {helper.ExitCode}).");
                await Task.WhenAny(connection,Task.Delay(200,timeout.Token));
                timeout.Token.ThrowIfCancellationRequested();
            }
            await connection;
            if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out var processId)||processId!=helper.Id)throw new InvalidOperationException("The sensor connection was not the launched helper.");
            _=ReadAsync(pipe);
        }
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
        {
            snapshot=new(null,null,[],ex is System.ComponentModel.Win32Exception {NativeErrorCode:1223}?"Administrator access required":ex.Message);
            firstReading.TrySetResult(false);pipe?.Dispose();pipe=null;started=false;
        }
    }

    private async Task ReadAsync(NamedPipeServerStream stream)
    {
        try
        {
            using var reader=new StreamReader(stream);
            while(!lifetime.IsCancellationRequested)
            {
                var line=await reader.ReadLineAsync(lifetime.Token);if(line is null)break;
                if(line.Length>262144)throw new InvalidDataException("Invalid sensor packet.");
                var packet=JsonSerializer.Deserialize<TemperaturePacket>(line);if(packet is null)continue;
                Interlocked.Exchange(ref received,Environment.TickCount64);snapshot=packet;
                if(packet.Status!="Preparing sensors…")firstReading.TrySetResult(true);
            }
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException or JsonException){if(!lifetime.IsCancellationRequested)snapshot=new(null,null,[],ex.Message);}
        finally{firstReading.TrySetResult(false);if(ReferenceEquals(pipe,stream)){started=false;if(!lifetime.IsCancellationRequested&&(snapshot?.Ready==true||snapshot?.Status=="Preparing sensors…"))snapshot=new(null,null,[],"Sensor connection closed");}}
    }
    public void Restart(){if(started)return;firstReading=new(TaskCreationOptions.RunContinuationsAsynchronously);}
    public void Stop(){var previous=pipe;pipe=null;started=false;snapshot=null;firstReading.TrySetResult(false);previous?.Dispose();}
    public void Dispose(){lifetime.Cancel();pipe?.Dispose();lifetime.Dispose();}
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out int processId);
}
