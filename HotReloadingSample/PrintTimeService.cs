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

        var tasks = Enumerable.Range(0, 500).Select(i => ConcurrentTask(i)).ToArray();
        //var tasks = Enumerable.Range(0, 10).Select(i => ConcurrentTask(i)).ToArray();
        //var tasks = Enumerable.Range(0, 1).Select(i => ConcurrentTask(i)).ToArray();

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
                ++TotCount;
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
                            var freq = deltaCount / deltaT.TotalSeconds;

                            var cpu_perc = double.Round((deltaProcTime / deltaT) * 100);
                            _logger.LogWarning("TaskFrequency:{TaskFrequency} CPU%:{CpuPerc}", freq, cpu_perc);
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
                //var l2 = _loggerFactory.CreateLogger($"{typeof(PrintTimeService).FullName}.Task[{taskIndex}].TicTac.{++n2}");
                //l2.LogDebug("Write something in a just created new logger...");
                await Task.Delay(2, stoppingToken);
                _logger.LogDebug("Tic");
                await Task.Delay(2, stoppingToken);
                _logger.LogTrace("Tac");
            }
        }

    }
}
