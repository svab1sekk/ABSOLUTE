using System.Windows;
using System.Windows.Controls;
using Absolute.App.Controls;
using Absolute.App.Services;
using Absolute.Core;
using static Absolute.App.Controls.Ui;

namespace Absolute.App;

public partial class MainWindow
{
    private void BuildBenchmark()
    {
        var cpu=new CheckBox{Content="CPU · SHA-256",IsChecked=true,Margin=new Thickness(0,0,24,0)};
        var gpu=new CheckBox{Content="GPU · Direct2D",IsChecked=true,Margin=new Thickness(0,0,24,0)};
        var ram=new CheckBox{Content="RAM · copy",IsChecked=true};
        var choices=new StackPanel{Orientation=Orientation.Horizontal};choices.Children.Add(cpu);choices.Children.Add(gpu);choices.Children.Add(ram);
        var progress=new ProgressBar{Maximum=1,Margin=new Thickness(0,20,0,16)};
        var state=Text(benchmarkCancellation is null?"Choose components to test":"Benchmark running…",13,"#B3A0C6");
        var cancel=B("Cancel",()=>benchmarkCancellation?.Cancel());cancel.Margin=new Thickness(12,0,0,0);cancel.IsEnabled=benchmarkCancellation is not null;
        var start=B("Run benchmark",async()=>
        {
            if(benchmarkCancellation is not null)return;
            var tests=new List<string>();if(cpu.IsChecked==true)tests.Add("CPU");if(gpu.IsChecked==true)tests.Add("GPU");if(ram.IsChecked==true)tests.Add("RAM");
            if(tests.Count==0){Notify("Select at least one component.");return;}
            using var cancellation=new CancellationTokenSource();benchmarkCancellation=cancellation;
            benchmarkNotices.Clear();var completed=0;
            try
            {
                cpu.IsEnabled=gpu.IsEnabled=ram.IsEnabled=false;cancel.IsEnabled=true;
                for(var i=0;i<tests.Count;i++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();var index=i;
                    var report=new Progress<double>(p=>progress.Value=(index+p)/tests.Count);
                    state.Text=tests[i]+" benchmark running…";
                    ComponentBenchmarkResult result;
                    try
                    {
                        if(tests[i]=="CPU")result=await CpuBenchmark.RunAsync(report,cancellation.Token);
                        else if(tests[i]=="GPU")result=await GpuBenchmarkWindow.RunAsync(this,report,cancellation.Token);
                        else{var memory=await MemoryBenchmark.RunAsync(report,cancellation.Token);result=new("RAM",memory.MegabytesPerSecond,"MB/s",memory.DurationSeconds,memory.RecordedAt,"32 MB managed memory copy · single thread");}
                    }
                    catch(NotSupportedException error){benchmarkNotices.Add(error.Message);continue;}
                    preferences.ComponentBenchmarks.Insert(0,result);preferences.ComponentBenchmarks=preferences.ComponentBenchmarks.Take(60).ToList();store.Save(preferences);
                    completed++;
                }
                progress.Value=1;Notify($"{completed} benchmark{(completed==1?"":"s")} completed"+(benchmarkNotices.Count>0?$" · {benchmarkNotices.Count} unavailable":""));
            }
            catch(OperationCanceledException){Notify("Benchmark cancelled.");}
            finally{benchmarkCancellation=null;RefreshPage("Benchmark");}
        },true);
        start.IsEnabled=benchmarkCancellation is null;
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(start);buttons.Children.Add(cancel);
        Page.Children.Add(Card(Stack(Text("Component tests",22,weight:FontWeights.SemiBold),Space(20),choices,progress,state,Space(18),buttons)));
        foreach(var notice in benchmarkNotices)Page.Children.Add(Card(Text(notice,12,"#C99AB3")));
        Page.Children.Add(Text("CPU: hashing throughput. GPU: hardware 2D rendering FPS. RAM: memory-copy throughput.",12,"#A396AF"));
        Section("Results");
        var results=preferences.ComponentBenchmarks.Count>0?preferences.ComponentBenchmarks:preferences.Benchmarks.Select(b=>new ComponentBenchmarkResult("RAM",b.MegabytesPerSecond,"MB/s",b.DurationSeconds,b.RecordedAt,"Legacy memory-copy run")).ToList();
        if(results.Count==0){Page.Children.Add(Card(Text("No benchmark results yet",13,"#A396AF")));return;}
        foreach(var result in results.Take(12))
        {
            var value=Text($"{result.Score:N0} {result.Unit}",27,"#D3B4F0",FontWeights.SemiBold);
            var info=Stack(Text(result.Component,17,weight:FontWeights.SemiBold),Text(result.Detail,12,"#A396AF"),Text($"{result.RecordedAt.LocalDateTime:g} · {result.Seconds:F1}s",11,"#8D7A9D"));
            Page.Children.Add(Card(Columns((info,1),(value,-1))));
        }
    }

    private void BuildSettings()
    {
        CheckBox Toggle(bool value,Func<bool,Task> action)
        {
            var toggle=new CheckBox{IsChecked=value,Content=value?"Enabled":"Off"};toggle.Click+=async(_,_)=>await Safe(async()=>{await action(toggle.IsChecked==true);toggle.Content=toggle.IsChecked==true?"Enabled":"Off";});return toggle;
        }
        var monitoring=Toggle(preferences.LiveMonitoring,async value=>{preferences.LiveMonitoring=value;store.Save(preferences);if(value){await SampleAsync();monitor.Start();}else monitor.Stop();});
        var animation=Toggle(preferences.AnimateLogo,value=>{preferences.AnimateLogo=value;store.Save(preferences);return Task.CompletedTask;});
        var automatic=Toggle(preferences.AutoGamePresets,async value=>{preferences.AutoGamePresets=value;store.Save(preferences);if(!value){gameMonitor.Stop();await gamePresets.RestoreAsync();}else{gameMonitor.Start();await DetectGameAsync();}});
        Page.Children.Add(Card(Stack(Row("Home","Hardware refresh","Refresh system readings every 2 seconds",monitoring),Row("Presets","Automatic game profiles","Detect supported games while ABSOLUTE is running",automatic),Row("PC","Startup animation","Short rotation and movement of the logo",animation),Row("Home","Temperature sensors","Reconnect supported hardware-monitor providers",B("Reconnect",async()=>{await SampleAsync(true);Notify("Sensor providers refreshed.");})),Row("Presets","Restore game profile","Restore settings owned by the current automatic game profile",B("Restore",async()=>{preferences.AutoGamePresets=false;store.Save(preferences);await gamePresets.RestoreAsync();Navigate("Settings");})),Row("Benchmark","Benchmark history","Remove saved benchmark results",B("Clear",()=>{if(Confirm("Clear benchmark history?","Remove saved CPU, GPU, and RAM results?")){preferences.ComponentBenchmarks.Clear();preferences.Benchmarks.Clear();store.Save(preferences);Notify("Benchmark history cleared.");}}),true)),new Thickness(23,2,23,2)));
        if(preferences.PreviousPowerPlan is Guid previous)Page.Children.Add(Card(Row("Presets","Previous power plan","Restore the plan saved by an earlier ABSOLUTE version",B("Restore",async()=>{await WindowsSystemService.SetPowerPlanAsync(previous);preferences.PreviousPowerPlan=null;store.Save(preferences);Navigate("Settings");}),true)));
        Page.Children.Add(Card(Stack(Text("ABSOLUTE",22,weight:FontWeights.SemiBold),Text($"Version {typeof(App).Assembly.GetName().Version?.ToString(3)}",12,"#A396AF"),Space(12),Text("Windows 10 / Windows 11",13,"#BCAFCA"),Text("Settings and benchmark results stay on this PC.",12,"#A396AF"))));
    }
}
