using Absolute.Core;
using Vortice.DXGI;

namespace Absolute.App.Services;

internal static class GraphicsDiscovery
{
    public static IDXGIAdapter1 PreferredAdapter(IDXGIFactory1 factory)
    {
        using var modern=factory.QueryInterfaceOrNull<IDXGIFactory6>();
        if(modern is not null)
            for(uint index=0;modern.EnumAdapterByGpuPreference(index,GpuPreference.HighPerformance,out IDXGIAdapter1? adapter).Success;index++)
            {
                if(adapter is null)break;
                if(IsHardware(adapter.Description1))return adapter;
                adapter.Dispose();
            }
        for(uint index=0;factory.EnumAdapters1(index,out var adapter).Success;index++)
        {
            if(IsHardware(adapter.Description1))return adapter;
            adapter.Dispose();
        }
        throw new NotSupportedException("A hardware graphics adapter is unavailable. Software rendering is not scored.");
    }

    public static bool IsHardware(AdapterDescription1 description)=>
        description.VendorId!=0x1414&&
        (description.Flags&AdapterFlags.Software)==0&&
        !description.Description.Contains("Basic Render Driver",StringComparison.OrdinalIgnoreCase)&&
        !description.Description.Contains("Basic Display Adapter",StringComparison.OrdinalIgnoreCase)&&
        !description.Description.Contains("Software",StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<HardwarePart> Read()
    {
        var result=new List<HardwarePart>();
        using var factory=DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for(uint index=0;factory.EnumAdapters1(index,out var adapter).Success;index++)
        {
            using(adapter)
            {
                var description=adapter.Description1;
                if(!IsHardware(description))continue;
                result.Add(new("Graphics",HardwareNames.Clean(description.Description),new Dictionary<string,string>
                {
                    [description.DedicatedVideoMemory>0?"VRAM":"Memory"] = description.DedicatedVideoMemory>0?$"{(ulong)description.DedicatedVideoMemory/1073741824d:0.#} GB":"Shared"
                }.ToArray()));
            }
        }
        return result;
    }
}
