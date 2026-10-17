using System.Diagnostics;
using System.Numerics;
using Absolute.Core;
using Absolute.App.Services;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Absolute.App.Controls;

internal static class Gpu3DWorkload
{
    public const double Seconds=10;
    private static readonly Lazy<(ReadOnlyMemory<byte> Vertex,ReadOnlyMemory<byte> Pixel)> Shaders=new(()=>
        (Compiler.Compile(Shader,"VSMain","AbsoluteScene1","vs_5_0",ShaderFlags.OptimizationLevel3),Compiler.Compile(Shader,"PSMain","AbsoluteScene1","ps_5_0",ShaderFlags.OptimizationLevel3)));
    public static void ValidateShaders()=>_ = Shaders.Value;
    public static ComponentBenchmarkResult Measure(IntPtr window,IProgress<double>? progress,CancellationToken cancellationToken)
    {
        var shaders=Shaders.Value;
        using var factory=DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        using var selected=GraphicsDiscovery.PreferredAdapter(factory);
        ID3D11Device device;ID3D11DeviceContext context;
        try{D3D11.D3D11CreateDevice(selected,DriverType.Unknown,DeviceCreationFlags.BgraSupport,[FeatureLevel.Level_11_0],out device,out _,out context).CheckError();}
        catch(Exception error){throw new NotSupportedException("GPU 3D needs a hardware Direct3D 11 driver. A software renderer is not scored.",error);}
        using(device)
        using(context)
        using(var dxgi=device.QueryInterface<IDXGIDevice>())
        using(var adapter=dxgi.GetAdapter())
        {
            using var adapter1=adapter.QueryInterface<IDXGIAdapter1>();
            if(!GraphicsDiscovery.IsHardware(adapter1.Description1))throw new NotSupportedException("GPU 3D is unavailable on a software adapter.");
            var desc=new SwapChainDescription1{Width=1280,Height=720,Format=Format.R8G8B8A8_UNorm,BufferCount=2,BufferUsage=Usage.RenderTargetOutput,SampleDescription=new SampleDescription(1,0),Scaling=Scaling.Stretch,SwapEffect=SwapEffect.Discard,AlphaMode=AlphaMode.Ignore};
            using var swap=factory.CreateSwapChainForHwnd(device,window,desc);
            factory.MakeWindowAssociation(window,WindowAssociationFlags.IgnoreAltEnter);
            using var back=swap.GetBuffer<ID3D11Texture2D>(0);
            using var target=device.CreateRenderTargetView(back);
            using var depth=device.CreateTexture2D(new Texture2DDescription{Width=1280,Height=720,MipLevels=1,ArraySize=1,Format=Format.D32_Float,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.DepthStencil});
            using var depthView=device.CreateDepthStencilView(depth);
            using var vertex=device.CreateVertexShader(shaders.Vertex.Span);
            using var pixel=device.CreatePixelShader(shaders.Pixel.Span);
            using var parameters=device.CreateBuffer(new BufferDescription{ByteWidth=16,BindFlags=BindFlags.ConstantBuffer,Usage=ResourceUsage.Default});
            using var raster=device.CreateRasterizerState(new RasterizerDescription{FillMode=FillMode.Solid,CullMode=CullMode.None,DepthClipEnable=true});
            context.OMSetRenderTargets(target,depthView);context.RSSetViewport(new Viewport(1280,720));context.RSSetState(raster);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);context.VSSetShader(vertex);context.PSSetShader(pixel);
            context.VSSetConstantBuffer(0,parameters);context.PSSetConstantBuffer(0,parameters);
            var clock=Stopwatch.StartNew();long frames=0;var lastReport=TimeSpan.Zero;var samples=new List<double>();var previous=0d;
            // A one-second warm-up excludes shader creation and the first-frame queue.
            while(clock.Elapsed.TotalSeconds<Seconds+1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var t=clock.Elapsed.TotalSeconds;var data=new Vector4((float)t,1280f/720,0,0);context.UpdateSubresource(data,parameters);
                context.ClearRenderTargetView(target,new Color4(.026f,.018f,.04f,1));context.ClearDepthStencilView(depthView,DepthStencilClearFlags.Depth,1,0);
                context.DrawInstanced(36,1024,0,0);swap.Present(0,PresentFlags.None).CheckError();
                var now=clock.Elapsed.TotalSeconds;
                if(t>=1){frames++;if(previous>=1)samples.Add((now-previous)*1000);}previous=now;
                if(clock.Elapsed-lastReport>TimeSpan.FromMilliseconds(150)){progress?.Report(Math.Clamp((now-1)/Seconds,0,1));lastReport=clock.Elapsed;}
            }
            context.Flush();clock.Stop();
            var duration=clock.Elapsed.TotalSeconds-1;
            var sorted=samples.OrderBy(v=>v).ToArray();var p99=sorted.Length>0?sorted[(int)((sorted.Length-1)*.99)]:0;
            return new("GPU 3D",frames/duration,"FPS",duration,DateTimeOffset.UtcNow,$"Scene 1 · Direct3D 11 · 1280 × 720 · 1,024 lit cubes · 99th frame {p99:0.0} ms · {adapter.Description.Description.TrimEnd('\0')}");
        }
    }

    private const string Shader="""
        cbuffer Parameters : register(b0) { float4 settings; };
        struct Vertex { float4 position : SV_POSITION; float3 normal : NORMAL; float3 world : TEXCOORD0; float3 tint : COLOR0; };
        static const float3 corners[8] = {
          float3(-1,-1,-1),float3(1,-1,-1),float3(1,1,-1),float3(-1,1,-1),
          float3(-1,-1,1),float3(1,-1,1),float3(1,1,1),float3(-1,1,1) };
        static const uint indices[36] = {0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,3,7,6,3,6,2,0,1,5,0,5,4};
        static const float3 normals[6] = {float3(0,0,-1),float3(0,0,1),float3(-1,0,0),float3(1,0,0),float3(0,1,0),float3(0,-1,0)};
        float3 turn(float3 p,float angle) {
          float s=sin(angle),c=cos(angle);p=float3(p.x*c+p.z*s,p.y,-p.x*s+p.z*c);
          s=sin(angle*.7);c=cos(angle*.7);return float3(p.x,p.y*c-p.z*s,p.y*s+p.z*c);
        }
        Vertex VSMain(uint vid:SV_VertexID,uint instance:SV_InstanceID) {
          Vertex output;float time=settings.x;float a=time*.35+instance*.17;
          float3 p=turn(corners[indices[vid]]*.36,a);
          float3 n=turn(normals[vid/6],a);
          p+=float3((float(instance%32)-15.5)*.94,(float(instance/32)-15.5)*.94,25+sin(instance*.12+time)*3);
          p.xy+=float2(sin(time*.31),cos(time*.27))*1.6;
          output.world=p;output.normal=n;output.tint=.5+.5*cos(float3(0,2,4)+instance*.13);
          output.position=float4(p.x*1.45/settings.y,p.y*1.45,p.z*100.0/99.9-.1*100.0/99.9,p.z);
          return output;
        }
        float4 PSMain(Vertex input):SV_TARGET {
          float3 n=normalize(input.normal);float3 p=input.world;
          float light=.16+max(0,dot(n,normalize(float3(-.4,.7,-.6))))*.8;
          float3 view=normalize(-p);float spec=pow(max(0,dot(reflect(normalize(float3(.4,-.7,.6)),n),view)),32);
          float3 q=p*.5;float field=0;
          [loop] for(uint i=0;i<24;i++){q=sin(q.yzx*1.41+float3(.2,.6,.9)+settings.x*.06);field+=dot(q,q)/(24+i);}
          float3 color=lerp(float3(.24,.1,.43),float3(.55,.38,.78),input.tint.x)*light;
          color+=input.tint*.15*field+spec*.45;
          return float4(color,1);
        }
        """;
}
