using System.Windows;
using System.Windows.Controls;
using Absolute.App.Controls;
using Absolute.App.Services;
using Absolute.Core;
using static Absolute.App.Controls.Ui;

namespace Absolute.App;

public partial class MainWindow
{
    private ProgressBar? benchmarkBar;
    private TextBlock? benchmarkStatus;
    private double benchmarkProgress;
    private string benchmarkState="Choose components to test";

    private void BuildBenchmark()
    {
        var running=benchmarkCancellation is not null;
        var choices=new WrapPanel();
        var tests=new Dictionary<string,CheckBox>();
        foreach(var (component,label,selected) in new[]{("CPU","CPU",true),("GPU 3D","GPU 3D",true),("RAM","RAM",true),("GPU 2D","GPU 2D",false)})
        {
            var check=new CheckBox{Content=label,IsChecked=selected,IsEnabled=!running,Margin=new Thickness(0,0,24,8)};
            tests[component]=check;choices.Children.Add(check);
        }
        benchmarkBar=new ProgressBar{Maximum=1,Value=benchmarkProgress,Margin=new Thickness(0,17,0,14)};
        benchmarkStatus=Text(benchmarkState,13,"#B3A0C6");
        var cancel=B("Cancel",()=>benchmarkCancellation?.Cancel());cancel.Margin=new Thickness(12,0,0,0);cancel.IsEnabled=running;
        var start=B("Run benchmark",async()=>
        {
            var selected=tests.Where(t=>t.Value.IsChecked==true).Select(t=>t.Key).ToArray();
            if(selected.Length==0){Notify("Select at least one component.");return;}
            await RunBenchmarksAsync(selected);
        },true);start.IsEnabled=!running;
        var actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(start);actions.Children.Add(cancel);
        Page.Children.Add(Card(Stack(Text("Performance test",23,weight:FontWeights.SemiBold),Space(8),Text("CPU throughput · 3D rendering · Memory transfer",12,"#A396AF"),Space(22),choices,benchmarkBar,benchmarkStatus,Space(18),actions)));
        Section("Results");
        if(preferences.BenchmarkRuns.Count==0)Page.Children.Add(Card(Text("No complete runs yet",13,"#A396AF")));
        foreach(var run in preferences.BenchmarkRuns.Take(20))Page.Children.Add(BenchmarkCard(run));
        if(preferences.ComponentBenchmarks.Count>0||preferences.Benchmarks.Count>0)
        {
            var legacy=Stack();
            foreach(var result in preferences.ComponentBenchmarks.Take(12))legacy.Children.Add(Card(Columns((Text(result.Component+" · "+result.RecordedAt.LocalDateTime.ToString("g"),12,"#A396AF"),1),(Text($"{result.Score:N0} {result.Unit}",17),-1))));
            Page.Children.Add(new Expander{Header="Previous version's measurements",Content=legacy,Foreground=Brush("#B9A7CB"),Margin=new Thickness(0,10,0,0)});
        }
    }

    private async Task RunBenchmarksAsync(IReadOnlyList<string> tests)
    {
        if(benchmarkCancellation is not null)return;
        using var cancellation=new CancellationTokenSource();benchmarkCancellation=cancellation;
        var results=new List<ComponentBenchmarkResult>();var notices=new List<string>();var cancelled=false;
        benchmarkProgress=0;benchmarkState="Preparing benchmark…";RefreshPage("Benchmark");
        try
        {
            for(var i=0;i<tests.Count;i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();var index=i;var component=tests[i];
                var duration=component=="GPU 3D"?Controls.Gpu3DWorkload.Seconds:component=="GPU 2D"?4.5:4;
                benchmarkState=component+" · preparing…";if(benchmarkStatus is not null)benchmarkStatus.Text=benchmarkState;
                var progress=new Progress<double>(fraction=>
                {
                    if(benchmarkCancellation is null)return;
                    benchmarkProgress=(index+fraction)/tests.Count;benchmarkState=$"{component} · {fraction*duration:0.0} / {duration:0.#} s · {benchmarkProgress:P0}";
                    if(benchmarkBar is not null)benchmarkBar.Value=benchmarkProgress;
                    if(benchmarkStatus is not null)benchmarkStatus.Text=benchmarkState;
                });
                try
                {
                    ComponentBenchmarkResult result;
                    if(component=="CPU")result=await CpuBenchmark.RunAsync(progress,cancellation.Token);
                    else if(component.StartsWith("GPU",StringComparison.Ordinal))result=await GpuBenchmarkWindow.RunAsync(this,progress,cancellation.Token,component=="GPU 3D");
                    else{var memory=await MemoryBenchmark.RunAsync(progress,cancellation.Token);result=new("RAM",memory.MegabytesPerSecond,"MB/s",memory.DurationSeconds,memory.RecordedAt,"256 MB memory copy · 512 MB working set · single thread");}
                    results.Add(result);
                }
                catch(NotSupportedException error){notices.Add(component+": "+error.Message);}
            }
        }
        catch(OperationCanceledException){cancelled=true;notices.Add("Run cancelled");}
        catch(Exception error){notices.Add(error.Message);Notify(error.Message);}
        finally
        {
            var run=new BenchmarkRun(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,hardware?.Machine??Environment.MachineName,BenchmarkScoring.SuiteVersion,results,notices,cancelled);
            preferences.BenchmarkRuns.Insert(0,run);preferences.BenchmarkRuns=preferences.BenchmarkRuns.Take(40).ToList();store.Save(preferences);
            benchmarkCancellation=null;benchmarkProgress=1;benchmarkState=cancelled?"Run cancelled":run.Complete?"Benchmark complete":"Partial run saved";
            RefreshPage("Benchmark");
        }
    }

    private static Border BenchmarkCard(BenchmarkRun run)
    {
        var score=run.Score;var rating=BenchmarkScoring.Rating(score);
        var title=Stack(Text(run.Complete?"Benchmark result":run.Cancelled?"Cancelled run":"Partial run",18,weight:FontWeights.SemiBold),Space(6),Text(run.RecordedAt.LocalDateTime.ToString("g")+" · "+string.Join(" · ",run.Results.Select(r=>r.Component)),11,"#A396AF"));
        var grade=Stack(Text(rating,22,"#D0ACEA",FontWeights.SemiBold),Text(score is double value?$"{value:N0} points":"No score",12,"#B8A2C9"));grade.HorizontalAlignment=HorizontalAlignment.Right;grade.Margin=new Thickness(20,0,0,0);
        var details=new StackPanel{Margin=new Thickness(0,20,0,0)};
        foreach(var result in run.Results)
        {
            var points=BenchmarkScoring.Points(result);
            var labels=Stack(Text(result.Component,16,weight:FontWeights.SemiBold),Text(result.Detail,11,"#A396AF"));labels.Margin=new Thickness(0,0,16,0);
            var values=Stack(Text($"{result.Score:N0} {result.Unit}",18,"#D1B2EB",FontWeights.SemiBold),Text(BenchmarkScoring.Rating(points)+(points is double p?$" · {p:N0} points":""),12,"#A396AF"));values.MinWidth=140;
            details.Children.Add(Card(Columns((labels,1),(values,-1)),new Thickness(16)));
        }
        foreach(var notice in run.Notices)details.Children.Add(Text(notice,12,"#CA9BB2"));
        details.Children.Add(Text("100 points = CPU 1,024 MB/s SHA-256 · RAM 8,192 MB/s copy · GPU 60 FPS in Scene 1. Overall score is the geometric mean of CPU, GPU 3D and RAM. Ratings describe these workloads, not a percentile or predicted game FPS. Compare full runs from the same suite version.",11,"#9B8AA8"));
        var expand=new Expander{Header=Columns((title,1),(grade,-1)),Content=details,HorizontalContentAlignment=HorizontalAlignment.Stretch,Foreground=Brush("#F2EFF7"),IsExpanded=false};
        return Card(expand,new Thickness(22));
    }

    private void BuildSettings()
    {
        CheckBox Toggle(bool value,Func<bool,Task> action)
        {
            var toggle=new CheckBox{IsChecked=value,Content=value?"Enabled":"Off"};toggle.Click+=async(_,_)=>await Safe(async()=>{await action(toggle.IsChecked==true);toggle.Content=toggle.IsChecked==true?"Enabled":"Off";});return toggle;
        }
        var monitoring=Toggle(preferences.LiveMonitoring,async value=>{preferences.LiveMonitoring=value;store.Save(preferences);if(value){await SampleAsync();monitor.Start();}else monitor.Stop();});
        var temperature=Toggle(preferences.TemperatureMonitoring,async value=>{preferences.TemperatureMonitoring=value;store.Save(preferences);if(value){temperatures.Restart();await temperatures.StartAsync();}else temperatures.Stop();});
        Page.Children.Add(Card(Stack(Row("Home","Hardware refresh","Refresh readings every 2 seconds",monitoring),Row("Processor","Built-in temperatures","CPU and GPU sensor access",temperature),Row("Processor","Sensor connection",temperatures.Status,B("Reconnect",async()=>{temperatures.Restart();await temperatures.StartAsync();await SampleAsync(true);})),Row("Presets","Restore game profile","Restore the current game's Windows settings",B("Restore",async()=>{await gamePresets.RestoreAsync();nextGameAttempt=DateTimeOffset.UtcNow.AddSeconds(30);Navigate("Settings");})),Row("Benchmark","Benchmark history","Remove saved results",B("Clear",()=>{if(Confirm("Clear benchmark history?","Remove saved CPU, GPU, and RAM results?")){preferences.ComponentBenchmarks.Clear();preferences.Benchmarks.Clear();preferences.BenchmarkRuns.Clear();store.Save(preferences);Notify("Benchmark history cleared.");}}),true)),new Thickness(23,2,23,2)));
        if(PlaybookService.Active is not null)Page.Children.Add(Card(Row("Playbooks","Restore previous version's settings","Undo a settings profile applied by an earlier ABSOLUTE version",B("Restore",async()=>{await PlaybookService.ActivateAsync("restore");Navigate("Settings");}),true)));
        if(preferences.PreviousPowerPlan is Guid previous)Page.Children.Add(Card(Row("Presets","Previous power plan","Restore the plan saved by an earlier ABSOLUTE version",B("Restore",async()=>{await WindowsSystemService.SetPowerPlanAsync(previous);preferences.PreviousPowerPlan=null;store.Save(preferences);Navigate("Settings");}),true)));
        Page.Children.Add(Card(Stack(Text("ABSOLUTE",22,weight:FontWeights.SemiBold),Text($"Version {typeof(App).Assembly.GetName().Version?.ToString(3)}",12,"#A396AF"),Space(12),Text("Windows 10 / Windows 11",13,"#BCAFCA"),Text("Settings and benchmark results stay on this PC.",12,"#A396AF"))));
    }
}
