using System.Diagnostics;
using System.IO;
using System.Text;

namespace Absolute.App.Services;

internal static class PowerShellRunner
{
    public static async Task<string> RunAsync(string script,CancellationToken cancellationToken,TimeSpan? timeout=null)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"))
        {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(var argument in new[]{"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';"+script))})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new InvalidOperationException("Windows PowerShell could not start.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);limit.CancelAfter(timeout??TimeSpan.FromMinutes(3));
        try{await process.WaitForExitAsync(limit.Token);}
        catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);cancellationToken.ThrowIfCancellationRequested();throw new TimeoutException("The Windows operation timed out.");}
        var result=await output;var failure=await error;
        if(process.ExitCode!=0)throw new InvalidOperationException(string.IsNullOrWhiteSpace(failure)?result.Trim():failure.Trim());
        return result.Trim();
    }
}
