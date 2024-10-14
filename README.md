# Serilog.Unofficial.HotReloading 

[Serilog](https://serilog.net/) logger supporting hot reload to apply at runtime logger pipeline configuration changes

## Quick start

```
md QuickStartSample
cd QuickStartSample
dotnet new console
dotnet add package Microsoft.Extensions.Hosting
dotnet add package Serilog.Unofficial.HotReloading
dotnet add package Serilog.Extensions.Hosting
dotnet add package Serilog.Settings.Configuration
dotnet add package Serilog.Sinks.Console
```

````cs filename="Program.cs"
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
````

````json filename="appsettings.json"
//appsettings.json
{
  "Serilog": {
    "MinimumLevel": {
      //Microsoft levels (used in code):   Trace | Debug | Information | Warning | Error | Critical | None
      //Serilog levels (used in config): Verbose | Debug | Information | Warning | Error | Fatal
      "Default": "Information",
      "Override": {
        "Microsoft": "Information",

        // Try to uncomment one of this lines and save the file while the program is running
        // to see new logger configuration reloader in action
        //"SampleBackgroundService": "Debug",
        //"SampleBackgroundService": "Verbose",

        "System": "Warning"
      }
    }
  }
}
````

## Introduction

Serilog is based on an immutable "logging pipeline" usually configured at application startup (see [serilog Configuration Basics](https://github.com/serilog/serilog/wiki/configuration-basics))

When you receive an `ILogger` instance (directly from Serilog via the `Log.ForContext(...)` API and/or through DI integration i.e. with `Serilog.Extensions.Hosting` and `Microsoft.Extensions.Logging.Abstractions` interfaces `ILogger<T>`/`ILoggerFactory`) the behavior of the `ILogger` is strictly bound forever to the root immutable logging pipeline.

While this is very efficient, sometimes you may need more flexibility. For example you may want to change on the fly the minimum logging level for some `SourceContext` and/or redirect some heavy low-level logging on temp files just for few time span...

Some of these patterns are fully or partially supported
 - via `LoggingLevelSwitch` (see [Dynamically changing the Serilog level](https://nblumhardt.com/2014/10/dynamically-changing-the-serilog-level/))
 - via `Serilog.Sinks.Map` (see [Hot-reload any Serilog sink](https://nblumhardt.com/2023/02/dynamically-reload-any-serilog-sink/))

But all them suffer some limitation (i.e. you cannot add a new `SourceContext` in app configuration file when it's already running [Adding new Override's on the fly](https://github.com/serilog/serilog-settings-configuration/issues/284#issuecomment-1289664499)).

### Wrapper approach

In my knowledge there are two example of a different approach very promising to overcome all the limitation and allow the full runtime reconfiguration capability (**!!! obviously it's not for free: all enhancements in runtime flexibility OBVIOUSLY come at some (little?) price in runtime perfs!!!** )

 - `SwitchableLogger` from unofficial [`Serilog.Settings.Reloader`](https://github.com/tagcode/serilog-settings-reloader) package 
 - `ReloadableLogger`/`CreateBootstrapLogger()` from official [`Serilog.Extensions.Hosting`](https://github.com/serilog/serilog-extensions-hosting) package (see [Bootstrap logging with Serilog + ASP.NET Core](https://nblumhardt.com/2020/10/bootstrap-logger/))

booth of them does some kind of "wrapping" around the real root serilog's logger pipeline allowing to swap it at runtime while the user's `ILogger` reference remain the same.

#### SwitchableLogger: it's lock free... synch issues?

I used the unofficial `SwitchableLogger` for a long time: it just works very well (and IMHO the runtime performances are very high, probably the better possible of the "wrapper" approach) but i discovered a potential synchronization issue: during the (little?) time while we are "switching" the main logger, there is a race condition so an already existent `ILogger` has a chance to write to a disposed sink.

See `HotReloadingSynchronization` test in this repo for details

Is it a concern? I don't know. IMHO the real impact is sink implementation dependent... (but it's reasonable to expect in the worst case some missing events in log files... probably not a big issue in many cases)

#### ReloadableLogger possible improvements

Anyway i decided to create this project deriving it from the official `ReloadableLogger`, and relying on it's strong synchronization model.
I decided to fork it and not to use AS-IS because i tried also to gain 2 little extra bonus improvements:
 
 1. Remove the dependency from `Microsoft.Extensions.*` 
 2. Optimize the "unfrozen" state.  
    In fact the original `ReloadableLogger` typical use case is to keep the logger in a mutable "unfrozen" state just for the time of app initialization and then switch it into an high optimized (0 overhead in many cases) "frozen" state ASAP the app is ready to run.  
    But while in unfrozen mutable state, the original implementation use a simple lock and a [process-wide memory barrier](https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.memorybarrierprocesswide?view=net-8.0) to synchronize all the access to logger.  
    In this implementation a [`ReaderWriterLockSlim`](https://learn.microsoft.com/en-us/dotnet/api/system.threading.readerwriterlockslim?view=net-8.0) is used allowing a a better concurrency when the logger is used (the fast path scenario used very often) and locking them only when a configuration change is in-flight (the worst case rare path).

## More info

Take a look into the provided `HotReloadSample` app: it allow to test this implementation side-by side with the other presented alternativers...

## References

 - [Adding new Override's on the fly](https://github.com/serilog/serilog-settings-configuration/issues/284)
 - [Enables updating the configured min-level overrides at runtime](https://github.com/serilog/serilog/pull/1764)
 - [Bootstrap logging with Serilog + ASP.NET Core](https://nblumhardt.com/2020/10/bootstrap-logger/)
 - [Hot-reload any Serilog sink](https://nblumhardt.com/2023/02/dynamically-reload-any-serilog-sink/)
 - [Reload Serilog JSON Configuration on changes in .NET Core 2.1](https://stackoverflow.com/questions/53449596/reload-serilog-json-configuration-on-changes-in-net-core-2-1)
 - [Dynamically changing the Serilog level](https://nblumhardt.com/2014/10/dynamically-changing-the-serilog-level/)
 - [Serilog.Settings.Reloader](https://github.com/tagcode/serilog-settings-reloader)
 

