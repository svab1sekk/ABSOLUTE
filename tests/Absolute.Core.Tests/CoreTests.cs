using Absolute.Core;
using Xunit;

namespace Absolute.Core.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "absolute-tests-" + Guid.NewGuid());

    [Fact]
    public void PowerPlanParserHandlesLocalizedLabelsAndActiveMarker()
    {
        const string output = "Existující schémata napájení\nGUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Rovnováha) *\nGUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)\n";
        var plans = PowerPlans.Parse(output);
        Assert.Equal(2, plans.Count);
        Assert.Equal(PowerPlans.Balanced, plans[0].Id);
        Assert.Equal("Rovnováha", plans[0].Name);
        Assert.True(plans[0].Active);
        Assert.False(plans[1].Active);
    }

    [Fact]
    public void ParserDoesNotInventPlansFromErrors()
    {
        Assert.Empty(PowerPlans.Parse("Access is denied."));
    }

    [Fact]
    public void PreferencesAndRestorationSurviveMultipleSaves()
    {
        var path = Path.Combine(directory, "nested", "preferences.json");
        var store = new PreferenceStore(path);
        var preferences = store.Load();
        Assert.True(preferences.LiveMonitoring);
        preferences.PreviousPowerPlan = PowerPlans.Saver;
        preferences.Benchmarks.Add(new(DateTimeOffset.UtcNow, 1000, 4, "TEST-PC"));
        store.Save(preferences);
        var restored = store.Load();
        Assert.Equal(PowerPlans.Saver, restored.PreviousPowerPlan);
        Assert.Single(restored.Benchmarks);
        restored.LiveMonitoring = false;
        store.Save(restored);
        Assert.False(store.Load().LiveMonitoring);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void InvalidPreferencesRecoverWithoutDeletingTheOriginal()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "preferences.json");
        File.WriteAllText(path, "not json");
        var preferences = new PreferenceStore(path).Load();
        Assert.True(preferences.LiveMonitoring);
        Assert.Empty(preferences.Benchmarks);
        Assert.Equal("not json", File.ReadAllText(path));
    }

    [Fact]
    public async Task CancelledBenchmarkDoesNotProduceAResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MemoryBenchmark.RunAsync(null, cancellation.Token));
    }

    [Fact]
    public async Task BenchmarkStopsWhenCancelledDuringMeasurement()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MemoryBenchmark.RunAsync(null, cancellation.Token));
    }

    [Fact]
    public async Task BenchmarkActuallyCopiesMemoryForTheMeasurementWindow()
    {
        var result = await MemoryBenchmark.RunAsync(null, CancellationToken.None);
        Assert.True(result.MegabytesPerSecond > 0);
        Assert.InRange(result.DurationSeconds, 4, 30);
        Assert.Equal(Environment.MachineName, result.Machine);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
