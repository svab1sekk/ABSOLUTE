using Absolute.Core;
using Vortice.DXGI;

namespace Absolute.App.Services;

internal static class GraphicsDiscovery
{
    public static IReadOnlyList<HardwarePart> Read()
    {
        var result=new List<HardwarePart>();
        using var factory=DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for(uint index=0;factory.EnumAdapters1(index,out var adapter).Success;index++)
        {
            using(adapter)
            {
                var description=adapter.Description1;
                if((description.Flags&AdapterFlags.Software)!=0)continue;
                result.Add(new("Graphics",HardwareNames.Clean(description.Description),new Dictionary<string,string>
                {
                    [description.DedicatedVideoMemory>0?"VRAM":"Memory"] = description.DedicatedVideoMemory>0?$"{(ulong)description.DedicatedVideoMemory/1073741824d:0.#} GB":"Shared"
                }.ToArray()));
            }
        }
        return result;
    }
}
