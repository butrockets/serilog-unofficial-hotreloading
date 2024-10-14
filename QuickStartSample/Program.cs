// Program.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Serilog;

//Create a serilog "BootstratLogger" at the very first stage of the application startup
var reloadableLogger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] BootstrapLogger->{Message:lj}{NewLine}{Exception}")
    .CreateReloadableLogger();

//Assign it to static serilog Log API
Log.Logger = reloadableLogger;

//You can use the serilog Log API
Log.Information("Getting the motors running...");
Log.Information("Using ReloadableLogger {ReloadableLoggerType}", reloadableLogger.GetType().FullName);

//Create an ASP.net core app
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<SampleBackgroundService>();
builder.Services.AddSerilog(reloadableLogger);
var app = builder.Build();

//Now you can use the Microsoft.Extensions.Logging.Abstractions API
var mainLogger = app.Services.GetRequiredService<ILogger<Program>>();
mainLogger.LogInformation("App built!!");

//Reloading logic
//Here you can customize the serilog pipeline
void doReloadLoggerConfiguration()
{
    mainLogger.LogInformation("Reloading logger configuration");
    reloadableLogger.Reload(lc => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(app.Services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}->{Message:lj}{NewLine}{Exception}")
        );
}
doReloadLoggerConfiguration();
ChangeToken.OnChange(
    changeTokenProducer: ((IConfigurationRoot)builder.Configuration).GetReloadToken,
    changeTokenConsumer: doReloadLoggerConfiguration);


app.Run();

mainLogger.LogInformation("App terminated");
Log.CloseAndFlush();

class SampleBackgroundService(ILogger<SampleBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Started!!!");
        while (true)
        {
            await Task.Delay(1000, stoppingToken);
            logger.LogDebug("Tic");
            await Task.Delay(1000, stoppingToken);
            logger.LogTrace("Tac");
        }
    }
}
