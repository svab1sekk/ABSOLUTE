using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Absolute.App.Controls;
using Absolute.App.Services;
using Absolute.Core;
using static Absolute.App.Controls.Ui;

namespace Absolute.App;

public partial class MainWindow : Window
{
    private readonly WindowsSystemService system=new();
    private readonly SensorService sensors=new();
    private readonly TemperatureMonitor temperatures=new();
    private readonly PreferenceStore store=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Absolute","preferences.json"));
    private readonly AppPreferences preferences;
    private readonly GamePresetService gamePresets;
    private readonly DispatcherTimer monitor=new(){Interval=TimeSpan.FromSeconds(2)};
    private readonly DispatcherTimer gameMonitor=new(){Interval=TimeSpan.FromSeconds(3)};
    private readonly DispatcherTimer toastTimer=new(){Interval=TimeSpan.FromSeconds(7)};
    private readonly bool smoke=Environment.GetCommandLineArgs().Contains("--smoke-test");
    private IReadOnlyList<PowerPlan> powerPlans=[];
    private HardwareInfo? hardware;
    private HardwareDiscoveryResult? inventory;
    private IReadOnlyList<DriverDevice> devices=[];
    private SensorSnapshot sensorSnapshot=new(null,null,null,[]);
    private SystemSnapshot? snapshot;
    private Task inventoryTask=Task.CompletedTask,devicesTask=Task.CompletedTask;
    private CancellationTokenSource? driverCancellation,benchmarkCancellation;
    private bool sampling,detecting,closed,closingAllowed,closingInProgress,driverChecking,cpuHasInterval;
    private DateTimeOffset nextGameAttempt;
    private string? editingGame;
    private string selectedPage="Home",gameQuery="",gameState="Waiting for a game",inventoryError="",devicesError="";
    private TextBlock? cpuValue,memoryValue,memoryDetail,gpuValue,cpuTemperature,gpuTemperature;
    private TextBlock? driverHomeStatus;
    private Controls.Sparkline? cpuChart,gpuChart,memoryChart;
    private readonly Queue<double> cpuHistory=new(),gpuHistory=new(),memoryHistory=new();
    private IEnumerable<GameDefinition> Games=>Catalogs.Games.Concat(preferences.CustomGames);

    public MainWindow()
    {
        InitializeComponent();preferences=store.Load();gamePresets=new(preferences,store);
        foreach(var name in new[]{"Home","System","Network","Presets","Playbooks","Benchmark","PC","Drivers"})
        {
            var content=new StackPanel{Orientation=Orientation.Horizontal};var glyph=Icon(name,18);glyph.Margin=new Thickness(0,0,14,0);content.Children.Add(glyph);
            var label=new TextBlock{Text=name,VerticalAlignment=VerticalAlignment.Center};
            label.SetBinding(TextBlock.ForegroundProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(RadioButton),1)});content.Children.Add(label);
            var item=new RadioButton{Content=content,GroupName="Pages",Style=(Style)FindResource("Nav"),Tag=name};
            System.Windows.Automation.AutomationProperties.SetName(item,name);item.Checked+=(_,_)=>Navigate(name);Navigation.Children.Add(item);
        }
        SettingsNav.Content=Icon("Settings",20);SettingsNav.Checked+=(_,_)=>Navigate("Settings");((RadioButton)Navigation.Children[0]).IsChecked=true;
        driverMonitor.Tick+=async(_,_)=>await Safe(RunDriverCheckAsync);
        toastTimer.Tick+=(_,_)=>{Toast.Visibility=Visibility.Collapsed;toastTimer.Stop();};
        monitor.Tick+=async(_,_)=>await SampleAsync();gameMonitor.Tick+=async(_,_)=>await DetectGameAsync();
        ContentRendered+=async(_,_)=>
        {
            var firstFrame=App.StartupClock.Elapsed.TotalMilliseconds;
            var animation=AnimateIntroAsync();
            await InitializeAsync();await animation;
            if(smoke)
            {
                var args=Environment.GetCommandLineArgs();var index=Array.IndexOf(args,"--smoke-test");
                await RunSmokeCheckAsync(index+1<args.Length?args[index+1]:"smoke-results",firstFrame);
            }
            else{driverMonitor.Start();_=StartDriverCheckAsync();if(preferences.TemperatureMonitoring)_=StartTemperatureMonitorAsync();}
        };
        Closing+=OnClosing;Closed+=(_,_)=>{closed=true;driverMonitor.Stop();monitor.Stop();gameMonitor.Stop();toastTimer.Stop();driverCancellation?.Cancel();benchmarkCancellation?.Cancel();temperatures.Dispose();};
    }

    private async Task InitializeAsync()
    {
        inventoryTask=LoadInventoryAsync();devicesTask=LoadDevicesAsync();
        await Safe(async()=>
        {
            var summary=Task.Run(system.Hardware);var plans=WindowsSystemService.GetPowerPlansAsync();
            hardware=await summary;powerPlans=await plans;await SampleAsync();RefreshPage("Home","PC");
        });
        if(!smoke)
        {
            try{await gamePresets.RestoreAsync();gameMonitor.Start();}
            catch(Exception error){gameState="Profile recovery required";Notify(error.Message);nextGameAttempt=DateTimeOffset.UtcNow.AddSeconds(30);gameMonitor.Start();}
        }
        if(!closed&&preferences.LiveMonitoring)monitor.Start();
    }
    private async Task LoadInventoryAsync()
    {
        try
        {
            var result=await Task.Run(HardwareDiscovery.ReadParts);
            IReadOnlyList<HardwarePart> graphics=[];
            try{graphics=await Task.Run(GraphicsDiscovery.Read);}catch(Exception){/* Basic WMI graphics inventory remains available. */}
            var parts=result.Parts.ToList();
            foreach(var gpu in graphics)
            {
                var existing=parts.FindIndex(p=>p.Category=="Graphics"&&p.Name.Equals(gpu.Name,StringComparison.OrdinalIgnoreCase)&&!p.Details.Any(d=>d.Key is "VRAM" or "Memory"));
                if(existing>=0)parts[existing]=parts[existing] with{Details=parts[existing].Details.Concat(gpu.Details).ToArray()};else parts.Add(gpu);
            }
            inventory=result with{Parts=parts};
            if(hardware is not null&&parts.FirstOrDefault(p=>p.Category=="Graphics") is {} primary)hardware=hardware with{Graphics=primary.Name};
        }
        catch(Exception error){inventoryError=error.Message;}
        if(!closed)RefreshPage("Home","PC","Drivers");
    }
    private async Task LoadDevicesAsync()
    {
        try{devices=await Task.Run(HardwareDiscovery.ReadDrivers);devicesError="";}catch(Exception error){devices=[];devicesError=error.Message;}
        if(!closed){UpdateHomeDriverStatus();RefreshPage("Drivers");}
    }
    private async Task StartDriverCheckAsync(){await Task.Delay(300);if(!closed)await RunDriverCheckAsync();}
    private async Task StartTemperatureMonitorAsync()
    {
        await temperatures.StartAsync();
        try{await temperatures.FirstReading.WaitAsync(TimeSpan.FromMinutes(2));}catch(TimeoutException){}
        if(!closed)await SampleAsync();
    }
    private async Task SampleAsync(bool reprobe=false)
    {
        if(sampling||closed)return;sampling=true;
        try
        {
            var next=await Task.Run(system.Sample);
            var nextSensors=await Task.Run(()=>sensors.Read(reprobe,preferences.TemperatureMonitoring?temperatures.Snapshot:null));if(closed)return;
            var hadSample=snapshot is not null;cpuHasInterval=hadSample;snapshot=next;sensorSnapshot=nextSensors;
            if(cpuValue is not null)cpuValue.Text=hadSample?$"{next.CpuPercent:F0}%":"—";
            if(hadSample){Remember(cpuHistory,next.CpuPercent);cpuChart?.Add(next.CpuPercent);}
            if(memoryValue is not null)memoryValue.Text=$"{GiB(next.MemoryUsed):F1} GB";
            if(memoryDetail is not null)memoryDetail.Text=$"of {GiB(next.MemoryTotal):F1} GB usable";
            var memoryPercent=next.MemoryTotal>0?next.MemoryUsed*100d/next.MemoryTotal:0;
            Remember(memoryHistory,memoryPercent);memoryChart?.Add(memoryPercent);
            if(nextSensors.GpuLoad is double usage){Remember(gpuHistory,usage);gpuChart?.Add(usage);}
            if(gpuValue is not null)gpuValue.Text=nextSensors.GpuLoad is double gpu?$"{gpu:F0}%":"—";
            if(cpuTemperature is not null)cpuTemperature.Text=Temperature(nextSensors.CpuTemperature);
            if(gpuTemperature is not null)gpuTemperature.Text=Temperature(nextSensors.GpuTemperature);
            if(cpuTemperature is not null)cpuTemperature.ToolTip=temperatures.Status;

        }
        catch(Exception error){if(!closed)Notify(error.Message);}
        finally{sampling=false;}
    }
    private async Task DetectGameAsync()
    {
        if(detecting||closed||closingInProgress||DateTimeOffset.UtcNow<nextGameAttempt)return;detecting=true;
        try
        {
            var candidates=Games.ToArray();var selected=preferences.SelectedGame;
            var found=await Task.Run(()=>GamePresetService.Detect(candidates,selected));
            if(closed||closingInProgress)return;
            var old=gamePresets.ActiveGame;
            if(found is null){await gamePresets.RestoreAsync();gameState="Waiting for a game";}
            else gameState=await gamePresets.ApplyAsync(found);
            if(old!=gamePresets.ActiveGame){if(editingGame is null)RefreshPage("Presets");RefreshPage("Home");}
        }
        catch(Exception error){gameState=error.Message;nextGameAttempt=DateTimeOffset.UtcNow.AddSeconds(30);if(!closed){Notify(error.Message);RefreshPage("Presets");}}
        finally{detecting=false;}
    }
    private async void OnClosing(object? sender,CancelEventArgs args)
    {
        if(closingAllowed)return;args.Cancel=true;if(closingInProgress)return;closingInProgress=true;
        monitor.Stop();gameMonitor.Stop();driverCancellation?.Cancel();benchmarkCancellation?.Cancel();
        await Safe(gamePresets.RestoreAsync);closingAllowed=true;Close();
    }
    private bool introPlayed;
    private int introRenderedFrames;
    private async Task AnimateIntroAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Render);
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target=SidebarLogo.TranslatePoint(new Point(SidebarLogo.ActualWidth/2,SidebarLogo.ActualHeight/2),this);
        var from=IntroLogo.TranslatePoint(new Point(48,48),this);
        var duration=TimeSpan.FromMilliseconds(420);var ease=new CubicEase{EasingMode=EasingMode.EaseInOut};
        DoubleAnimation Move(double end)=>new(0,end,duration){BeginTime=TimeSpan.FromMilliseconds(480),EasingFunction=ease};
        IntroTranslation.BeginAnimation(TranslateTransform.XProperty,Move(target.X-from.X));IntroTranslation.BeginAnimation(TranslateTransform.YProperty,Move(target.Y-from.Y));
        var scale=new DoubleAnimation(1,78d/96,duration){BeginTime=TimeSpan.FromMilliseconds(480),EasingFunction=ease};
        IntroScale.BeginAnimation(ScaleTransform.ScaleXProperty,scale);IntroScale.BeginAnimation(ScaleTransform.ScaleYProperty,scale);
        IntroBackdrop.BeginAnimation(OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(360)){BeginTime=TimeSpan.FromMilliseconds(480)});
        var rotate=new DoubleAnimation(0,360,TimeSpan.FromMilliseconds(480)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut}};
        IntroRotation.BeginAnimation(RotateTransform.AngleProperty,rotate);
        void Rendered(object? sender,EventArgs args)=>introRenderedFrames++;
        CompositionTarget.Rendering+=Rendered;
        var finish=new DoubleAnimation(1,1,TimeSpan.FromMilliseconds(900));
        finish.Completed+=(_,_)=>{SidebarLogo.Opacity=1;IntroLayer.Visibility=Visibility.Collapsed;introPlayed=true;completion.TrySetResult();};
        IntroLogo.BeginAnimation(OpacityProperty,finish);
        try{await completion.Task;}finally{CompositionTarget.Rendering-=Rendered;}
    }

    private void Navigate(string name)
    {
        selectedPage=name;cpuValue=memoryValue=memoryDetail=gpuValue=cpuTemperature=gpuTemperature=driverHomeStatus=null;cpuChart=gpuChart=memoryChart=null;
        Page.Children.Clear();PageScroll.ScrollToTop();
        var title=Text(name,34,weight:FontWeights.SemiBold);title.Margin=new Thickness(0,0,0,26);Page.Children.Add(title);
        switch(name)
        {
            case "Home":BuildHome();break;
            case "System":case "Network":break;
            case "Presets":BuildPresets();break;
            case "Playbooks":BuildPlaybooks();break;
            case "Benchmark":BuildBenchmark();break;
            case "PC":BuildPc();break;
            case "Drivers":BuildDrivers();break;
            case "Settings":BuildSettings();break;
        }
    }
    private void RefreshPage(params string[] pages){if(!closed&&pages.Contains(selectedPage))Navigate(selectedPage);}
    private void Select(string name){foreach(RadioButton item in Navigation.Children)if((string)item.Tag==name)item.IsChecked=true;}
    private Button B(string label,Action action,bool primary=false)=>B(label,()=>{action();return Task.CompletedTask;},primary);
    private Button B(string label,Func<Task> action,bool primary=false)=>Ui.Button(label,()=>Safe(action),primary);
    private async Task Safe(Func<Task> action){try{await action();}catch(OperationCanceledException){}catch(Exception error){if(!closed)Notify(error.Message);}}
    private void Notify(string message){ToastText.Text=message;Toast.Visibility=Visibility.Visible;toastTimer.Stop();toastTimer.Start();}
    private bool Confirm(string title,string message)=>ConfirmDialog.Ask(this,title,message);
    private static double GiB(ulong bytes)=>bytes/1073741824d;
    private static string Temperature(double? value)=>value is double temperature?$"{temperature:F0} °C":"Temp unavailable";
    private static void Remember(Queue<double> history,double value){history.Enqueue(value);while(history.Count>50)history.Dequeue();}
    private static FrameworkElement Space(double height)=>new Border{Height=height};
    private void Section(string title){var text=Text(title,17,weight:FontWeights.SemiBold);text.Margin=new Thickness(0,10,0,14);Page.Children.Add(text);}
    private static Grid Info(string label,string value){var result=Columns((Text(label,12,"#A396AF"),.75),(Text(value,13),1));result.Margin=new Thickness(0,7,0,7);return result;}
    private static void OpenUrl(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    private void MinimizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void MaximizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void CloseClick(object sender,RoutedEventArgs e)=>Close();
}
