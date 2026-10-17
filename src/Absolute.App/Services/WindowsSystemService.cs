using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Absolute.Core;

namespace Absolute.App.Services;

internal sealed class WindowsSystemService
{
    private ulong previousIdle, previousKernel, previousUser;

    public SystemSnapshot Sample()
    {
        double cpu = 0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel + user - previousKernel - previousUser;
            if (previousKernel != 0 && total > 0) cpu = 100d * (total - Math.Min(total, idle - previousIdle)) / total;
            (previousIdle, previousKernel, previousUser) = (idle, kernel, user);
        }
        var memory = ReadMemory();
        return new(Math.Clamp(cpu, 0, 100), memory.TotalPhysical - memory.AvailablePhysical, memory.TotalPhysical,
            TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    private static MemoryStatus ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new System.ComponentModel.Win32Exception();
        return status;
    }
    public static ulong UsableMemory()=>ReadMemory().TotalPhysical;

    public HardwareInfo Hardware()
    {
        using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        using var os = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = Convert.ToInt32(os?.GetValue("CurrentBuildNumber", "0"));
        var version = build >= 22000 ? "Windows 11" : "Windows 10";
        var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        var graphics = EnumDisplayDevices(null, 0, ref display, 0) ? display.DeviceString : "Unavailable";
        var disk = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        return new(HardwareNames.Clean(cpu?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unavailable"), HardwareNames.Clean(graphics),
            $"{version} {os?.GetValue("DisplayVersion")}", Environment.MachineName,
            Environment.ProcessorCount, ReadMemory().TotalPhysical, disk.TotalSize, disk.AvailableFreeSpace);
    }

    public static NetworkSample? Network()
    {
        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any)))
            .FirstOrDefault();
        if (adapter is null) return null;
        var info = adapter.GetIPProperties();
        var stats = adapter.GetIPv4Statistics();
        return new(adapter.Name,
            info.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "Unavailable",
            string.Join(", ", info.DnsAddresses.Select(a => a.ToString())), stats.BytesReceived, stats.BytesSent, adapter.Speed);
    }

    public static async Task<IReadOnlyList<PowerPlan>> GetPowerPlansAsync() => PowerPlans.Parse(await RunAsync("powercfg.exe", "/list"));

    public static Task<string> SetPowerPlanAsync(Guid id) => RunAsync("powercfg.exe", "/setactive", id.ToString());
    public static Task<string> FlushDnsAsync() => RunAsync("ipconfig.exe", "/flushdns");

    private static async Task<string> RunAsync(string file, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, file))
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {file}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            throw new TimeoutException($"{file} did not finish in time.");
        }
        var result = await output;
        var failure = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(failure) ? result.Trim() : failure.Trim());
        return result;
    }

    public static void OpenSettings(string page)
    {
        string[] allowed = ["gaming-gamemode", "startupapps", "storagesense", "display", "network-status", "network-ethernet", "network-proxy", "privacy-backgroundapps", "windowsupdate", "notifications", "powersleep", "about"];
        if (!allowed.Contains(page)) throw new ArgumentException("Unsupported settings page.", nameof(page));
        Process.Start(new ProcessStartInfo("ms-settings:" + page) { UseShellExecute = true });
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice display, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
}
