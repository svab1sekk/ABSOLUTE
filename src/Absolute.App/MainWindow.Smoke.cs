using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Absolute.App;

public partial class MainWindow
{
    private async Task RunSmokeCheckAsync(string output, double firstFrameMilliseconds)
    {
        var success = false;
        var errors = new List<string>();
        var rendered = new List<string>();
        try
        {
            Directory.CreateDirectory(output);
            if (hardware is null || hardware.Memory == 0) errors.Add("Hardware discovery returned no physical memory.");
            if (snapshot is null || snapshot.MemoryTotal == 0) errors.Add("Live memory readings are unavailable.");
            if (!powerPlans.Any(p => p.Active)) errors.Add("No active Windows power plan was discovered.");
            await SampleAsync();
            if (snapshot is null || !double.IsFinite(snapshot.CpuPercent) || snapshot.CpuPercent is < 0 or > 100)
                errors.Add("CPU utilization is invalid.");
            monitor.Stop();
            Toast.Visibility = Visibility.Collapsed;
            foreach (var size in new[] { (Width: 1240d, Height: 820d, Name: "desktop"), (Width: 1020d, Height: 700d, Name: "compact") })
            {
                Width = size.Width; Height = size.Height;
                foreach (var page in new[] { "System", "Network", "Presets", "Playbooks", "Benchmark", "PC", "Settings" })
                {
                    if (page == "Settings") SettingsNav.IsChecked = true; else Select(page);
                    await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ApplicationIdle);
                    if (Page.Children.Count < 3) errors.Add($"{page} has no page content.");
                    var dpi = VisualTreeHelper.GetDpi(this);
                    var bitmap = new RenderTargetBitmap((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                    bitmap.Render(this);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var filename = $"{size.Name}-{page.ToLowerInvariant()}.png";
                    using var image = File.Create(Path.Combine(output, filename)); encoder.Save(image);
                    rendered.Add(filename);
                }
            }
            success = errors.Count == 0;
        }
        catch (Exception error) { errors.Add(error.ToString()); }
        finally
        {
            try
            {
                Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
                {
                    Success = success, ManagedFirstFrameMilliseconds = firstFrameMilliseconds,
                    OperatingSystem = hardware?.OperatingSystem, MemoryBytes = snapshot?.MemoryTotal,
                    CpuPercent = snapshot?.CpuPercent, PowerPlans = powerPlans.Count, Screenshots = rendered, Errors = errors
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally { Application.Current.Shutdown(success ? 0 : 1); }
        }
    }
}
