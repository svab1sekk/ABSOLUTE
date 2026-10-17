using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Absolute.App.Services;
using Absolute.Core;
using static Absolute.App.Controls.Ui;

namespace Absolute.App;

public partial class MainWindow
{
    private void BuildPc()
    {
        var installed=inventory?.InstalledMemory??hardware?.Memory??0;
        var usable=hardware?.Memory??0;
        Page.Children.Add(Card(Columns((Stack(Text(hardware?.Machine??Environment.MachineName,28,weight:FontWeights.SemiBold),Text(hardware?.OperatingSystem??"Windows",13,"#A396AF"),Space(16),Text($"{GiB(installed):0.#} GB installed · {GiB(usable):0.#} GB usable",14,"#CCB4E2")),1),(Icon("PC",58,"#8B65AF"),-1))));
        if(inventory is null)
        {
            Page.Children.Add(Card(Text(string.IsNullOrEmpty(inventoryError)?"Discovering hardware…":inventoryError,14,"#A396AF")));return;
        }
        var parts=inventory.Parts.ToList();
        var cooling=sensorSnapshot.Readings.Where(s=>s.Type is "Fan" or "Flow" or "Control"||s.Name.Contains("pump",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(cooling.Length>0)parts.Add(new("Cooling","Cooling sensors",cooling.Select(s=>new KeyValuePair<string,string>(s.Name,$"{s.Value:0.#} {(s.Type=="Fan"?"RPM":s.Type=="Flow"?"L/h":"%")}")).ToArray()));
        var rows=new StackPanel();
        for(var i=0;i<parts.Count;i+=2)
        {
            var first=PartCard(parts[i]);first.Margin=new Thickness(0,0,9,18);
            if(i+1<parts.Count){var second=PartCard(parts[i+1]);second.Margin=new Thickness(9,0,0,18);rows.Children.Add(Columns((first,1),(second,1)));}
            else rows.Children.Add(first);
        }
        Page.Children.Add(rows);
        if(cooling.Length==0)Page.Children.Add(Card(Stack(Text("Cooling",18,weight:FontWeights.SemiBold),Space(10),Text("Windows has not exposed a fan or pump sensor. A compatible hardware-monitor provider can expose supported air or liquid-cooling telemetry.",12,"#A396AF"),Space(12),B("Sensor setup",ShowSensorSetup))));
        if(inventory.Unavailable.Count>0)Page.Children.Add(Text("Not reported by this PC: "+string.Join(", ",inventory.Unavailable.Distinct()),12,"#9785A5"));
    }
    private static Border PartCard(HardwarePart part)
    {
        var body=Stack(Text(part.Category.ToUpperInvariant(),10,"#A188B8",FontWeights.SemiBold),Space(10),Text(part.Name,18,weight:FontWeights.SemiBold),Space(12));
        foreach(var detail in part.Details)body.Children.Add(Info(detail.Key,detail.Value));
        return Card(body,new Thickness(22));
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
        driverHomeStatus.Text=driverChecking?"Checking Windows Update…":driverSearch?.Error is not null?"Online check unavailable":devices.Count>0?$"{devices.Count} devices"+(driverSearch is null?"":$" · {driverSearch.Updates.Count} update offers"):string.IsNullOrEmpty(devicesError)?"Discovering devices…":"Inventory unavailable";
    }
    private sealed record DriverRow(string Device,string Category,string Provider,string Version,string Status,DriverDevice Source);
    private void BuildDrivers()
    {
        var offered=driverSearch?.Updates.Count??0;
        var status=driverChecking?"Checking Windows Update…":driverSearch is null?"Not checked online yet":driverSearch.Error is not null?"Windows Update check failed":$"{offered} applicable update{(offered==1?"":"s")} offered · {driverSearch.CheckedAt.LocalDateTime:t}";
        var check=B("Check updates",RunDriverCheckAsync,true);check.IsEnabled=!driverChecking;
        var cancel=B("Cancel",()=>driverCancellation?.Cancel());cancel.IsEnabled=driverChecking;cancel.Margin=new Thickness(10,0,0,0);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(check);actions.Children.Add(cancel);
        Page.Children.Add(Card(Columns((Stack(Text($"{devices.Count} devices",24,weight:FontWeights.SemiBold),Space(6),Text(status,12,"#AE91C8")),1),(actions,-1))));
        if(driverSearch?.Error is string error)Page.Children.Add(Card(Text(error,12,"#C99CB6")));
        if(!string.IsNullOrEmpty(devicesError))Page.Children.Add(Card(Text(devicesError,12,"#C99CB6")));
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,CanUserResizeRows=false,EnableRowVirtualization=true,EnableColumnVirtualization=true,SelectionMode=DataGridSelectionMode.Single,Height=480,Margin=new Thickness(0,0,0,16)};
        DataGridTextColumn Column(string label,string property,double width)
        {
            var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));style.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.NoWrap));style.Setters.Add(new Setter(VerticalAlignmentProperty,VerticalAlignment.Center));style.Setters.Add(new Setter(MarginProperty,new Thickness(8,0,8,0)));
            return new DataGridTextColumn{Header=label,Binding=new Binding(property),Width=new DataGridLength(width,DataGridLengthUnitType.Star),ElementStyle=style};
        }
        grid.Columns.Add(Column("Device","Device",2.4));grid.Columns.Add(Column("Class","Category",.8));grid.Columns.Add(Column("Provider","Provider",1.1));grid.Columns.Add(Column("Installed version","Version",1.2));grid.Columns.Add(Column("Update check","Status",1.3));
        void Filter()
        {
            grid.ItemsSource=devices.Where(d=>(driverCategory=="All devices"||Category(d.Category)==driverCategory)&&(d.Name+" "+d.Manufacturer+" "+d.Provider).Contains(driverQuery,StringComparison.OrdinalIgnoreCase))
                .Select(d=>new DriverRow(d.Name,Category(d.Category),d.Provider,d.Version,Updates(d).Any()?"Update offered":d.ProblemCode==28?"Driver missing":driverChecking?"Checking…":driverSearch?.Error is not null?"Check failed":driverSearch is null?"Not checked":"No offer from WU",d)).ToArray();
        }
        var search=Search(driverQuery,"Search installed hardware",value=>{driverQuery=value;Filter();});
        var categories=new ComboBox{Width=180,Margin=new Thickness(12,0,0,0)};
        categories.Items.Add("All devices");foreach(var category in devices.Select(d=>Category(d.Category)).Distinct().Order())categories.Items.Add(category);
        categories.SelectedItem=categories.Items.Contains(driverCategory)?driverCategory:"All devices";categories.SelectionChanged+=(_,_)=>{driverCategory=categories.SelectedItem?.ToString()??"All devices";Filter();};
        var toolbar=Columns((search,1),(categories,-1));toolbar.Margin=new Thickness(0,0,0,16);Page.Children.Add(toolbar);Page.Children.Add(grid);
        var detail=Card(Text("Select a device to inspect its driver and manufacturer support.",12,"#A396AF"));Page.Children.Add(detail);
        grid.SelectionChanged+=(_,_)=>
        {
            if(grid.SelectedItem is not DriverRow row)return;
            var d=row.Source;
            var body=Stack(Text(d.Name,19,weight:FontWeights.SemiBold),Space(10),Info("Manufacturer",d.Manufacturer),Info("Installed driver",d.Version),Info("Driver date",d.Date),Info("Signed driver",d.Signed switch{true=>"Yes",false=>"No",_=>"Not reported"}),Info("Device problem",d.ProblemCode switch{0=>"None reported",null=>"Not reported",var problem=>$"Windows code {problem}"}),Info("Device ID",d.Id));
            foreach(var update in Updates(d))body.Children.Add(Info("Windows Update offer",update.Title));
            var links=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,14,0,0)};
            links.Children.Add(B("Manufacturer support  ↗",()=>OpenUrl(DriverService.VendorUrl(d))));var windows=B("Windows Update  ↗",()=>WindowsSystemService.OpenSettings("windowsupdate"));windows.Margin=new Thickness(10,0,0,0);links.Children.Add(windows);body.Children.Add(links);detail.Child=body;
        };
        Filter();
        Page.Children.Add(Text("Windows Update checks all applicable driver offers. “No offer” is not a guarantee of the newest OEM release; use manufacturer support for vendor-only updates. No driver is installed automatically.",12,"#93849F"));
    }
    private IEnumerable<DriverUpdate> Updates(DriverDevice device)=>driverSearch?.Updates.Where(u=>!string.IsNullOrWhiteSpace(u.HardwareId)&&(device.HardwareIds.Contains(u.HardwareId,StringComparer.OrdinalIgnoreCase)||device.Id.StartsWith(u.HardwareId,StringComparison.OrdinalIgnoreCase)))??[];
    private static string Category(string value)=>value.ToUpperInvariant() switch{"DISPLAY"=>"Graphics","NET"=>"Network","MEDIA" or "AUDIOENDPOINT"=>"Audio","MOUSE" or "KEYBOARD" or "HIDCLASS"=>"Input","MONITOR"=>"Monitors","SYSTEM" or "PROCESSOR"=>"Motherboard / CPU","HDC" or "SCSIADAPTER" or "DISKDRIVE"=>"Storage","USB"=>"USB",_=>value};
}
