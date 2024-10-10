using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Serilog;


var reloadableLogger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] BootstrapLogger->{Message:lj}{NewLine}{Exception}")
    .CreateReloadableLogger(args.Length > 0 && args[0].Contains('u'));

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
        reloadableLogger.Reload(lc => lc
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(app.Services)
            .Enrich.FromLogContext()
            .WriteTo.Console(Serilog.Events.LogEventLevel.Information, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}->{Message:lj}{NewLine}{Exception}")
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

static class XX
{
    public static ILogger CreateReloadableLogger(this LoggerConfiguration loggerConfiguration, bool useUnofficial)
        => useUnofficial ? loggerConfiguration.CreateReloadableLogger() : loggerConfiguration.CreateBootstrapLogger();

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