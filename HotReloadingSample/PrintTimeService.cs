using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HotReloadingSample;

public class PrintTimeService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;

    public PrintTimeService(ILogger<PrintTimeService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {        
        int TotCount = 0;
        const int taskCount = 5000;
        //const int taskCount = 500;
        //const int taskCount = 10;
        //const int taskCount = 1;
        const int taskSemiDelay_ms = 20;
        const double expectedFreq = 1000 / (taskSemiDelay_ms * 2) * taskCount;

        var tasks = Enumerable.Range(0, taskCount).Select(i => ConcurrentTask(i)).ToArray();

        return Task.WhenAll(tasks);

        async Task ConcurrentTask(int taskIndex)
        {
            using var process = taskIndex == 0 ? Process.GetCurrentProcess() : null;
            int prevCount = TotCount;
            var prevProcTime = TimeSpan.Zero;
            int n = 0;
            int n2 = 0;
            var time = new Stopwatch();
            while (!stoppingToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref TotCount);
                if (!time.IsRunning || time.ElapsedMilliseconds > 5000)
                {
                    if (taskIndex == 0)
                    {
                        Debug.Assert(process != null);
                        var procTime = process.TotalProcessorTime;
                        var deltaProcTime = procTime - prevProcTime;
                        prevProcTime = procTime;

                        var tc = TotCount;
                        var deltaCount = tc - prevCount;
                        prevCount = tc;
                        var deltaT = time.Elapsed;
                        if (deltaT > TimeSpan.Zero)
                        {
                            var freq = double.Round(deltaCount / deltaT.TotalSeconds);
                            var freqRatio = double.Round(100 * freq / expectedFreq);
                            var cpu_perc = double.Round((deltaProcTime / deltaT) * 100);
                            _logger.LogWarning("TaskFrequency:{TaskFrequency} ({TaskFrequencyRatio}%) CPU%:{CpuPerc}", freq, freqRatio, cpu_perc);
                        }
                    }

                    if(taskIndex == 0)
                        _logger.LogInformation("The current time is: {CurrentTime}", DateTimeOffset.UtcNow);
                    //else
                    //    _logger.LogDebug("The current time is: {CurrentTime}", DateTimeOffset.UtcNow);

                    time.Restart();
                    var l = _loggerFactory.CreateLogger($"{typeof(PrintTimeService).FullName}.Task[{taskIndex}].Cycle.{++n}");
                    l.LogDebug("Write something in a just created new logger...");
                }
                var l2 = _loggerFactory.CreateLogger($"{typeof(PrintTimeService).FullName}.Task[{taskIndex}].TicTac.{++n2}");
                l2.LogDebug("Write something in a just created new logger...");
                await Task.Delay(taskSemiDelay_ms, stoppingToken);
                _logger.LogDebug("Tic");
                await Task.Delay(taskSemiDelay_ms, stoppingToken);
                _logger.LogTrace("Tac");
            }
        }

    }
}
