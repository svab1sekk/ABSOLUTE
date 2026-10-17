using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Absolute.App.Services;

namespace Absolute.App;

public partial class App : Application
{
    internal static readonly Stopwatch StartupClock = new();
    public App() => StartupClock.Start();
    private Mutex? instance;
    private bool ownsInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        if(e.Args.FirstOrDefault()=="--sensor-host")
        {
            var result=2;
            try{if(e.Args.Length==4&&int.TryParse(e.Args[1],out var parent)&&long.TryParse(e.Args[2],out var started))result=Task.Run(()=>TemperatureHost.RunAsync(parent,started,e.Args[3])).GetAwaiter().GetResult();}
            catch(Exception){result=1;}
            Shutdown(result);return;
        }
        if(e.Args.FirstOrDefault()=="--playbook")
        {
            var result=2;
            try{if(e.Args.Length==4&&e.Args[2]=="--user")result=PlaybookService.ExecuteElevated(e.Args[1],e.Args[3]);}
            catch(Exception){result=1;}
            Shutdown(result);return;
        }
        using var identity=WindowsIdentity.GetCurrent();
        instance=new Mutex(false,@"Local\ABSOLUTE.Desktop."+identity.User?.Value);
        try{ownsInstance=instance.WaitOne(0);}catch(AbandonedMutexException){ownsInstance=true;}
        if(!ownsInstance)
        {
            var existing=FindWindow(null,"ABSOLUTE");if(existing!=IntPtr.Zero){if(IsIconic(existing))ShowWindow(existing,9);SetForegroundWindow(existing);}
            Shutdown();return;
        }
        base.OnStartup(e);
        MainWindow=new MainWindow();MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e){if(ownsInstance)instance?.ReleaseMutex();instance?.Dispose();base.OnExit(e);}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr FindWindow(string? className,string title);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsIconic(IntPtr window);
}
