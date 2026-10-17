namespace Absolute.Core;

public sealed record PowerPlan(Guid Id, string Name, bool Active);
public sealed record BenchmarkResult(DateTimeOffset RecordedAt, double MegabytesPerSecond, double DurationSeconds, string Machine);
public sealed record NetworkSample(string Adapter, string Address, string Dns, long Received, long Sent, long LinkSpeed);
public sealed record SystemSnapshot(double CpuPercent, ulong MemoryUsed, ulong MemoryTotal, TimeSpan Uptime);
public sealed record HardwareInfo(string Processor, string Graphics, string OperatingSystem, string Machine, int LogicalProcessors, ulong Memory, long DiskTotal, long DiskFree);

public sealed class AppPreferences
{
    public bool LiveMonitoring { get; set; } = true;
    public Guid? PreviousPowerPlan { get; set; }
    public List<BenchmarkResult> Benchmarks { get; set; } = [];
    public bool AutoGamePresets { get; set; } = true;
    public bool AnimateLogo { get; set; } = true;
    public bool TemperatureMonitoring { get; set; } = true;
    public string? SelectedGame { get; set; }
    public List<GameDefinition> CustomGames { get; set; } = [];
    public List<ComponentBenchmarkResult> ComponentBenchmarks { get; set; } = [];
    public GameSessionJournal? GameSession { get; set; }
}

public static class PowerPlans
{
    public static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid Performance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Saver = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");

    public static IReadOnlyList<PowerPlan> Parse(string output)
    {
        var result = new List<PowerPlan>();
        foreach (var line in output.Split('\n'))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, @"(?<id>[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\s+\((?<name>.+)\)");
            if (match.Success && Guid.TryParse(match.Groups["id"].Value, out var id))
                result.Add(new(id, match.Groups["name"].Value, line.TrimEnd().EndsWith('*')));
        }
        return result;
    }
}
