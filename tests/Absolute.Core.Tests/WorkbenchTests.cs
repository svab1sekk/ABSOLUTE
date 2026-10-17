using Absolute.Core;
using Xunit;

namespace Absolute.Core.Tests;

public sealed class WorkbenchTests
{
    private sealed class Backend:ISettingsBackend
    {
        public readonly Dictionary<SettingAddress,SettingValue> Values=[];
        public SettingAddress? FailOn;
        public SettingValue? Read(SettingAddress address)=>Values.GetValueOrDefault(address);
        public void Write(SettingAddress address,SettingValue? value)
        {
            if(address==FailOn&&value?.Data=="1")throw new IOException("Simulated settings write failure");
            if(value is null)Values.Remove(address);else Values[address]=value;
        }
    }
    [Fact]
    public void PlaybookRestoresExistingValuesAndRemovesOnlyValuesItCreated()
    {
        var backend=new Backend();var original=new SettingAddress("UserSettings","Existing");var added=new SettingAddress("UserSettings","New");
        backend.Values[original]=new("DWord","7");
        var journal=SettingsTransaction.Capture(backend,[new(original,new("DWord","1")),new(added,new("DWord","1"))]);
        SettingsTransaction.Apply(backend,journal);SettingsTransaction.Restore(backend,journal);
        Assert.Equal(new SettingValue("DWord","7"),backend.Read(original));Assert.Null(backend.Read(added));
    }
    [Fact]
    public void ConflictingExternalChangesPreventTheEntireRestore()
    {
        var backend=new Backend();var first=new SettingAddress("Settings","First");var second=new SettingAddress("Settings","Second");
        var journal=SettingsTransaction.Capture(backend,[new(first,new("DWord","1")),new(second,new("DWord","1"))]);
        SettingsTransaction.Apply(backend,journal);backend.Values[second]=new("DWord","99");
        Assert.Throws<InvalidOperationException>(()=>SettingsTransaction.Restore(backend,journal));
        Assert.Equal("1",backend.Read(first)?.Data);Assert.Equal("99",backend.Read(second)?.Data);
    }
    [Fact]
    public void APartialApplyFailureRollsBackEarlierWrites()
    {
        var backend=new Backend();var first=new SettingAddress("Settings","First");var second=new SettingAddress("Settings","Second");backend.FailOn=second;
        backend.Values[first]=new("DWord","8");
        var journal=SettingsTransaction.Capture(backend,[new(first,new("DWord","1")),new(second,new("DWord","1"))]);
        Assert.Throws<IOException>(()=>SettingsTransaction.Apply(backend,journal));Assert.Equal("8",backend.Read(first)?.Data);Assert.Null(backend.Read(second));
    }
    [Fact]
    public void ProcessorNamesKeepTheProductAndDropTrademarkNoise()=>Assert.Equal("Intel Core i7-13700K",HardwareNames.Clean("Intel(R)  Core(TM) i7-13700K"));
    [Fact]
    public async Task CpuBenchmarkMeasuresRealWork()
    {
        var result=await CpuBenchmark.RunAsync(null,CancellationToken.None);Assert.True(result.Score>0);Assert.Equal("MB/s",result.Unit);Assert.True(result.Seconds>=4);
    }
    [Fact]
    public async Task CpuBenchmarkHonorsCancellation()
    {
        using var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>CpuBenchmark.RunAsync(null,cancellation.Token));
    }
}
