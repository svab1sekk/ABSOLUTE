using System.Text.Json;
using Absolute.Core;

namespace Absolute.App.Services;

internal static class DriverService
{
    public static async Task<DriverSearchResult> SearchAsync(CancellationToken cancellationToken)
    {
        const string script="""
            $session=New-Object -ComObject Microsoft.Update.Session
            $session.ClientApplicationID='ABSOLUTE driver check'
            $searcher=$session.CreateUpdateSearcher()
            $searcher.Online=$true
            $result=$searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0")
            $found=@()
            for($i=0;$i -lt $result.Updates.Count;$i++){
              $update=$result.Updates.Item($i)
              $found+=@{Title=$update.Title;HardwareId=$update.DriverHardwareID;Provider=$update.DriverProvider;Version=$update.DriverVerDate.ToString('yyyy-MM-dd');UpdateId=$update.Identity.UpdateID;Revision=$update.Identity.RevisionNumber;Downloaded=$update.IsDownloaded}
            }
            ConvertTo-Json -InputObject @($found) -Compress -Depth 4
            """;
        try
        {
            var json=await PowerShellRunner.RunAsync(script,cancellationToken,TimeSpan.FromMinutes(5));
            return new(JsonSerializer.Deserialize<DriverUpdate[]>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??[],DateTimeOffset.UtcNow);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){return new([],DateTimeOffset.UtcNow,ex.Message);}
    }

    public static async Task<IReadOnlyList<string>> DownloadAsync(IEnumerable<DriverUpdate> offers,CancellationToken cancellationToken)
    {
        var selected=offers.Where(u=>!u.Downloaded).DistinctBy(u=>u.UpdateId).ToArray();
        if(selected.Any(u=>!Guid.TryParse(u.UpdateId,out _)||u.Revision<0))throw new InvalidOperationException("The driver offer has an invalid update identity.");
        if(selected.Length==0)return [];
        var json=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(selected.Select(u=>new{u.UpdateId,u.Revision}))));
        var script="""
            $requests=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__REQUESTS__')) | ConvertFrom-Json
            $session=New-Object -ComObject Microsoft.Update.Session
            $session.ClientApplicationID='ABSOLUTE driver download'
            $searcher=$session.CreateUpdateSearcher()
            $collection=New-Object -ComObject Microsoft.Update.UpdateColl
            foreach($request in $requests){
              $criteria="UpdateID='$($request.UpdateId)' and RevisionNumber=$($request.Revision)"
              $found=$searcher.Search($criteria)
              if($found.Updates.Count -ne 1){throw 'The selected driver offer is no longer available. Check updates again.'}
              $update=$found.Updates.Item(0)
              if(-not $update.EulaAccepted){throw 'This driver requires its license to be accepted in Windows Update.'}
              [void]$collection.Add($update)
            }
            $downloader=$session.CreateUpdateDownloader()
            $downloader.Updates=$collection
            $result=$downloader.Download()
            $downloaded=@()
            for($i=0;$i -lt $collection.Count;$i++){
              if($result.GetUpdateResult($i).ResultCode -eq 2){$downloaded+=$collection.Item($i).Identity.UpdateID}
            }
            if($downloaded.Count -ne $collection.Count){throw 'Windows could not download every selected package. Open Windows Update for details.'}
            ConvertTo-Json -InputObject @($downloaded) -Compress
            """;
        var result=await PowerShellRunner.RunAsync(script.Replace("__REQUESTS__",json,StringComparison.Ordinal),cancellationToken,TimeSpan.FromMinutes(15));
        return JsonSerializer.Deserialize<string[]>(result)??[];
    }

    public static string SupportUrl(HardwarePart component)
    {
        var name=component.Name;var query=Uri.EscapeDataString(name);
        if(component.Category=="Motherboard")
        {
            var model=System.Text.RegularExpressions.Regex.Replace(name,@"^(ASUS|MSI|Gigabyte|ASRock)\s+","",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if(name.StartsWith("ASUS",StringComparison.OrdinalIgnoreCase))return "https://www.asus.com/supportonly/"+Uri.EscapeDataString(model)+"/helpdesk_download/";
            if(name.StartsWith("MSI",StringComparison.OrdinalIgnoreCase))return "https://www.msi.com/Motherboard/"+Uri.EscapeDataString(System.Text.RegularExpressions.Regex.Replace(model,@"\s*\(MS-[^)]*\)","").Trim().Replace(' ','-'))+"/support#driver";
            if(name.Contains("Gigabyte",StringComparison.OrdinalIgnoreCase))return "https://www.gigabyte.com/Search?kw="+query;
            if(name.Contains("ASRock",StringComparison.OrdinalIgnoreCase))return "https://www.asrock.com/support/index.asp?Model="+Uri.EscapeDataString(model);
        }
        if(name.Contains("TITAN ARMY",StringComparison.OrdinalIgnoreCase))return "https://www.titan-army.com/";
        if(particular("LG "))return "https://www.lg.com/us/support/software-firmware-drivers";
        if(particular("AOC "))return "https://aoc.com/";
        if(particular("Acer "))return "https://www.acer.com/us-en/support/drivers-and-manuals";
        if(particular("Philips "))return "https://www.philips.com/support";
        if(particular("ViewSonic "))return "https://www.viewsonic.com/global/support/";
        if(particular("HP "))return "https://support.hp.com/drivers";
        if(particular("iiyama "))return "https://iiyama.com/gl_en/support/";
        return VendorUrl(new("",name,component.Category,name,"","","",[],null));
        bool particular(string brand)=>name.StartsWith(brand,StringComparison.OrdinalIgnoreCase);
    }

    public static string VendorUrl(DriverDevice device)
    {
        var value=(device.Manufacturer+" "+device.Provider+" "+device.Name).ToLowerInvariant();
        foreach(var (token,url) in Vendors)if(value.Contains(token,StringComparison.Ordinal))return url;
        return "https://support.microsoft.com/windows/automatically-get-recommended-drivers-and-updates-for-your-hardware-4efb3701-345d-44db-dbff-8017b0e6b341";
    }
    private static readonly (string,string)[] Vendors=
    [
        ("nvidia","https://www.nvidia.com/Download/index.aspx"),("advanced micro","https://www.amd.com/en/support/download/drivers.html"),("amd","https://www.amd.com/en/support/download/drivers.html"),("intel","https://www.intel.com/content/www/us/en/download-center/home.html"),
        ("logitech","https://support.logi.com/"),("razer","https://mysupport.razer.com/"),("corsair","https://www.corsair.com/us/en/s/downloads"),("steelseries","https://steelseries.com/gg"),("hyperx","https://hyperx.com/pages/ngenuity"),
        ("wooting","https://wooting.io/wootility"),("keychron","https://www.keychron.com/"),("glorious","https://www.gloriousgaming.com/pages/software"),("pulsar","https://www.pulsar.gg/pages/download"),("endgame","https://www.endgamegear.com/"),("vaxee","https://www.vaxee.co/en/"),("zowie","https://zowie.benq.com/en-us/support.html"),("ducky","https://www.duckychannel.com.tw/"),
        ("asustek","https://www.asus.com/support/download-center/"),("asus","https://www.asus.com/support/download-center/"),("micro-star","https://www.msi.com/support"),("gigabyte","https://www.gigabyte.com/Support"),("asrock","https://www.asrock.com/support/"),
        ("dell","https://www.dell.com/support/home"),("lenovo","https://pcsupport.lenovo.com/"),("hewlett","https://support.hp.com/drivers"),("realtek","https://www.realtek.com/Download/List?cate_id=593&menu_id=298"),("samsung","https://www.samsung.com/support/downloads/"),("benq","https://www.benq.com/en-us/support.html"),("lg electronics","https://www.lg.com/us/support/software-firmware-drivers"),("nzxt","https://nzxt.com/software/cam")
    ];
}
