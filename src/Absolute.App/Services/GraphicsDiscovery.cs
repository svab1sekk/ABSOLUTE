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
                    ["Dedicated VRAM"]=$"{(ulong)description.DedicatedVideoMemory/1073741824d:0.#} GB",
                    ["Shared memory limit"]=$"{(ulong)description.SharedSystemMemory/1073741824d:0.#} GB",
                    ["Vendor / device"]=$"{description.VendorId:X4} / {description.DeviceId:X4}"
                }.ToArray()));
            }
        }
        return result;
    }
}
