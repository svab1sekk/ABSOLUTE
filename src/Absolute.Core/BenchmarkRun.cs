namespace Absolute.Core;

public sealed record BenchmarkRun(string Id, DateTimeOffset RecordedAt, string Machine, int SuiteVersion,
    IReadOnlyList<ComponentBenchmarkResult> Results, IReadOnlyList<string> Notices, bool Cancelled = false)
{
    public bool Complete => !Cancelled && new[] { "CPU", "GPU 3D", "RAM" }.All(component => Results.Any(r => r.Component == component && r.Score > 0 && double.IsFinite(r.Score)));
    public double? Score => BenchmarkScoring.Combined(Results);
}

public static class BenchmarkScoring
{
    public const int SuiteVersion = 1;
    // Explicit workload targets, not hardware-population percentiles.
    public static double? Target(string component) => component switch { "CPU" => 1024, "RAM" => 8192, "GPU 3D" => 60, "GPU 2D" => 60, _ => null };
    public static double? Points(ComponentBenchmarkResult result) => Target(result.Component) is double target && double.IsFinite(result.Score) && result.Score > 0 ? result.Score / target * 100 : null;
    public static string Rating(double? points) => points switch { null => "Unrated", < 40 => "Low", < 75 => "Below target", < 115 => "Normal", < 165 => "Good", _ => "Excellent" };
    public static double? Combined(IEnumerable<ComponentBenchmarkResult> source)
    {
        var results = source.GroupBy(r => r.Component).Select(g => g.First()).ToArray();
        // The 3D workload represents GPU performance when both GPU tests are selected.
        var scores = results.Where(r => r.Component != "GPU 2D" || !results.Any(c => c.Component == "GPU 3D")).Select(Points).Where(p => p is > 0).Select(p => p!.Value).ToArray();
        return scores.Length == 0 ? null : Math.Exp(scores.Average(p => Math.Log(p)));
    }
}
