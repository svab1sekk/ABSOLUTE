using System.Diagnostics;

namespace Absolute.Core;

/// <summary>A single-threaded managed memory copy test; results compare runs of this test only.</summary>
public static class MemoryBenchmark
{
    public static Task<BenchmarkResult> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(() =>
    {
        const int size = 32 * 1024 * 1024;
        var source = new byte[size];
        var destination = new byte[size];
        Random.Shared.NextBytes(source);
        source.CopyTo(destination, 0); // Warm up before timing.
        var clock = Stopwatch.StartNew();
        long copies = 0;
        var lastReport = TimeSpan.Zero;
        while (clock.Elapsed.TotalSeconds < 4)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Buffer.BlockCopy(source, 0, destination, 0, size);
            copies++;
            if (clock.Elapsed - lastReport >= TimeSpan.FromMilliseconds(80))
            {
                progress?.Report(Math.Min(1, clock.Elapsed.TotalSeconds / 4));
                lastReport = clock.Elapsed;
            }
        }
        clock.Stop();
        GC.KeepAlive(destination);
        return new BenchmarkResult(DateTimeOffset.UtcNow, copies * (double)size / 1024 / 1024 / clock.Elapsed.TotalSeconds,
            clock.Elapsed.TotalSeconds, Environment.MachineName);
    }, cancellationToken);
}
