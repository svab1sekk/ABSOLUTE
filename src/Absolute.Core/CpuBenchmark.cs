using System.Diagnostics;
using System.Security.Cryptography;

namespace Absolute.Core;

public static class CpuBenchmark
{
    public static Task<ComponentBenchmarkResult> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        long bytes = 0;
        var threads = Math.Max(1, Environment.ProcessorCount);
        Parallel.For(0, threads, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = threads }, worker =>
        {
            var source = new byte[1024 * 1024]; Random.Shared.NextBytes(source);
            Span<byte> hash = stackalloc byte[32];
            var reported = TimeSpan.Zero;
            while (clock.Elapsed.TotalSeconds < 4)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SHA256.HashData(source, hash);
                Interlocked.Add(ref bytes, source.Length);
                if (worker == 0 && clock.Elapsed - reported > TimeSpan.FromMilliseconds(100))
                { progress?.Report(Math.Min(1, clock.Elapsed.TotalSeconds / 4)); reported = clock.Elapsed; }
            }
        });
        clock.Stop();
        return new ComponentBenchmarkResult("CPU", bytes / 1048576d / clock.Elapsed.TotalSeconds, "MB/s", clock.Elapsed.TotalSeconds, DateTimeOffset.UtcNow, $"SHA-256 · {threads} workers");
    }, cancellationToken);
}
