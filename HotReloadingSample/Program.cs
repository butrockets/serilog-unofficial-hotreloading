using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Serilog;


var reloadingStrategy = ReloadingStrategy.Original;
if(args.Length > 0)
{
    if (args[0].Contains('u')) reloadingStrategy = ReloadingStrategy.Unofficial;
    else if (args[0].Contains('s')) reloadingStrategy = ReloadingStrategy.Switchable;
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
            .WriteTo.Console(consoleLevel, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}->{Message:lj}{NewLine}{Exception}")
            );
    }
    doReloadLoggerConfiguration();
    if(args.Length > 0 && args[0].Contains('f'))
    {
        Log.Information("Freezing logger configuration");
        reloadableLogger.Freeze();
    }

    ChangeToken.OnChange(
        changeTokenProducer: builder.Configuration.GetSection("Serilog").GetReloadToken,
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
    Original,       //Serilog.Extensions.Hosting.ReloadableLogger
    Unofficial,     //Serilog.Extensions.Hosting.ReloadableLogger
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