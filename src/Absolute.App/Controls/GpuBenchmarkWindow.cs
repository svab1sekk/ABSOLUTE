using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;
using Absolute.Core;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using static Absolute.App.Controls.Ui;

namespace Absolute.App.Controls;

internal static class GpuBenchmarkWindow
{
    public static async Task<ComponentBenchmarkResult> RunAsync(Window owner,IProgress<double>? progress,CancellationToken cancellationToken,bool threeD=false)
    {
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var completion=new TaskCompletionSource<ComponentBenchmarkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host=new SurfaceHost();var finished=false;
        var window=new Window{Title=threeD?"ABSOLUTE · GPU 3D":"ABSOLUTE · GPU 2D",Owner=owner,Width=1000,Height=620,MinWidth=800,MinHeight=500,WindowStartupLocation=WindowStartupLocation.CenterOwner,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brush("#0B090F"),FontFamily=owner.FontFamily};
        WindowChrome.SetWindowChrome(window,new WindowChrome{CaptionHeight=38,GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
        var title=Text(threeD?"GPU 3D · Scene 1 · 1280 × 720":"GPU 2D · Direct2D",13,"#C3B1D6");title.Margin=new Thickness(16,0,0,0);
        var close=new Button{Content="×",Style=(Style)Application.Current.FindResource("Caption")};
        WindowChrome.SetIsHitTestVisibleInChrome(close,true);close.Click+=(_,_)=>cancellation.Cancel();
        var header=Columns((title,1),(close,-1));
        var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(38)});layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(header);Grid.SetRow(host,1);layout.Children.Add(host);window.Content=layout;
        window.Closing+=(_,args)=>{if(!finished){args.Cancel=true;cancellation.Cancel();}};
        window.Loaded+=async(_,_)=>
        {
            try
            {
                var pixels=host.Pixels();var width=Math.Max(1,pixels.Right-pixels.Left);var height=Math.Max(1,pixels.Bottom-pixels.Top);
                var handle=host.Handle;var seconds=threeD?Gpu3DWorkload.Seconds:4.5;
                var visibleProgress=new Progress<double>(fraction=>
                {
                    progress?.Report(fraction);
                    title.Text=threeD&&fraction<=0?"GPU 3D · Warming up…":$"GPU {(threeD?"3D · Scene 1":"2D · Direct2D")} · {fraction:P0} · {fraction*seconds:0.0} / {seconds:0.#} s";
                });
                var result=await Task.Run(()=>threeD?Gpu3DWorkload.Measure(handle,visibleProgress,cancellation.Token):Measure(handle,width,height,visibleProgress,cancellation.Token),cancellation.Token);
                completion.TrySetResult(result);
            }
            catch(OperationCanceledException){completion.TrySetCanceled(cancellation.Token);}
            catch(Exception error){completion.TrySetException(error);}
            finally{finished=true;window.Close();}
        };
        window.Show();
        return await completion.Task;
    }

    private static ComponentBenchmarkResult Measure(IntPtr window,int width,int height,IProgress<double>? progress,CancellationToken cancellationToken)
    {
        using var graphicsFactory=Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
        using var hardwareAdapter=Services.GraphicsDiscovery.PreferredAdapter(graphicsFactory);
        using var factory=D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.SingleThreaded,DebugLevel.None);
        ID2D1HwndRenderTarget target;
        try
        {
            target=factory.CreateHwndRenderTarget(new RenderTargetProperties(RenderTargetType.Hardware,new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm,Vortice.DCommon.AlphaMode.Ignore),96,96,RenderTargetUsage.None,FeatureLevel.Default),
                new HwndRenderTargetProperties{Hwnd=window,PixelSize=new SizeI(width,height),PresentOptions=PresentOptions.Immediately});
        }
        catch(Exception error){throw new NotSupportedException("Hardware Direct2D rendering is unavailable. Check the GPU driver or remote-desktop graphics support.",error);}
        using(target)
        using(var violet=target.CreateSolidColorBrush(new Color4(.62f,.37f,.86f,.55f)))
        using(var cyan=target.CreateSolidColorBrush(new Color4(.2f,.72f,.8f,.45f)))
        {
            const int sprites=1600;var clock=Stopwatch.StartNew();long frames=0;var lastProgress=TimeSpan.Zero;
            while(clock.Elapsed.TotalSeconds<4.5)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var t=(float)clock.Elapsed.TotalSeconds;
                target.BeginDraw();target.Clear(new Color4(.035f,.025f,.05f,1));
                for(var i=0;i<sprites;i++)
                {
                    var x=(float)((Math.Sin(i*1.317+t*(.3+i%7*.04))*.47+.5)*width);
                    var y=(float)((Math.Cos(i*.713+t*(.24+i%5*.06))*.45+.5)*height);
                    target.FillEllipse(new Ellipse(new Vector2(x,y),9+i%19,9+i%19),i%3==0?cyan:violet);
                }
                target.EndDraw(out _,out _).CheckError();frames++;
                if(clock.Elapsed-lastProgress>TimeSpan.FromMilliseconds(100)){progress?.Report(Math.Min(1,clock.Elapsed.TotalSeconds/4.5));lastProgress=clock.Elapsed;}
            }
            clock.Stop();
            return new("GPU 2D",frames/clock.Elapsed.TotalSeconds,"FPS",clock.Elapsed.TotalSeconds,DateTimeOffset.UtcNow,$"Direct2D hardware · {sprites} translucent sprites · {width} × {height}");
        }
    }

    private sealed class SurfaceHost:HwndHost
    {
        public Rectangle Pixels(){GetClientRect(Handle,out var rectangle);return rectangle;}
        protected override HandleRef BuildWindowCore(HandleRef parent)=>new(this,CreateWindowEx(0,"STATIC","",0x50000004,0,0,1000,580,parent.Handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero));
        protected override void DestroyWindowCore(HandleRef window)=>DestroyWindow(window.Handle);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateWindowEx(uint extended,string className,string name,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
        [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DestroyWindow(IntPtr window);
        [StructLayout(LayoutKind.Sequential)]public struct Rectangle{public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetClientRect(IntPtr window,out Rectangle rectangle);
    }
}
