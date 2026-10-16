using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Absolute.App.Controls;
using Absolute.App.Services;
using Absolute.Core;
using static Absolute.App.Controls.Ui;

namespace Absolute.App;

public partial class MainWindow : Window
{
    private readonly WindowsSystemService system = new();
    private readonly PreferenceStore store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Absolute", "preferences.json"));
    private readonly AppPreferences preferences;
    private readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private IReadOnlyList<PowerPlan> powerPlans = [];
    private HardwareInfo? hardware;
    private SystemSnapshot? snapshot;
    private NetworkSample? network;
    private DateTimeOffset networkTime;
    private CancellationTokenSource? benchmarkCancellation;
    private bool sampling, closed, initialized;
    private string selectedPage = "System";
    private TextBlock? cpuValue, memoryValue, memoryDetail, powerValue, uptimeValue, downloadValue, uploadValue;
    private ProgressBar? memoryBar;
    private Sparkline? cpuChart;

    public MainWindow()
    {
        InitializeComponent();
        preferences = store.Load();
        foreach (var name in new[] { "System", "Network", "Presets", "Playbooks", "Benchmark", "PC" })
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icon(name, 18); icon.Margin = new Thickness(0, 0, 14, 0);
            content.Children.Add(icon);
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            label.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(RadioButton), 1) });
            content.Children.Add(label);
            var navigation = new RadioButton { Content = content, GroupName = "Pages", Style = (Style)FindResource("Nav"), Tag = name };
            System.Windows.Automation.AutomationProperties.SetName(navigation, name);
            navigation.Checked += (_, _) => Navigate(name);
            Navigation.Children.Add(navigation);
        }
        SettingsNav.Content = Icon("Settings", 20);
        SettingsNav.Checked += (_, _) => Navigate("Settings");
        ((RadioButton)Navigation.Children[0]).IsChecked = true;
        toastTimer.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; toastTimer.Stop(); };
        monitor.Tick += async (_, _) => await SampleAsync();
        ContentRendered += async (_, _) =>
        {
            var firstFrame = App.StartupClock.Elapsed.TotalMilliseconds;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await InitializeAsync();
            var args = Environment.GetCommandLineArgs();
            var smokeIndex = Array.IndexOf(args, "--smoke-test");
            if (smokeIndex >= 0) await RunSmokeCheckAsync(smokeIndex + 1 < args.Length ? args[smokeIndex + 1] : "smoke-results", firstFrame);
        };
        Closed += (_, _) => { closed = true; monitor.Stop(); toastTimer.Stop(); benchmarkCancellation?.Cancel(); };
    }

    private async Task InitializeAsync()
    {
        await Safe(async () =>
        {
            hardware = await Task.Run(system.Hardware);
            if (closed) return;
            await SampleAsync();
            powerPlans = await WindowsSystemService.GetPowerPlansAsync();
            if (closed) return;
            Navigate(selectedPage);
        });
        initialized = true;
        BuildCurrentPage();
        if (!closed && preferences.LiveMonitoring) monitor.Start();
    }

    private async Task SampleAsync()
    {
        if (sampling || closed) return;
        sampling = true;
        try
        {
            var next = await Task.Run(system.Sample);
            var nextNetwork = await Task.Run(WindowsSystemService.Network);
            if (closed) return;
            var hadSample = snapshot is not null;
            snapshot = next;
            if (cpuValue is not null) cpuValue.Text = hadSample ? $"{next.CpuPercent:F0}%" : "—";
            if (hadSample) cpuChart?.Add(next.CpuPercent);
            if (memoryValue is not null) memoryValue.Text = $"{GiB(next.MemoryUsed):F1} GB";
            if (memoryDetail is not null) memoryDetail.Text = $"of {GiB(next.MemoryTotal):F1} GB";
            if (memoryBar is not null) memoryBar.Value = next.MemoryTotal > 0 ? next.MemoryUsed * 100d / next.MemoryTotal : 0;
            if (uptimeValue is not null) uptimeValue.Text = $"{(int)next.Uptime.TotalHours}h {next.Uptime.Minutes}m";
            var now = DateTimeOffset.UtcNow;
            if (network is not null && nextNetwork is not null && network.Adapter == nextNetwork.Adapter)
            {
                var seconds = Math.Max(0.1, (now - networkTime).TotalSeconds);
                if (downloadValue is not null) downloadValue.Text = Rate(Math.Max(0, nextNetwork.Received - network.Received) / seconds);
                if (uploadValue is not null) uploadValue.Text = Rate(Math.Max(0, nextNetwork.Sent - network.Sent) / seconds);
            }
            network = nextNetwork;
            networkTime = now;
            StatusText.Text = preferences.LiveMonitoring ? "Live monitoring  ·  Local session" : "Monitoring paused  ·  Local session";
        }
        catch (Exception error) { StatusText.Text = "Monitoring unavailable"; Notify(error.Message); }
        finally { sampling = false; }
    }

    private void Navigate(string name)
    {
        selectedPage = name;
        cpuValue = memoryValue = memoryDetail = powerValue = uptimeValue = downloadValue = uploadValue = null;
        cpuChart = null; memoryBar = null;
        Page.Children.Clear(); PageScroll.ScrollToTop();
        var eyebrow = Text("W O R K S P A C E   /   " + name.ToUpperInvariant(), 10, "#85728F", FontWeights.SemiBold);
        eyebrow.Margin = new Thickness(0, 0, 0, 9); Page.Children.Add(eyebrow);
        var title = Text(name, 34, weight: FontWeights.SemiBold);
        var header = Columns((title, 1), (Pill(name == "System" ? "●  SYSTEM OVERVIEW" : "ABSOLUTE"), -1));
        header.Margin = new Thickness(0, 0, 0, 27); Page.Children.Add(header);
        switch (name)
        {
            case "System": BuildSystem(); break;
            case "Network": BuildNetwork(); break;
            case "Presets": BuildPresets(); break;
            case "Playbooks": BuildPlaybooks(); break;
            case "Benchmark": BuildBenchmark(); break;
            case "PC": BuildPc(); break;
            case "Settings": BuildSettings(); break;
        }
    }

    private void BuildSystem()
    {
        cpuValue = Text("—", 42, weight: FontWeights.SemiBold);
        cpuChart = new Sparkline { Height = 86, Margin = new Thickness(0, 20, 0, 0) };
        var processor = Stack(Text("PROCESSOR", 10, "#A396AF", FontWeights.SemiBold), cpuValue, Text("CPU utilization", 12, "#A396AF"), cpuChart);
        processor.Margin = new Thickness(0, 0, 30, 0);
        memoryValue = Text(snapshot is null ? "—" : $"{GiB(snapshot.MemoryUsed):F1} GB", 32, weight: FontWeights.SemiBold);
        memoryDetail = Text(snapshot is null ? "Reading memory…" : $"of {GiB(snapshot.MemoryTotal):F1} GB", 12, "#A396AF");
        memoryBar = new ProgressBar { Maximum = 100, Value = snapshot is { MemoryTotal: > 0 } s ? s.MemoryUsed * 100d / s.MemoryTotal : 0, Margin = new Thickness(0, 18, 0, 0) };
        var memory = Stack(Text("MEMORY", 10, "#A396AF", FontWeights.SemiBold), memoryValue, memoryDetail, memoryBar);
        memory.Margin = new Thickness(28, 0, 28, 0);
        powerValue = Text(ActivePlanName, 21, weight: FontWeights.SemiBold); powerValue.Margin = new Thickness(0, 10, 0, 8);
        uptimeValue = Text(snapshot is null ? "—" : $"{(int)snapshot.Uptime.TotalHours}h {snapshot.Uptime.Minutes}m", 14, "#BCAFCA");
        var power = Stack(Text("POWER PLAN", 10, "#A396AF", FontWeights.SemiBold), powerValue, Text("UPTIME", 10, "#A396AF"), uptimeValue);
        power.Margin = new Thickness(28, 0, 0, 0);
        var overview = Card(Columns((processor, 1.2), (Divider(memory), 1), (Divider(power), 1)), new Thickness(26));
        overview.Background = new LinearGradientBrush(Color.FromRgb(27, 19, 36), Color.FromRgb(19, 15, 25), 20);
        Page.Children.Add(overview);
        Section("System controls", "Changes stay in your control");
        var rows = Stack(
            Row("Presets", "Power plan", ActivePlanName, B("Presets", () => Select("Presets"))),
            Row("Benchmark", "Game Mode", "Manage Windows gaming preferences", SettingsButton("gaming-gamemode")),
            Row("System", "Startup apps", "Review what launches with Windows", SettingsButton("startupapps")),
            Row("PC", "Storage cleanup", "Review temporary files in Windows", SettingsButton("storagesense"), true));
        Page.Children.Add(Card(rows, new Thickness(23, 2, 23, 2)));
        var footer = Columns((Text("Quick access", 12, "#8C809B"), 1), (B("View PC details  ↗", () => Select("PC")), -1));
        Page.Children.Add(footer);
    }

    private void BuildNetwork()
    {
        downloadValue = Text("—", 30, weight: FontWeights.SemiBold);
        uploadValue = Text("—", 30, weight: FontWeights.SemiBold);
        var connection = Stack(Text("ACTIVE ADAPTER", 10, "#A396AF"), Text(network?.Adapter ?? "No active connection", 21, weight: FontWeights.SemiBold), Text(network is null ? "" : $"{network.LinkSpeed / 1_000_000:N0} Mbps link", 12, "#A396AF"));
        Page.Children.Add(Card(Columns((connection, 1.3), (Stack(Text("DOWNLOAD", 10, "#A396AF"), downloadValue), 1), (Stack(Text("UPLOAD", 10, "#A396AF"), uploadValue), 1))));
        Section("Connection", "");
        Page.Children.Add(Card(Stack(Info("Local address", network?.Address ?? "Unavailable"), Info("DNS servers", network?.Dns ?? "Unavailable"))));
        Section("Network tools", "");
        Page.Children.Add(Card(Stack(
            Row("Network", "DNS cache", "Clear cached name lookups", B("Flush DNS", async () => { await WindowsSystemService.FlushDnsAsync(); Notify("DNS cache cleared."); })),
            Row("Network", "Connection settings", "Adapters, Wi-Fi, and Ethernet", SettingsButton("network-status")),
            Row("Presets", "Proxy settings", "Review your Windows proxy configuration", SettingsButton("network-proxy"), true)), new Thickness(23, 2, 23, 2)));
    }

    private void BuildPresets()
    {
        Page.Children.Add(Card(Columns((Stack(Text("Active power plan", 12, "#A396AF"), Text(ActivePlanName, 25, weight: FontWeights.SemiBold)), 1),
            (B("Restore previous", RestorePowerPlan), -1))));
        foreach (var preset in new[] { ("Balanced", "Windows balances power use and performance.", PowerPlans.Balanced, "EVERYDAY"),
            ("Performance", "Prefer performance while connected to power.", PowerPlans.Performance, "PERFORMANCE"),
            ("Power saver", "Reduce power use on battery.", PowerPlans.Saver, "EFFICIENCY") })
        {
            var plan = powerPlans.FirstOrDefault(p => p.Id == preset.Item3);
            var selected = plan?.Active == true;
            var button = B(selected ? "Active" : "Apply preset", () => ApplyPowerPlan(preset.Item3), !selected && plan is not null);
            button.IsEnabled = plan is not null && !selected;
            var copy = Stack(Pill(preset.Item4), Text(preset.Item1, 23, weight: FontWeights.SemiBold), Text(plan is null ? "Not offered by this device's power configuration." : preset.Item2, 13, "#A79AB6"));
            foreach (FrameworkElement child in copy.Children) child.Margin = new Thickness(0, 0, 0, 8);
            Page.Children.Add(Card(Columns((copy, 1), (button, -1))));
        }
        Page.Children.Add(Text("Presets change only the Windows power plan. Your previous plan can be restored.", 12, "#A396AF"));
    }

    private void BuildPlaybooks()
    {
        Playbook("Gaming", "Review the settings that matter before a session.",
            ("Game Mode", "gaming-gamemode"), ("Display & refresh rate", "display"), ("Background startup apps", "startupapps"));
        Playbook("Everyday", "Keep your desktop focused.",
            ("Notifications", "notifications"), ("Storage cleanup", "storagesense"), ("Power & sleep", "powersleep"));
        Playbook("Maintenance", "Keep Windows and your storage in check.",
            ("Windows Update", "windowsupdate"), ("Storage cleanup", "storagesense"), ("About this PC", "about"));
    }

    private void Playbook(string title, string detail, params (string Label, string Page)[] steps)
    {
        var panel = Stack(Text(title, 23, weight: FontWeights.SemiBold), Text(detail, 12, "#A396AF"));
        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            panel.Children.Add(Row("Playbooks", step.Label, "", SettingsButton(step.Page), i == steps.Length - 1));
        }
        Page.Children.Add(Card(panel, new Thickness(23, 21, 23, 4)));
    }

    private void BuildBenchmark()
    {
        var value = Text("—", 46, weight: FontWeights.SemiBold);
        var progress = new ProgressBar { Maximum = 1, Margin = new Thickness(0, 22, 0, 22) };
        var detail = Text("32 MB buffer  ·  Single thread  ·  About 4 seconds", 12, "#A396AF");
        var start = B("Run benchmark", async () =>
        {
            if (benchmarkCancellation is not null) { Notify("A benchmark is already running."); return; }
            using var cancellation = new CancellationTokenSource(); benchmarkCancellation = cancellation;
            try
            {
                detail.Text = "Testing memory copy throughput…";
                var result = await MemoryBenchmark.RunAsync(new Progress<double>(p => progress.Value = p), cancellation.Token);
                preferences.Benchmarks.Insert(0, result);
                preferences.Benchmarks = preferences.Benchmarks.Take(20).ToList();
                store.Save(preferences);
                if (!closed) Notify($"Benchmark complete · {result.MegabytesPerSecond:N0} MB/s");
            }
            catch (OperationCanceledException) { if (!closed) Notify("Benchmark cancelled."); }
            finally { benchmarkCancellation = null; if (!closed && selectedPage == "Benchmark") BuildCurrentPage(); }
        }, true);
        start.IsEnabled = benchmarkCancellation is null;
        if (benchmarkCancellation is not null) detail.Text = "Benchmark running…";
        var cancel = B("Cancel", () => benchmarkCancellation?.Cancel());
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        start.Margin = new Thickness(0, 0, 10, 0); buttons.Children.Add(start); buttons.Children.Add(cancel);
        var latest = preferences.Benchmarks.FirstOrDefault();
        if (latest is not null) value.Text = $"{latest.MegabytesPerSecond:N0}";
        Page.Children.Add(Card(Stack(Pill("MEMORY COPY"), value, Text("MB/s", 13, "#BBA0D8"), progress, detail, Space(22), buttons)));
        Page.Children.Add(Text("A local memory copy test. Compare repeat runs on this PC; this is not an FPS or overall PC score.", 12, "#A396AF"));
        Section("Recent runs", "");
        if (preferences.Benchmarks.Count == 0) Page.Children.Add(Card(Text("No results yet", 13, "#A396AF")));
        else
        {
            var history = new StackPanel();
            foreach (var result in preferences.Benchmarks.Take(5))
                history.Children.Add(Info(result.RecordedAt.ToLocalTime().ToString("d MMM · HH:mm", CultureInfo.CurrentCulture), $"{result.MegabytesPerSecond:N0} MB/s"));
            Page.Children.Add(Card(history));
        }
    }

    private void BuildPc()
    {
        if (hardware is null) { Page.Children.Add(Card(Text("Reading hardware…", 16, "#A396AF"))); return; }
        Page.Children.Add(Card(Columns((Stack(Pill("THIS DEVICE"), Space(12), Text(hardware.Machine, 28, weight: FontWeights.SemiBold), Text(hardware.OperatingSystem, 13, "#A396AF")), 1), (Icon("PC", 62, "#8B65AF"), -1))));
        Section("Hardware", "");
        Page.Children.Add(Card(Stack(Info("Processor", hardware.Processor), Info("Logical processors", hardware.LogicalProcessors.ToString()), Info("Graphics", hardware.Graphics), Info("Memory", $"{GiB(hardware.Memory):F1} GB"))));
        Section("System drive", "");
        var used = hardware.DiskTotal - hardware.DiskFree;
        Page.Children.Add(Card(Stack(Info("Storage", $"{GiB((ulong)hardware.DiskFree):N0} GB free of {GiB((ulong)hardware.DiskTotal):N0} GB"),
            new ProgressBar { Maximum = hardware.DiskTotal, Value = used, Margin = new Thickness(0, 14, 0, 0) })));
    }

    private void BuildSettings()
    {
        var monitoring = new CheckBox { IsChecked = preferences.LiveMonitoring, Foreground = Brush("#CDB5E5"), VerticalAlignment = VerticalAlignment.Center, Content = "Enabled", Padding = new Thickness(6, 0, 0, 0) };
        monitoring.Click += async (_, _) => await Safe(async () =>
        {
            preferences.LiveMonitoring = monitoring.IsChecked == true;
            store.Save(preferences);
            if (preferences.LiveMonitoring) { await SampleAsync(); monitor.Start(); } else monitor.Stop();
            monitoring.Content = preferences.LiveMonitoring ? "Enabled" : "Paused";
            StatusText.Text = preferences.LiveMonitoring ? "Live monitoring  ·  Local session" : "Monitoring paused  ·  Local session";
        });
        monitoring.Content = preferences.LiveMonitoring ? "Enabled" : "Paused";
        Page.Children.Add(Card(Stack(
            Row("Benchmark", "Live monitoring", "Refresh CPU, memory, and network every 2 seconds", monitoring),
            Row("Presets", "Restore power plan", "Return to the plan saved before your first preset", B("Restore", RestorePowerPlan)),
            Row("PC", "Benchmark history", "Remove saved benchmark results from this PC", B("Clear history", () =>
            {
                if (Confirm("Clear benchmark history?", "This removes your saved benchmark results.")) { preferences.Benchmarks.Clear(); store.Save(preferences); Notify("Benchmark history cleared."); }
            }), true)), new Thickness(23, 2, 23, 2)));
        Page.Children.Add(Card(Stack(Text("ABSOLUTE", 22, weight: FontWeights.SemiBold), Text("Version 0.1.0", 12, "#A396AF"), Space(14), Text("Windows 10 / Windows 11", 13, "#BCAFCA"), Text("Preferences and results stay on this PC.", 12, "#A396AF"))));
    }

    private async Task ApplyPowerPlan(Guid id)
    {
        powerPlans = await WindowsSystemService.GetPowerPlansAsync();
        var target = powerPlans.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("This power plan is not available on your device.");
        var current = powerPlans.FirstOrDefault(p => p.Active);
        if (target.Active) return;
        if (current is null) throw new InvalidOperationException("Cannot identify the current power plan. No changes were made.");
        if (!Confirm($"Apply {target.Name}?", $"This changes the Windows power plan from {current.Name}. You can restore it in Presets.")) return;
        preferences.PreviousPowerPlan ??= current.Id;
        store.Save(preferences);
        await WindowsSystemService.SetPowerPlanAsync(id);
        powerPlans = await WindowsSystemService.GetPowerPlansAsync();
        if (!powerPlans.Any(p => p.Id == id && p.Active)) throw new InvalidOperationException("Windows did not activate the requested plan.");
        BuildCurrentPage(); Notify($"{target.Name} applied.");
    }

    private async Task RestorePowerPlan()
    {
        if (preferences.PreviousPowerPlan is not Guid previous) { Notify("No previous power plan has been saved yet."); return; }
        await WindowsSystemService.SetPowerPlanAsync(previous);
        powerPlans = await WindowsSystemService.GetPowerPlansAsync();
        if (!powerPlans.Any(p => p.Id == previous && p.Active)) throw new InvalidOperationException("Windows did not restore the previous plan.");
        preferences.PreviousPowerPlan = null; store.Save(preferences);
        BuildCurrentPage(); Notify("Previous power plan restored.");
    }

    private string ActivePlanName => powerPlans.FirstOrDefault(p => p.Active)?.Name ?? (initialized ? "Unavailable" : "Reading…");
    private static double GiB(ulong bytes) => bytes / 1073741824d;
    private static string Rate(double bytes) => bytes >= 1048576 ? $"{bytes / 1048576:F1} MB/s" : $"{bytes / 1024:F1} KB/s";
    private void BuildCurrentPage() { if (!closed) Navigate(selectedPage); }
    private void Select(string name)
    {
        foreach (RadioButton item in Navigation.Children) if ((string)item.Tag == name) item.IsChecked = true;
    }
    private Button SettingsButton(string page) => B("Open  ↗", () => WindowsSystemService.OpenSettings(page));
    private Button B(string label, Action action, bool primary = false) => B(label, () => { action(); return Task.CompletedTask; }, primary);
    private Button B(string label, Func<Task> action, bool primary = false) => Ui.Button(label, () => Safe(action), primary);
    private async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { if (!closed) Notify(error.Message); }
    }
    private void Notify(string message) { ToastText.Text = message; Toast.Visibility = Visibility.Visible; toastTimer.Stop(); toastTimer.Start(); }
    private bool Confirm(string title, string message) => ConfirmDialog.Ask(this, title, message);
    private static FrameworkElement Space(double height) => new Border { Height = height };
    private void Section(string title, string detail)
    {
        var label = Text(title, 17, weight: FontWeights.SemiBold);
        var section = Columns((label, 1), (Text(detail, 11, "#8F829C"), -1));
        section.Margin = new Thickness(0, 10, 0, 14); Page.Children.Add(section);
    }
    private static Border Divider(UIElement content) => new() { Child = content, BorderBrush = Brush("#34263E"), BorderThickness = new Thickness(1, 0, 0, 0) };
    private static Grid Info(string label, string value)
    {
        var result = Columns((Text(label, 13, "#A396AF"), 0.65), (Text(value, 13), 1));
        result.Margin = new Thickness(0, 10, 0, 10); return result;
    }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
