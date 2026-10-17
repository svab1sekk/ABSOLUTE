using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Absolute.Core;

namespace Absolute.App.Services;

internal sealed record DetectedGame(GameDefinition Game,string? Executable,int ProcessId);

internal sealed class GamePresetService(AppPreferences preferences,PreferenceStore store)
{
    private readonly SemaphoreSlim gate=new(1,1);
    public string? ActiveGame=>preferences.GameSession?.GameId;
    private string status="Waiting for a game";

    public static DetectedGame? Detect(IEnumerable<GameDefinition> games,string? selected)
    {
        var catalog=games.ToArray();var libraries=SteamGameLibrary.Installed(catalog);var matches=new List<(DetectedGame Match,bool Foreground)>();
        using var currentProcess=Process.GetCurrentProcess();var session=currentProcess.SessionId;
        GetWindowThreadProcessId(GetForegroundWindow(),out var foreground);
        foreach(var process in Process.GetProcesses())
        {
            using(process)
                try
                {
                    if(process.SessionId!=session)continue;
                    var game=catalog.FirstOrDefault(g=>g.Processes.Contains(process.ProcessName,StringComparer.OrdinalIgnoreCase));
                    if(game is null&&libraries.Count==0)continue;
                    var executable=Executable(process.Id);
                    if(executable is not null)
                    {
                        var installed=libraries.FirstOrDefault(item=>SteamManifest.ContainsExecutable(item.Directory,executable));
                        if(installed.Game is not null)game=installed.Game;
                    }
                    if(game is null)continue;
                    if(game.Executable is not null&&!string.Equals(game.Executable,executable,StringComparison.OrdinalIgnoreCase))continue;
                    matches.Add((new(game,executable,process.Id),process.Id==foreground));
                }
                catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){}
        }
        return matches.OrderByDescending(x=>x.Foreground).ThenByDescending(x=>x.Match.Game.Id==selected).Select(x=>x.Match).FirstOrDefault();
    }
    private static string? Executable(int processId)
    {
        using var handle=OpenProcess(0x1000,false,processId);
        if(handle.IsInvalid)return null;
        var path=new StringBuilder(32768);var length=path.Capacity;
        return QueryFullProcessImageName(handle,0,path,ref length)?path.ToString():null;
    }
    public async Task<string> ApplyAsync(DetectedGame detected)
    {
        await gate.WaitAsync();
        try
        {
            if(ActiveGame==detected.Game.Id&&preferences.GameSession?.ProcessId==detected.ProcessId)return status;
            await RestoreCoreAsync();
            using var process=Process.GetProcessById(detected.ProcessId);
            var plans=await WindowsSystemService.GetPowerPlansAsync();
            var profile=preferences.GameProfiles.GetValueOrDefault(detected.Game.Id)??GameProfileSettings.Default;
            var current=plans.FirstOrDefault(p=>p.Active);var performance=plans.FirstOrDefault(p=>p.Id==profile.PowerPlan);
            long started=0;ProcessPriorityClass? original=null;
            try{started=process.StartTime.ToUniversalTime().Ticks;original=process.PriorityClass;}
            catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException){}
            var requested=profile.Priority switch{"Normal"=>ProcessPriorityClass.Normal,"AboveNormal"=>ProcessPriorityClass.AboveNormal,_=>(ProcessPriorityClass?)null};
            var priority=started!=0&&original is not null&&original is not (ProcessPriorityClass.High or ProcessPriorityClass.RealTime)&&requested!=original?requested:null;
            preferences.GameSession=new(detected.Game.Id,current?.Id,current is not null?performance?.Id:null,detected.ProcessId,started,original?.ToString(),priority?.ToString());
            store.Save(preferences);
            try
            {
                if(current is not null&&performance is not null)await WindowsSystemService.SetPowerPlanAsync(performance.Id);
                var priorityApplied=false;
                if(priority is not null)
                {
                    try{process.PriorityClass=priority.Value;priorityApplied=true;}
                    catch(System.ComponentModel.Win32Exception){preferences.GameSession=preferences.GameSession with{AppliedPriority=null};store.Save(preferences);}
                }
                status=priorityApplied?"Windows profile active":performance is not null&&current is not null?"Power active · priority unchanged or restricted":"Detected · Windows exposes no profile changes";
                return status;
            }
            catch(InvalidOperationException)when(process.HasExited){await RestoreCoreAsync();return "Game exited";}
            catch{await RestoreCoreAsync();throw;}
        }
        catch(ArgumentException){await RestoreCoreAsync();return "Game exited";}
        finally{gate.Release();}
    }
    public async Task RestoreAsync()
    {
        await gate.WaitAsync();try{await RestoreCoreAsync();}finally{gate.Release();}
    }
    private async Task RestoreCoreAsync()
    {
        var journal=preferences.GameSession;if(journal is null)return;
        if(journal.PreviousPower is Guid previous&&journal.AppliedPower is Guid applied)
        {
            var plans=await WindowsSystemService.GetPowerPlansAsync();
            if(plans.Any(p=>p.Id==applied&&p.Active))await WindowsSystemService.SetPowerPlanAsync(previous);
        }
        if(journal.AppliedPriority is not null&&journal.PreviousPriority is not null)
        {
            try
            {
                using var process=Process.GetProcessById(journal.ProcessId);
                if(process.StartTime.ToUniversalTime().Ticks==journal.ProcessStartTicks&&process.PriorityClass.ToString()==journal.AppliedPriority)
                    process.PriorityClass=Enum.Parse<ProcessPriorityClass>(journal.PreviousPriority);
            }
            catch(ArgumentException){}catch(InvalidOperationException){}
        }
        preferences.GameSession=null;store.Save(preferences);
    }
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out int processId);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,[MarshalAs(UnmanagedType.Bool)]bool inherit,int processId);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder name,ref int length);
}
