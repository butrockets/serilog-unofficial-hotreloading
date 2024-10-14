using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Serilog;

// This sample tries to compare three different strategies for serilog config hot reloading
// 1 - Original: Serilog.Extensions.Hosting.ReloadableLogger
// 2 - Unofficial (this project) : Serilog.Unofficial.HotReloading.ReloadableLogger
// 3 - Switchable: Serilog.Settings.Reloader.SwitchableLogger
//
// It's not intended as a well designed benchmark but just to give an idea also about runtime performances and overhead
// there is a Background service PrintTimeService spawning a lot of cyclic tasks (5000)
// with about 40 ms task period. In each cycle 2 log events are emitted 
// and a new contextual logger logger is created with an event logged into
// Every 5 seconds some stats are dumped to the console: 
// TaskFrequency: the actual number of task cycle (also a % against the max theoretical number of cycles)
// The % of CPU time used (may be > 100 with a multi core/multi cpu platform)
//
// When the program is running, play with appsettings.json files to enable/disable console and/or file logs
//
var reloadingStrategy = ReloadingStrategy.Unspecified;
if(args.Length > 0 && args[0].StartsWith('-'))
{
    if (args[0].Contains('o')) reloadingStrategy = ReloadingStrategy.Original;
    else if (args[0].Contains('u')) reloadingStrategy = ReloadingStrategy.Unofficial;
    else if (args[0].Contains('s')) reloadingStrategy = ReloadingStrategy.Switchable;
}
var freezeLogger = args.Length > 0 && args[0].Contains('f');

if(reloadingStrategy == ReloadingStrategy.Unspecified)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("-o: original strategy");
    Console.WriteLine("-of: original strategy with freezing");
    Console.WriteLine("-u: unofficial (this project) strategy");
    Console.WriteLine("-uf: unofficial (this project) strategy with freezing");
    Console.WriteLine("-s: switchable strategy");
    return 2;
}


var reloadableLogger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] BootstrapLogger->{Message:lj}{NewLine}{Exception}")
    .CreateReloadableLogger(reloadingStrategy);

Log.Logger = reloadableLogger;

try
{
    Log.Information("Getting the motors running...");
    Log.Information("Using ReloadableLogger {ReloadableLoggerType}", reloadableLogger.GetType().FullName);

    var builder = Host.CreateApplicationBuilder(args);    
    builder.Services.AddHostedService<HotReloadingSample.PrintTimeService>();
    builder.Services.AddSerilog(reloadableLogger);
    var app = builder.Build();

    void doReloadLoggerConfiguration()
    {
        Log.Information("Reloading logger configuration");
        var consoleLevel = builder.Configuration.GetValue<Serilog.Events.LogEventLevel>("ConsoleLogLevel");
        reloadableLogger.Reload(lc => lc
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(app.Services)
            .Enrich.FromLogContext()
            .WriteTo.Console(restrictedToMinimumLevel: consoleLevel, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}->{Message:lj}{NewLine}{Exception}")
            );
    }
    doReloadLoggerConfiguration();
    if(args.Length > 0 && args[0].Contains('f'))
    {
        Log.Information("Freezing logger configuration");
        reloadableLogger.Freeze();
    }

    ChangeToken.OnChange(
        changeTokenProducer: ((IConfigurationRoot)builder.Configuration).GetReloadToken,
        changeTokenConsumer: doReloadLoggerConfiguration);

    await app.RunAsync();
    
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

enum ReloadingStrategy
{
    Unspecified,
    Original,       //Serilog.Extensions.Hosting.ReloadableLogger
    Unofficial,     //Serilog.Unofficial.HotReloading.ReloadableLogger
    Switchable,     //Serilog.Settings.Reloader.SwitchableLogger
}

static class XX
{
    public static ILogger CreateReloadableLogger(this LoggerConfiguration loggerConfiguration, ReloadingStrategy reloadingStrategy)
    {
        switch (reloadingStrategy)
        {
            case ReloadingStrategy.Unofficial:
                return loggerConfiguration.CreateReloadableLogger();
            case ReloadingStrategy.Switchable:
                return new SwitchableLogger(loggerConfiguration.CreateLogger());
        }
        return loggerConfiguration.CreateBootstrapLogger();
    }

    public static void Reload(this ILogger logger, Func<LoggerConfiguration, LoggerConfiguration> configure)
    {
        switch (logger)
        {
            case Serilog.Unofficial.HotReloading.ReloadableLogger url:
                url.Reload(configure);
                break;
            case Serilog.Extensions.Hosting.ReloadableLogger rl:
                rl.Reload(configure);
                break;
            case SwitchableLogger sl:
                sl.Set(configure(new LoggerConfiguration()).CreateLogger(), disposePrev: true);
                break;
        }
    }
    public static void Freeze(this ILogger logger)
    {
        switch (logger)
        {
            case Serilog.Unofficial.HotReloading.ReloadableLogger url:
                url.Freeze();
                break;
            case Serilog.Extensions.Hosting.ReloadableLogger rl:
                rl.Freeze();
                break;
        }
    }

}