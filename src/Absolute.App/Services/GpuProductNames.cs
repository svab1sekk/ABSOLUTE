using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Absolute.App.Services;

internal static class GpuProductNames
{
    private static readonly Lazy<IReadOnlyDictionary<string,string>> Names=new(()=>
    {
        using var resource=typeof(GpuProductNames).Assembly.GetManifestResourceStream("Absolute.GpuPciIds")!;
        using var stream=new GZipStream(resource,CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??[];
    });
    public static string Resolve(string reported,string deviceId)
    {
        if(Regex.IsMatch(reported,"NVIDIA|GeForce|Quadro|Tesla|AMD|Radeon|Intel|Arc|Iris|UHD",RegexOptions.IgnoreCase))return reported;
        var match=Regex.Match(deviceId,@"VEN_(?<vendor>[0-9a-f]{4})&DEV_(?<device>[0-9a-f]{4})",RegexOptions.IgnoreCase);
        if(!match.Success)return reported;
        var vendor=match.Groups["vendor"].Value.ToLowerInvariant();
        if(!Names.Value.TryGetValue(vendor+":"+match.Groups["device"].Value.ToLowerInvariant(),out var value))return reported;
        var model=Regex.Match(value,@"\[([^]]+)\]");if(model.Success)value=model.Groups[1].Value;
        var brand=vendor switch{"10de"=>"NVIDIA","1002"=>"AMD","8086"=>"Intel",_=>""};
        return value.StartsWith(brand,StringComparison.OrdinalIgnoreCase)?value:brand+" "+value;
    }
}
