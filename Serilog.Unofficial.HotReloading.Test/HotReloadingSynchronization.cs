using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Serilog.Unofficial.HotReloading.Test;

public class HotReloadingSynchronization(ITestOutputHelper testOutputHelper)
{
    private const string ReloadRoundProp = "ReloadRound";

    class DisposableSink(Action<bool> incEmitCount) : ILogEventSink, IDisposable
    {
        private bool disposed = false;
        public void Dispose()
            => disposed = true;
        public void Emit(LogEvent logEvent)
        { 
            incEmitCount(disposed);
            if (disposed)
                throw new ObjectDisposedException(nameof(DisposableSink));
        }
    }

    class CountSink : ILogEventSink
    {
        Dictionary<int, int> counts = new();
        public void Emit(LogEvent logEvent)
        {
            var reloadRound = 0;
            if (logEvent.Properties.TryGetValue(ReloadRoundProp, out var v))
                if (v is ScalarValue sv)
                    reloadRound = Convert.ToInt32(sv.Value);
            lock (counts) 
            {
                counts.TryGetValue(reloadRound, out var c);
                counts[reloadRound] = c + 1;
            }            
        }
        public int GetCount(int reloadRound)
        {
            lock(counts)
            {
                if (counts.TryGetValue(reloadRound, out var v))
                    return v;
            }
            return 0;
        }
    }

    CountSink countSink = new();
    int emitOnDisposedSinkCount = 0;
    int emitOnNonDisposedSinkCount = 0;

    LoggerConfiguration ConfigureLogger(LoggerConfiguration lc, int reloadRound)
        => lc.Enrich.FromLogContext()            
            .Enrich.WithProperty(ReloadRoundProp, reloadRound)
            .WriteTo.Sink(countSink)
            .WriteTo.Sink(new DisposableSink((disposed) =>
            {
                if (disposed)
                    Interlocked.Increment(ref emitOnDisposedSinkCount);
                else
                    Interlocked.Increment(ref emitOnNonDisposedSinkCount);
            }));

    ILogger CreateOriginalReloadableLogger()
    {
        return ConfigureLogger(new LoggerConfiguration(), reloadRound:0)
            .CreateBootstrapLogger();
    }
    ILogger CreateUnsupportedReloadableLogger()
    {
        return ConfigureLogger(new LoggerConfiguration(), reloadRound: 0)
            .CreateReloadableLogger();
    }
    ILogger CreateSwitchableLogger()
    {
        return new SwitchableLogger(
            ConfigureLogger(new LoggerConfiguration(), reloadRound: 0)
            .CreateLogger());
    }

    void DoReload(ILogger l, int reloadRound)
    {
        if(l is Serilog.Extensions.Hosting.ReloadableLogger orl)
        {
            orl.Reload(lc => ConfigureLogger(lc, reloadRound));
        }
        else if(l is ReloadableLogger url)
        {
            url.Reload(lc => ConfigureLogger(lc, reloadRound));
        }
        else if (l is SwitchableLogger sl)
        {
            sl.Set(ConfigureLogger(new LoggerConfiguration(), reloadRound)
                .CreateLogger(), disposePrev: true);                
        }
    }

    async Task SynchronizationTest(ILogger l, bool expectFailure)
    {
        int reloadRound = 0;
        Assert.Equal(0, emitOnDisposedSinkCount);
        Assert.Equal(0, countSink.GetCount(0));
        Assert.Equal(0, countSink.GetCount(1));
        l.Information("x");
        Assert.Equal(0, emitOnDisposedSinkCount); 
        Assert.Equal(1, countSink.GetCount(0));
        Assert.Equal(0, countSink.GetCount(1));
        DoReload(l, ++reloadRound);
        Assert.Equal(0, emitOnDisposedSinkCount); 
        Assert.Equal(1, countSink.GetCount(0));
        Assert.Equal(0, countSink.GetCount(1));
        l.Information("x");
        Assert.Equal(0, emitOnDisposedSinkCount); 
        Assert.Equal(1, countSink.GetCount(0));
        Assert.Equal(1, countSink.GetCount(1));

        DoReload(l, ++reloadRound);

        const int totalTime = 1000;
        const int loggingTasksCount = 50;

        async Task loggingTask()
        {
            var t = Stopwatch.StartNew();
            await Task.Yield();
            while (t.ElapsedMilliseconds < totalTime)
            {
                for (int j = 0; j < 10; ++j)
                {
                    for (int i = 0; i < 100; ++i)
                    {
                        l.Information("x");
                    }
                    await Task.Yield();
                }
                await Task.Delay(5);
            }
        }
        async Task reloadingTask()
        {
            var t = Stopwatch.StartNew();
            await Task.Yield();
            while (t.ElapsedMilliseconds < totalTime)
            {
                await Task.Delay(10);
                DoReload(l, ++reloadRound);
            }
        }

        var loggingTasks = Enumerable.Range(0, loggingTasksCount)
            .Select(i => loggingTask()).ToList();

        await Task.WhenAll(loggingTasks.Append(reloadingTask()));

        //dispose last logger...
        DoReload(l, ++reloadRound);

        testOutputHelper.WriteLine($"ReloadRound: {reloadRound}");
        var counters = Enumerable.Range(0, reloadRound)
            .Select(countSink.GetCount)
            .ToArray();
        
        var totCount = counters.Sum();
        testOutputHelper.WriteLine($"TotCount: {totCount:n0}");
        testOutputHelper.WriteLine($"EmitOnNonDisposedSinkCount: {emitOnNonDisposedSinkCount:n0}");
        testOutputHelper.WriteLine($"EmitOnDisposedSinkCount: {emitOnDisposedSinkCount:n0}");

        if (expectFailure)
        {
            Assert.NotEqual(0, emitOnDisposedSinkCount);
            Assert.NotEqual(totCount, emitOnNonDisposedSinkCount);
        }
        else
        {
            Assert.Equal(0, emitOnDisposedSinkCount);
            Assert.Equal(totCount, emitOnNonDisposedSinkCount);
        }
    }

    //Make some rounds to better equilibrate the "benchmarks"
    public static IEnumerable<object[]> GetRounds() => Enumerable.Range(1, 5)
        .Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(GetRounds))]
    public Task OriginalReloadableLogger(int round)
        => SynchronizationTest(CreateOriginalReloadableLogger(), expectFailure: false);

    [Theory]
    [MemberData(nameof(GetRounds))]
    public Task UnsupportedReloadableLogger(int round)
        => SynchronizationTest(CreateUnsupportedReloadableLogger(), expectFailure: false);

    [Theory]
    [MemberData(nameof(GetRounds))]
    public Task SwitchableLogger(int round)
        => SynchronizationTest(CreateSwitchableLogger(), expectFailure: true);

}
