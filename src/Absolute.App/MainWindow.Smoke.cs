using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Absolute.App.Controls;
using Absolute.Core;

namespace Absolute.App;

public partial class MainWindow
{
    private async Task RunSmokeCheckAsync(string output,double firstFrameMilliseconds)
    {
        var success=false;var errors=new List<string>();var rendered=new List<string>();
        string gpuCheck="Not run";ComponentBenchmarkResult? gpuResult=null;
        var gameProfileCheck="Not run";var gpu3DCheck="Not run";ComponentBenchmarkResult? gpu3DResult=null;var shaderCheck="Not run";
        try
        {
            Directory.CreateDirectory(output);
            await Task.WhenAll(inventoryTask,devicesTask);
            if(!Services.TemperatureHost.BundledDriverIsValid())errors.Add("The bundled temperature driver is missing or failed verification.");
            await temperatures.StartAsync(false);
            try{await temperatures.FirstReading.WaitAsync(TimeSpan.FromSeconds(110));}catch(TimeoutException){errors.Add("Built-in sensor helper did not respond.");}
            if(temperatures.Snapshot?.Ready!=true)errors.Add("Built-in sensor initialization failed: "+temperatures.Status);
            try{await Task.WhenAll(Artwork.WarmAsync(Catalogs.Games),Artwork.WarmProjectsAsync()).WaitAsync(TimeSpan.FromSeconds(65));}catch(TimeoutException){/* Publisher results are reported separately; offline access must not prevent rendering. */}
            if(hardware is null||hardware.Memory==0)errors.Add("No physical memory reported.");
            if(snapshot is null||snapshot.MemoryTotal==0)errors.Add("Live memory readings unavailable.");
            if(!powerPlans.Any(p=>p.Active))errors.Add("No active Windows power plan discovered.");
            if(inventory?.Parts.Count is not >0)errors.Add("Hardware inventory is empty: "+inventoryError);
            if(devices.Count==0)errors.Add("Installed driver inventory is empty: "+devicesError);
            if(!introPlayed||introRenderedFrames<2)errors.Add("Startup animation did not render.");
            await SampleAsync();
            if(snapshot is null||!double.IsFinite(snapshot.CpuPercent)||snapshot.CpuPercent is <0 or >100)errors.Add("CPU utilization is invalid.");
            monitor.Stop();gameMonitor.Stop();Toast.Visibility=Visibility.Collapsed;
            await CheckGameProfileAsync();gameProfileCheck="Passed · power and process priority restored";
            try
            {
                using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(20));
                gpuResult=await GpuBenchmarkWindow.RunAsync(this,null,limit.Token);gpuCheck="Passed";
                if(gpuResult.Score<=0)errors.Add("Hardware Direct2D benchmark produced no frames.");
            }
            catch(NotSupportedException error){gpuCheck="Unavailable on runner: "+error.Message;}
            Gpu3DWorkload.ValidateShaders();shaderCheck="Passed · vertex and pixel shaders compiled";
            try
            {
                using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(35));
                gpu3DResult=await GpuBenchmarkWindow.RunAsync(this,null,limit.Token,true);gpu3DCheck="Passed";
                if(gpu3DResult.Score<=0)errors.Add("Hardware Direct3D benchmark produced no frames.");
            }
            catch(NotSupportedException error){gpu3DCheck="Unavailable on runner: "+error.Message;}
            var actualResults=new List<ComponentBenchmarkResult>{await CpuBenchmark.RunAsync(null,CancellationToken.None)};
            var memoryResult=await MemoryBenchmark.RunAsync(null,CancellationToken.None);
            actualResults.Add(new("RAM",memoryResult.MegabytesPerSecond,"MB/s",memoryResult.DurationSeconds,memoryResult.RecordedAt,"256 MB memory copy · 512 MB working set · single thread"));
            if(gpuResult is not null)actualResults.Add(gpuResult);
            if(gpu3DResult is not null)actualResults.Add(gpu3DResult);
            preferences.BenchmarkRuns.Insert(0,new("smoke",DateTimeOffset.UtcNow,Environment.MachineName,BenchmarkScoring.SuiteVersion,actualResults,gpu3DResult is null?[gpu3DCheck]:[]));
            foreach(var size in new[]{(Width:1240d,Height:820d,Name:"desktop"),(Width:1020d,Height:700d,Name:"compact")})
            {
                Width=size.Width;Height=size.Height;
                foreach(var page in new[]{"Home","System","Network","Presets","Playbooks","Benchmark","PC","Drivers","Settings"})
                {
                    if(page=="Settings")SettingsNav.IsChecked=true;else Select(page);
                    await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
                    if(page is "System" or "Network")
                    {if(Page.Children.Count!=1)errors.Add(page+" should contain only its title.");}
                    else if(Page.Children.Count<(page=="Playbooks"?2:3))errors.Add(page+" is missing content.");
                    if(page=="PC"&&inventory?.Parts.Count(p=>p.Category=="Memory")!=1)errors.Add("PC memory is not combined into one card.");
                    if(page=="PC"&&inventory?.Parts.Any(p=>p.Category is "Volume" or "Firmware" or "Network" or "Cooling" or "Audio" or "Mouse" or "Keyboard")==true)errors.Add("PC contains an excluded hardware category.");
                    Capture($"{size.Name}-{page.ToLowerInvariant()}.png");
                    if(page=="Presets")
                    {
                        editingGame=Catalogs.Games[0].Id;Navigate("Presets");await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
                        Capture($"{size.Name}-game-profile.png");editingGame=null;Navigate("Presets");
                    }
                    if(page=="Benchmark")
                    {
                        foreach(var card in Page.Children.OfType<Border>())if(card.Child is Expander expander)expander.IsExpanded=true;
                        await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);Capture($"{size.Name}-benchmark-details.png");
                    }
                }
            }
            void Capture(string filename)
            {
                var dpi=VisualTreeHelper.GetDpi(this);
                var bitmap=new RenderTargetBitmap((int)(ActualWidth*dpi.DpiScaleX),(int)(ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
                bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var image=File.Create(Path.Combine(output,filename));encoder.Save(image);rendered.Add(filename);
            }
            success=errors.Count==0;
        }
        catch(Exception error){errors.Add(error.ToString());}
        finally
        {
            try
            {
                Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new
                {
                    Success=success,ManagedFirstFrameMilliseconds=firstFrameMilliseconds,
                    StartupAnimationPlayed=introPlayed,StartupAnimationRenderedFrames=introRenderedFrames,
                    OperatingSystem=hardware?.OperatingSystem,MemoryBytes=snapshot?.MemoryTotal,CpuPercent=snapshot?.CpuPercent,
                    HardwareParts=inventory?.Parts.Count,MainDriverComponents=MainDriverComponents().Select(c=>new{c.Part.Category,c.Part.Name}),PowerPlans=powerPlans.Count,
                    Gpu2DCheck=gpuCheck,Gpu2DResult=gpuResult,Gpu3DCheck=gpu3DCheck,Gpu3DResult=gpu3DResult,ShaderValidation=shaderCheck,
                    GameProfileCheck=gameProfileCheck,
                    OnlineDriverCheck="Manufacturer catalogs; comparison requires matched physical hardware. No Windows Update driver API is used.",
                    TemperatureMonitor=temperatures.Status,BundledSensorDriverVerified=Services.TemperatureHost.BundledDriverIsValid(),
                    ArtworkSummary=new
                    {
                        Loaded=Artwork.Status.Count(p=>p.Value.StartsWith("Publisher artwork",StringComparison.Ordinal)||p.Value.StartsWith("Cached",StringComparison.Ordinal)),
                        Expected=Catalogs.Games.Count+Catalogs.Playbooks.Count,
                        Failures=Artwork.Status.Where(p=>!p.Value.StartsWith("Publisher artwork",StringComparison.Ordinal)&&!p.Value.StartsWith("Cached",StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value),
                        CurrentGameArt=new[]{"fortnite","valorant","lol"}.ToDictionary(id=>id,id=>Artwork.Status.GetValueOrDefault(id,"Not loaded"))
                    },
                    PublisherArtwork=Artwork.Status,
                    Playbooks="Official OS projects; no ABSOLUTE settings profiles are shown.",
                    Screenshots=rendered,Errors=errors
                },new JsonSerializerOptions{WriteIndented=true}));
            }
            finally{closingAllowed=true;Application.Current.Shutdown(success?0:1);}
        }
    }

    private static async Task CheckGameProfileAsync()
    {
        var path=Path.Combine(Path.GetTempPath(),"absolute-game-profile-"+Guid.NewGuid().ToString("N")+".json");
        var settings=new AppPreferences();var controller=new Services.GamePresetService(settings,new PreferenceStore(path));
        var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-Command","Start-Sleep -Seconds 30"})start.ArgumentList.Add(arg);
        using var process=System.Diagnostics.Process.Start(start)??throw new InvalidOperationException("Could not start the profile smoke-test process.");
        try
        {
            var priority=process.PriorityClass;
            var original=(await Services.WindowsSystemService.GetPowerPlansAsync()).First(p=>p.Active).Id;
            var game=new GameDefinition("smoke","Smoke",[process.ProcessName],"#AA83E0",0);
            await controller.ApplyAsync(new(game,null,process.Id));
            if(settings.GameSession is null)throw new InvalidOperationException("Game profile did not retain a rollback journal.");
            process.Refresh();
            if(priority is System.Diagnostics.ProcessPriorityClass.Normal or System.Diagnostics.ProcessPriorityClass.BelowNormal or System.Diagnostics.ProcessPriorityClass.Idle)
                if(process.PriorityClass!=System.Diagnostics.ProcessPriorityClass.AboveNormal)throw new InvalidOperationException("Game process priority was not applied.");
            await controller.RestoreAsync();
            process.Refresh();
            var restored=(await Services.WindowsSystemService.GetPowerPlansAsync()).First(p=>p.Active).Id;
            if(original!=restored||process.PriorityClass!=priority||settings.GameSession is not null)throw new InvalidOperationException("Game profile did not restore the original Windows state.");
            settings.GameProfiles[game.Id]=new(null,"Keep");
            await controller.ApplyAsync(new(game,null,process.Id));process.Refresh();
            if(process.PriorityClass!=priority||(await Services.WindowsSystemService.GetPowerPlansAsync()).First(p=>p.Active).Id!=original)
                throw new InvalidOperationException("The Keep current profile changed Windows settings.");
            await controller.RestoreAsync();
        }
        finally
        {
            await controller.RestoreAsync();
            if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}
            if(File.Exists(path))File.Delete(path);
        }
    }
}
