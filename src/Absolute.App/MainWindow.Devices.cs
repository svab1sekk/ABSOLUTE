using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Absolute.App.Services;
using Absolute.App.Controls;
using Absolute.Core;
using static Absolute.App.Controls.Ui;

namespace Absolute.App;

public partial class MainWindow
{
    private readonly Dictionary<string,VendorDriverResult> vendorResults=new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer driverMonitor=new(){Interval=TimeSpan.FromHours(4)};
    private sealed record MainDriverComponent(HardwarePart Part,IReadOnlyList<DriverDevice> Drivers);

    private void BuildPc()
    {
        var heading=Stack(Text(hardware?.Machine??Environment.MachineName,25,weight:FontWeights.SemiBold),Text(hardware?.OperatingSystem??"Windows",13,"#A396AF"));
        var summary=Card(Columns((heading,1),(Icon("PC",40,"#8B65AF"),-1)));summary.MaxWidth=698;Page.Children.Add(summary);
        if(inventory is null){Page.Children.Add(Text(string.IsNullOrEmpty(inventoryError)?"Discovering hardware…":inventoryError,13,"#A396AF"));return;}
        HardwareGroup("Displays",inventory.Parts.Where(p=>p.Category=="Monitor"),2,PartCard);
        HardwareGroup("Components",inventory.Parts.Where(p=>p.Category is "Processor" or "Graphics" or "Memory" or "Motherboard"),2,PartCard);
        HardwareGroup("Storage",inventory.Parts.Where(p=>p.Category=="Storage"),2,PartCard);
    }

    private void HardwareGroup(string title,IEnumerable<HardwarePart> source,int columns,Func<HardwarePart,Border> render)
    {
        var parts=source.ToArray();if(parts.Length==0)return;
        Section(title);var grid=new CenteredCardsPanel{Columns=columns,CardWidth=columns==3?264:340,Margin=new Thickness(0,0,0,20)};Page.Children.Add(grid);
        foreach(var part in parts){var card=render(part);card.Margin=new Thickness(0);grid.Children.Add(card);}
    }

    private static Border PartCard(HardwarePart part)
    {
        var icon=new Border{Width=38,Height=38,CornerRadius=new CornerRadius(10),Background=Brush("#21182C"),Child=Icon(part.Category,22),Margin=new Thickness(0,0,12,0)};
        var body=Stack(Columns((icon,-1),(Text(PartLabel(part),11,"#AD93C3"),1)),Space(17),Text(part.Name,20,weight:FontWeights.SemiBold));
        if(part.Details.Count>0)body.Children.Add(Space(10));
        foreach(var detail in part.Details)body.Children.Add(Info(detail.Key,detail.Value));
        return Card(body,new Thickness(22));
    }
    private static string PartLabel(HardwarePart part)=>part.Category switch{"Graphics"=>"GPU","Processor"=>"CPU","Memory"=>"RAM",_=>part.Category};

    private IReadOnlyList<MainDriverComponent> MainDriverComponents()
    {
        if(inventory is null)return [];
        return inventory.Parts.Where(p=>p.Category is "Processor" or "Graphics" or "Motherboard" or "Monitor").Select(part=>
        {
            var matches=part.Category switch
            {
                "Processor"=>devices.Where(d=>d.Category.Equals("PROCESSOR",StringComparison.OrdinalIgnoreCase)),
                "Motherboard"=>devices.Where(HardwareIdentity.IsMotherboardDriver),
                _=>devices.Where(d=>d.Id.Equals(part.Id,StringComparison.OrdinalIgnoreCase)||(part.HardwareIds??[]).Any(id=>d.HardwareIds.Contains(id,StringComparer.OrdinalIgnoreCase)))
            };
            return new MainDriverComponent(part,matches.DistinctBy(d=>d.Id).ToArray());
        }).ToArray();
    }

    private async Task RunDriverCheckAsync()
    {
        if(driverChecking||closed||smoke)return;
        using var cancellation=new CancellationTokenSource();driverCancellation=cancellation;driverChecking=true;UpdateHomeDriverStatus();RefreshPage("Drivers");
        try
        {
            await Task.WhenAll(inventoryTask,devicesTask);
            // Re-read installed versions after a user has downloaded and installed a package.
            if(vendorResults.Count>0)
            {
                inventoryTask=LoadInventoryAsync();devicesTask=LoadDevicesAsync();
                await Task.WhenAll(inventoryTask,devicesTask);
            }
            using var gate=new SemaphoreSlim(3);
            await Task.WhenAll(MainDriverComponents().Select(async component=>
            {
                await gate.WaitAsync(cancellation.Token);
                try
                {
                    var result=await Task.Run(()=>DriverService.SearchAsync(component.Part,component.Drivers,cancellation.Token),cancellation.Token);
                    if(!closed){vendorResults[component.Part.Id]=result;UpdateHomeDriverStatus();RefreshPage("Drivers");}
                }
                finally{gate.Release();}
            }));
        }
        catch(OperationCanceledException){}
        finally{driverChecking=false;driverCancellation=null;if(!closed){UpdateHomeDriverStatus();RefreshPage("Drivers");}}
    }

    private void UpdateHomeDriverStatus()
    {
        if(driverHomeStatus is null)return;
        driverHomeStatus.Text=driverChecking?"Checking drivers…":vendorResults.Values.Any(r=>r.Packages.Any(p=>p.Comparison>0))?"Updates available":vendorResults.Count==0?"Main hardware drivers":"Driver links";
    }

    private void BuildDrivers()
    {
        if(inventory is null){Page.Children.Add(Text(string.IsNullOrEmpty(inventoryError)?"Discovering hardware…":inventoryError,13,"#A396AF"));return;}
        var components=MainDriverComponents();
        HardwareGroup("Displays",components.Where(c=>c.Part.Category=="Monitor").Select(c=>c.Part),2,DriverCard);
        HardwareGroup("Core hardware",components.Where(c=>c.Part.Category!="Monitor").Select(c=>c.Part),3,DriverCard);
        Border DriverCard(HardwarePart part)
        {
            var component=components.First(c=>ReferenceEquals(c.Part,part));var result=vendorResults.GetValueOrDefault(part.Id);
            var versions=component.Drivers.Where(d=>!d.Provider.Equals("Microsoft",StringComparison.OrdinalIgnoreCase)).Select(d=>d.Version).Where(v=>!string.IsNullOrWhiteSpace(v)&&v is not "Unavailable" and not "Not reported").Distinct().ToArray();
            var icon=new Border{Width=38,Height=38,Background=Brush("#251B32"),CornerRadius=new CornerRadius(10),Child=Icon(part.Category,22),Margin=new Thickness(0,0,12,0)};
            var model=Text(part.Name,18,weight:FontWeights.SemiBold);model.MinHeight=50;
            var body=Stack(Columns((icon,-1),(Text(PartLabel(part),11,"#B49ACB"),1)),Space(18),model,Space(12));
            if(versions.Length==1&&part.Category is not ("Motherboard" or "Processor"))body.Children.Add(Text("Installed · "+(part.Name.Contains("NVIDIA",StringComparison.OrdinalIgnoreCase)?VendorDrivers.NvidiaVersion(versions[0]):versions[0]),12,"#A89AB4"));
            if(result?.Packages.Count==1)body.Children.Add(Text("Latest · "+result.Packages[0].Version,12,"#BFA4D5"));
            if(part.Category=="Motherboard")body.Children.Add(Text("Chipset · LAN · Wi-Fi · Bluetooth",11,"#A89AB4"));
            var updates=result?.Packages.Count(p=>p.Comparison>0)??0;
            var current=result?.Packages.Count>0&&result.Packages.All(p=>p.Comparison is <=0);
            var status=updates>0?"Update available":current?"Up to date":part.Category=="Processor"?"CPU support is included in chipset packages":driverChecking?"Checking…":result?.Source.Length==0?"Driver link unavailable":"";
            var detail=Text(status,12,updates>0?"#D0ABE9":"#AFA0BC");detail.MinHeight=48;detail.Margin=new Thickness(0,12,0,18);body.Children.Add(detail);
            if(result?.Packages.Count>0&&(!current||part.Category=="Motherboard"))
            {
                var direct=result.Packages.Count==1;
                var packages=B("Download  ↗",()=>{if(direct)OpenUrl(result.Packages[0].DownloadUrl);else ShowDriverPackages(part,result);},updates>0);packages.HorizontalAlignment=HorizontalAlignment.Left;packages.Margin=new Thickness(0,0,0,10);body.Children.Add(packages);
            }
            var source=result?.Source??DriverService.SupportUrl(part);
            if(source.Length>0){var support=B("Drivers  ↗",()=>OpenUrl(source));support.HorizontalAlignment=HorizontalAlignment.Left;body.Children.Add(support);}
            return Card(body,new Thickness(20));
        }
    }

    private void ShowDriverPackages(HardwarePart part,VendorDriverResult result)
    {
        var content=Stack(Text(part.Name,25,weight:FontWeights.SemiBold),Space(8),Text($"Checked {result.CheckedAt.LocalDateTime:g}",12,"#A396AF"),Space(22));
        foreach(var package in result.Packages)
        {
            var info=Stack(Text(package.Name,16,weight:FontWeights.SemiBold),Space(7),Text("Latest · "+package.Version+(package.InstalledVersion is string current?"    Installed · "+current:""),12,"#BDA9D0"),Space(5),Text(package.Status,12,package.Comparison>0?"#D4AFE9":"#A396AF"));
            var download=B("Download  ↗",()=>OpenUrl(package.DownloadUrl),true);download.Margin=new Thickness(18,0,0,0);
            content.Children.Add(Card(Columns((info,1),(download,-1)),new Thickness(19)));
        }
        var dialog=new Window{Title="Driver downloads",Owner=this,Width=730,Height=580,MinWidth=650,MinHeight=420,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#0E0B13"),Foreground=Brush("#F2EFF7"),FontFamily=FontFamily,Content=new ScrollViewer{Content=content,Margin=new Thickness(28)},ShowInTaskbar=false};
        dialog.ShowDialog();
    }
}
