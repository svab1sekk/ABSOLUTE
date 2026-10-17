using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Absolute.App.Services;
using Absolute.Core;
using static Absolute.App.Controls.Ui;

namespace Absolute.App;

public partial class MainWindow
{
    private CancellationTokenSource? driverDownloadCancellation;
    private string? downloadingComponent;
    private sealed record MainDriverComponent(HardwarePart Part,IReadOnlyList<DriverDevice> Drivers);

    private void BuildPc()
    {
        var heading=Stack(Text(hardware?.Machine??Environment.MachineName,25,weight:FontWeights.SemiBold),Text(hardware?.OperatingSystem??"Windows",13,"#A396AF"));
        Page.Children.Add(Card(Columns((heading,1),(Icon("PC",46,"#8B65AF"),-1))));
        if(inventory is null){Page.Children.Add(Text(string.IsNullOrEmpty(inventoryError)?"Discovering hardware…":inventoryError,13,"#A396AF"));return;}
        var grid=new UniformGrid{Columns=2};Page.Children.Add(grid);
        foreach(var part in inventory.Parts)
        {
            var card=PartCard(part);card.Margin=new Thickness(grid.Children.Count%2==0?0:9,0,grid.Children.Count%2==0?9:0,18);grid.Children.Add(card);
        }
    }

    private static Border PartCard(HardwarePart part)
    {
        var icon=new Border{Width=42,Height=42,CornerRadius=new CornerRadius(10),Background=Brush("#21182C"),Child=Icon(part.Category,23),Margin=new Thickness(0,0,14,0)};
        var body=Stack(Columns((icon,-1),(Text(part.Category=="Graphics"?"GPU":part.Category=="Processor"?"CPU":part.Category,11,"#AD93C3"),1)),Space(18),Text(part.Name,22,weight:FontWeights.SemiBold));
        if(part.Details.Count>0)body.Children.Add(Space(12));
        foreach(var detail in part.Details)body.Children.Add(Info(detail.Key,detail.Value));
        return Card(body,new Thickness(24));
    }

    private IReadOnlyList<MainDriverComponent> MainDriverComponents()
    {
        if(inventory is null)return [];
        return inventory.Parts.Where(p=>p.Category is "Processor" or "Graphics" or "Motherboard" or "Keyboard" or "Mouse" or "Monitor").Select(part=>
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
        try{driverSearch=await DriverService.SearchAsync(cancellation.Token);}
        catch(OperationCanceledException){}
        finally{driverChecking=false;driverCancellation=null;if(!closed){UpdateHomeDriverStatus();RefreshPage("Drivers");}}
    }

    private void UpdateHomeDriverStatus()
    {
        if(driverHomeStatus is null)return;
        driverHomeStatus.Text=driverChecking?"Checking updates…":driverSearch?.Error is not null?"Online check unavailable":driverSearch is null?"Main hardware drivers":MainDriverComponents().Any(c=>ComponentUpdates(c).Any())?"Updates available":"Checked Windows Update";
    }

    private void BuildDrivers()
    {
        var components=MainDriverComponents();
        if(inventory is null){Page.Children.Add(Text(string.IsNullOrEmpty(inventoryError)?"Discovering hardware…":inventoryError,13,"#A396AF"));return;}
        var grid=new UniformGrid{Columns=2};Page.Children.Add(grid);
        foreach(var component in components)
        {
            var part=component.Part;var offers=ComponentUpdates(component).ToArray();
            var pending=offers.Where(u=>!u.Downloaded).ToArray();
            var label=part.Category=="Processor"?"CPU":part.Category=="Graphics"?"GPU":part.Category;
            var installed=component.Drivers.Select(d=>d.Version).Where(v=>!string.IsNullOrWhiteSpace(v)&&v is not "Unavailable" and not "Not reported").Distinct().ToArray();
            var icon=new Border{Width=42,Height=42,Background=Brush("#251B32"),CornerRadius=new CornerRadius(11),Child=Icon(part.Category,23),Margin=new Thickness(0,0,14,0)};
            var body=Stack(Columns((icon,-1),(Text(label,12,"#B49ACB"),1)),Space(18),Text(part.Name,22,weight:FontWeights.SemiBold),Space(12));
            if(installed.Length==1&&part.Category!="Motherboard")body.Children.Add(Text("Installed · "+installed[0],12,"#9D8EAA"));
            if(part.Category=="Motherboard")body.Children.Add(Text("Chipset"+(component.Drivers.Any(d=>d.Category.Equals("NET",StringComparison.OrdinalIgnoreCase))?" · Network":"")+(component.Drivers.Any(d=>d.Category.Equals("BLUETOOTH",StringComparison.OrdinalIgnoreCase))?" · Bluetooth":""),12,"#9D8EAA"));
            var status=downloadingComponent==part.Id?"Downloading…":driverChecking?"Checking updates…":pending.Length>0?"Update available":offers.Length>0?"Downloaded":driverSearch?.Error is not null?"Check unavailable":driverSearch is null?"Not checked yet":"No update offered by Windows";
            body.Children.Add(Space(10));body.Children.Add(Text(status,12,pending.Length>0?"#D0ABE9":"#AFA0BC"));body.Children.Add(Space(20));
            var actions=new WrapPanel();
            if(pending.Length>0)
            {
                var download=B("Download",async()=>
                {
                    using var cancellation=new CancellationTokenSource();driverDownloadCancellation=cancellation;downloadingComponent=part.Id;RefreshPage("Drivers");
                    try
                    {
                        var downloaded=await DriverService.DownloadAsync(pending,cancellation.Token);
                        if(driverSearch is not null)driverSearch=driverSearch with{Updates=driverSearch.Updates.Select(u=>downloaded.Contains(u.UpdateId,StringComparer.OrdinalIgnoreCase)?u with{Downloaded=true}:u).ToArray()};
                        Notify("Drivers downloaded to Windows Update.");
                    }
                    finally{driverDownloadCancellation=null;downloadingComponent=null;if(!closed)RefreshPage("Drivers");}
                },true);
                download.IsEnabled=downloadingComponent is null;download.Margin=new Thickness(0,0,10,8);actions.Children.Add(download);
            }
            else if(offers.Length>0)
            {
                var install=B("Windows Update  ↗",()=>WindowsSystemService.OpenSettings("windowsupdate"));install.Margin=new Thickness(0,0,10,8);actions.Children.Add(install);
            }
            var support=B(part.Category=="Motherboard"?"Motherboard packages  ↗":"Manufacturer  ↗",()=>OpenUrl(DriverService.SupportUrl(part)));support.Margin=new Thickness(0,0,0,8);actions.Children.Add(support);body.Children.Add(actions);
            var card=Card(body,new Thickness(24));card.Margin=new Thickness(grid.Children.Count%2==0?0:9,0,grid.Children.Count%2==0?9:0,18);grid.Children.Add(card);
        }
        var check=B("Check updates",RunDriverCheckAsync);check.IsEnabled=!driverChecking&&downloadingComponent is null;check.HorizontalAlignment=HorizontalAlignment.Left;Page.Children.Add(check);
        var note=Text("Windows Update downloads · Vendor packages through manufacturer support",11,"#8F7F9E");note.Margin=new Thickness(0,14,0,0);Page.Children.Add(note);
        if(driverSearch?.Error is string error){var detail=Text(error,12,"#BF91AB");detail.Margin=new Thickness(0,12,0,0);Page.Children.Add(detail);}
    }

    private IEnumerable<DriverUpdate> ComponentUpdates(MainDriverComponent component)=>driverSearch?.Updates.Where(update=>component.Drivers.Any(device=>!string.IsNullOrWhiteSpace(update.HardwareId)&&(device.HardwareIds.Contains(update.HardwareId,StringComparer.OrdinalIgnoreCase)||device.Id.StartsWith(update.HardwareId,StringComparison.OrdinalIgnoreCase)))).DistinctBy(u=>u.UpdateId.Length>0?u.UpdateId:u.Title)??[];
}
