using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Absolute.Core;

namespace Absolute.App.Services;

internal sealed record PlaybookJournal(string Id,IReadOnlyList<SettingBackup> Settings);

internal static class PlaybookService
{
    private const string StateKey=@"Software\Absolute\Playbooks";
    private static readonly SemaphoreSlim ActivationGate=new(1,1);
    public static string? Active
    {
        get{using var key=Registry.CurrentUser.OpenSubKey(StateKey);return key?.GetValue("Active") as string;}
    }
    public static async Task ActivateAsync(string id)
    {
        await ActivationGate.WaitAsync();
        try{await ActivateCoreAsync(id);}finally{ActivationGate.Release();}
    }
    private static async Task ActivateCoreAsync(string id)
    {
        if(id!="restore"&&!Catalogs.Playbooks.Any(p=>p.Id==id&&p.Website is null))throw new InvalidOperationException("This external project cannot be applied without changing Windows files. Open its official project instead.");
        var sid=WindowsIdentity.GetCurrent().User?.Value??throw new InvalidOperationException("Cannot identify the current Windows user.");
        var start=new ProcessStartInfo(Environment.ProcessPath??throw new InvalidOperationException("Cannot locate ABSOLUTE.")){UseShellExecute=true,Verb="runas"};
        foreach(var argument in new[]{"--playbook",id,"--user",sid})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new InvalidOperationException("The playbook helper did not start.");
        await process.WaitForExitAsync();
        if(process.ExitCode!=0)
        {
            using var state=Registry.CurrentUser.OpenSubKey(StateKey);
            throw new InvalidOperationException(state?.GetValue("LastError") as string??"The playbook was not applied.");
        }
    }

    public static int ExecuteElevated(string id,string sid)
    {
        using var identity=WindowsIdentity.GetCurrent();
        if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))return 5;
        if(!new SecurityIdentifier(sid).IsAccountSid())return 5;
        using var user=Registry.Users.OpenSubKey(sid,true);if(user is null)return 5;
        using var state=user.CreateSubKey(StateKey);
        using var operation=new Mutex(false,@"Local\ABSOLUTE.Playbooks."+sid);
        var held=false;
        try
        {
            try{held=operation.WaitOne(TimeSpan.FromMinutes(4));}catch(AbandonedMutexException){held=true;}
            if(!held)throw new TimeoutException("Another settings playbook is still running.");
            if(id!="restore"&&!Catalogs.Playbooks.Any(p=>p.Id==id&&p.Website is null))throw new InvalidOperationException("Unknown settings profile.");
            var backend=new UserSettingsBackend(user);
            var previous=state.GetValue("Journal") is string saved?JsonSerializer.Deserialize<PlaybookJournal>(saved):null;
            // A Windows restore point is an emergency backup only. Switching uses our setting journal, never System Restore.
            if(id!="restore")CreateRestorePoint();
            if(previous is not null)SettingsTransaction.Restore(backend,previous.Settings);
            state.DeleteValue("Active",false);state.DeleteValue("Journal",false);
            if(id!="restore")
            {
                var journal=new PlaybookJournal(id,SettingsTransaction.Capture(backend,Edits(id)));
                state.SetValue("Journal",JsonSerializer.Serialize(journal));
                SettingsTransaction.Apply(backend,journal.Settings);
                state.SetValue("Active",id);
            }
            state.DeleteValue("LastError",false);return 0;
        }
        catch(Exception error){state.SetValue("LastError",error.Message);return 1;}
        finally{if(held)operation.ReleaseMutex();}
    }

    private static void CreateRestorePoint()
    {
        const string script="""
            $path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
            $item=Get-ItemProperty -Path $path -ErrorAction Stop
            $exists=$null -ne $item.PSObject.Properties['SystemRestorePointCreationFrequency']
            $original=$item.SystemRestorePointCreationFrequency
            try {
              New-ItemProperty -Path $path -Name SystemRestorePointCreationFrequency -Value 0 -PropertyType DWord -Force | Out-Null
              $before=@(Get-ComputerRestorePoint -ErrorAction Stop | Sort-Object SequenceNumber)[-1].SequenceNumber
              Checkpoint-Computer -Description 'ABSOLUTE settings profile' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
              $after=@(Get-ComputerRestorePoint -ErrorAction Stop | Sort-Object SequenceNumber)[-1].SequenceNumber
              if($null -eq $after -or $after -eq $before){throw 'Windows did not create a restore point. Enable System Protection for your Windows drive.'}
            } finally {
              if($exists){Set-ItemProperty -Path $path -Name SystemRestorePointCreationFrequency -Value $original}
              else{Remove-ItemProperty -Path $path -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue}
            }
            """;
        PowerShellRunner.RunAsync(script,CancellationToken.None,TimeSpan.FromMinutes(3)).GetAwaiter().GetResult();
    }

    private static IEnumerable<SettingEdit> Edits(string id)
    {
        yield return new(new(@"Software\Microsoft\GameBar","AutoGameModeEnabled"),new("DWord","1"));
        if(id is "competitive" or "focus")
        {
            yield return new(new(@"Software\Microsoft\Windows\CurrentVersion\GameDVR","AppCaptureEnabled"),new("DWord","0"));
            yield return new(new(@"System\GameConfigStore","GameDVR_Enabled"),new("DWord","0"));
        }
        if(id=="focus")yield return new(new(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications","ToastEnabled"),new("DWord","0"));
        if(id=="streaming")yield return new(new(@"Software\Microsoft\Windows\CurrentVersion\GameDVR","AppCaptureEnabled"),new("DWord","1"));
    }

    private sealed class UserSettingsBackend(RegistryKey user):ISettingsBackend
    {
        public SettingValue? Read(SettingAddress address)
        {
            using var key=user.OpenSubKey(address.Key);var value=key?.GetValue(address.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is null?null:new(key!.GetValueKind(address.Name).ToString(),value switch{byte[] bytes=>Convert.ToBase64String(bytes),string[] strings=>JsonSerializer.Serialize(strings),_=>value.ToString()!});
        }
        public void Write(SettingAddress address,SettingValue? value)
        {
            // Both path and value names originate exclusively from the compiled settings-profile catalog.
            if(!Allowed(address))throw new InvalidOperationException("Unsupported Windows setting.");
            using var key=user.CreateSubKey(address.Key);
            if(value is null)key.DeleteValue(address.Name,false);
            else
            {
                var kind=Enum.Parse<RegistryValueKind>(value.Kind);
                object restored=kind switch{RegistryValueKind.DWord=>int.Parse(value.Data,System.Globalization.CultureInfo.InvariantCulture),RegistryValueKind.QWord=>long.Parse(value.Data,System.Globalization.CultureInfo.InvariantCulture),RegistryValueKind.Binary or RegistryValueKind.None=>Convert.FromBase64String(value.Data),RegistryValueKind.MultiString=>JsonSerializer.Deserialize<string[]>(value.Data)??[],_=>value.Data};
                key.SetValue(address.Name,restored,kind);
            }
        }
        private static bool Allowed(SettingAddress address)=>Catalogs.Playbooks.Where(p=>p.Website is null).SelectMany(p=>Edits(p.Id)).Any(e=>e.Address==address);
    }
}
