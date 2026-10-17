using System.Diagnostics;
using System.Security.Cryptography;

namespace Absolute.Core;

public static class CpuBenchmark
{
    public static async Task<ComponentBenchmarkResult> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clock=Stopwatch.StartNew();
        var threads=Math.Max(1,Environment.ProcessorCount);
        // Dedicated workers leave the application's thread pool available for timers,
        // cancellation, sensor I/O and other asynchronous continuations during a full load.
        var workers=Enumerable.Range(0,threads).Select(worker=>Task.Factory.StartNew(()=>
        {
            var source=new byte[1024*1024];Random.Shared.NextBytes(source);
            Span<byte> hash=stackalloc byte[32];long bytes=0;var reported=TimeSpan.Zero;
            while(clock.Elapsed.TotalSeconds<4)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SHA256.HashData(source,hash);bytes+=source.Length;
                if(worker==0&&clock.Elapsed-reported>TimeSpan.FromMilliseconds(100))
                {progress?.Report(Math.Min(1,clock.Elapsed.TotalSeconds/4));reported=clock.Elapsed;}
            }
            cancellationToken.ThrowIfCancellationRequested();return bytes;
        },cancellationToken,TaskCreationOptions.LongRunning,TaskScheduler.Default)).ToArray();
        var counts=await Task.WhenAll(workers);clock.Stop();
        cancellationToken.ThrowIfCancellationRequested();
        return new("CPU",counts.Sum()/1048576d/clock.Elapsed.TotalSeconds,"MB/s",clock.Elapsed.TotalSeconds,DateTimeOffset.UtcNow,$"SHA-256 · {threads} workers");
    }
}
