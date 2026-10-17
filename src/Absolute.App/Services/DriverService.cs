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
              $found+=@{Title=$update.Title;HardwareId=$update.DriverHardwareID;Provider=$update.DriverProvider;Version=$update.DriverVerDate.ToString('yyyy-MM-dd')}
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
        ("asustek","https://www.asus.com/support/download-center/"),("asus","https://www.asus.com/support/download-center/"),("micro-star","https://www.msi.com/support"),("gigabyte","https://www.gigabyte.com/Support"),("asrock","https://www.asrock.com/support/"),
        ("dell","https://www.dell.com/support/home"),("lenovo","https://pcsupport.lenovo.com/"),("hewlett","https://support.hp.com/drivers"),("realtek","https://www.realtek.com/Download/List?cate_id=593&menu_id=298"),("samsung","https://www.samsung.com/support/downloads/"),("benq","https://www.benq.com/en-us/support.html"),("lg electronics","https://www.lg.com/us/support/software-firmware-drivers"),("nzxt","https://nzxt.com/software/cam")
    ];
}
