using System.Windows;
using System.Windows.Controls;
using Absolute.App.Controls;
using static Absolute.App.Controls.Ui;

namespace Absolute.App;

public partial class MainWindow
{
    private void BuildHome()
    {
        cpuValue=Text(!cpuHasInterval||snapshot is null?"—":$"{snapshot.CpuPercent:F0}%",37,weight:FontWeights.SemiBold);
        gpuValue=Text(sensorSnapshot.GpuLoad is double load?$"{load:F0}%":"—",37,weight:FontWeights.SemiBold);
        memoryValue=Text(snapshot is null?"—":$"{GiB(snapshot.MemoryUsed):F1} GB",34,weight:FontWeights.SemiBold);
        memoryDetail=Text(snapshot is null?"Reading memory…":$"of {GiB(snapshot.MemoryTotal):F1} GB usable",12,"#A396AF");
        cpuTemperature=Text(Temperature(sensorSnapshot.CpuTemperature),13,"#C7A9E6");gpuTemperature=Text(Temperature(sensorSnapshot.GpuTemperature),13,"#C7A9E6");
        cpuTemperature.ToolTip=temperatures.Status;
        gpuTemperature.ToolTip="Built-in GPU temperature monitoring";
        cpuChart=Chart(cpuHistory);gpuChart=Chart(gpuHistory);memoryChart=Chart(memoryHistory);
        var cpu=Tile("CPU",hardware?.Processor??"Processor",cpuValue,cpuTemperature,cpuChart);
        var gpu=Tile("GPU",hardware?.Graphics??"Graphics",gpuValue,gpuTemperature,gpuChart);
        var memory=Tile("RAM",inventory?.Parts.FirstOrDefault(p=>p.Category=="Memory")?.Name??"Physical memory",memoryValue,memoryDetail,memoryChart);
        cpu.Margin=new Thickness(0,0,10,18);gpu.Margin=new Thickness(5,0,5,18);memory.Margin=new Thickness(10,0,0,18);
        Page.Children.Add(Columns((cpu,1),(gpu,1),(memory,1)));
        Section("Quick access");
        var benchmark=Card(Row("Benchmark","Benchmark","CPU · GPU 3D · RAM",B("Open",()=>Select("Benchmark")),true),new Thickness(18,2,18,2));benchmark.Margin=new Thickness(0,0,10,18);
        driverHomeStatus=Text("",12,"#A396AF");UpdateHomeDriverStatus();
        var driverGlyph=Icon("Drivers",24);driverGlyph.Margin=new Thickness(0,0,14,0);
        var driver=Card(Columns((driverGlyph,-1),(Stack(Text("Drivers",14,weight:FontWeights.SemiBold),driverHomeStatus),1),(B("Open",()=>Select("Drivers")),-1)),new Thickness(20));driver.Margin=new Thickness(10,0,0,18);
        Page.Children.Add(Columns((benchmark,1),(driver,1)));
        Section("Tools");
        var tools=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};Page.Children.Add(tools);
        Tool("Presets","Game Mode","gaming-gamemode");Tool("System","Startup apps","startupapps");
        Tool("Storage","Storage cleanup","storagesense");Tool("Drivers","Windows Update","windowsupdate");
        void Tool(string icon,string label,string settings)
        {
            var open=B("Open",()=>Services.WindowsSystemService.OpenSettings(settings));
            var card=Card(Columns((Icon(icon,22),-1),(Text(label,14,weight:FontWeights.SemiBold),1),(open,-1)),new Thickness(20));
            ((Grid)card.Child).Children[1].SetValue(MarginProperty,new Thickness(14,0,10,0));
            card.Margin=new Thickness(tools.Children.Count%2==0?0:9,0,tools.Children.Count%2==0?9:0,18);tools.Children.Add(card);
        }
    }
    private static Sparkline Chart(IEnumerable<double> history)
    {
        var chart=new Sparkline{Height=74,Margin=new Thickness(0,12,0,0)};
        foreach(var value in history)chart.Add(value);return chart;
    }
    private static Border Tile(string label,string name,TextBlock value,TextBlock detail,UIElement extra)
    {
        var title=Text(name,12,"#B3A5C0");title.Height=40;title.VerticalAlignment=VerticalAlignment.Top;title.Margin=new Thickness(0,10,0,8);
        return Card(Stack(Text(label,10,"#A396AF",FontWeights.SemiBold),title,value,detail,Space(13),extra),new Thickness(20));
    }
}
