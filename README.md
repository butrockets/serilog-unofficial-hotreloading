# Serilog.Unofficial.HotReloading 

[Serilog](https://serilog.net/) logger supporting hot reload to apply at runtime logger pipeline configuration changes

## Introduction

Serilog is based on an immutable "logging pipeline" usually configured at application startup (see [serilog Configuration Basics](https://github.com/serilog/serilog/wiki/configuration-basics))

When you receive an `ILogger` instance (directly from Serilog via the `Log.ForContext(...)` API and/or through DI integration i.e. with `Serilog.Extensions.Hosting` and `Microsoft.Extensions.Logging.Abstractions` interfaces `ILogger<T>`/`ILoggerFactory`) the behavior of the `ILogger` is strictly bound forever to the root immutable logging pipeline.

While this is very efficient, sometimes you may need more flexibility. For example you may want to change on the fly the minimum logging level for some `SourceContext` and/or redirect some heavy low-level logging on temp files just for few time span...

Some of these patterns are fully or partially supported
 - via `LoggingLevelSwitch` (see [Dynamically changing the Serilog level](https://nblumhardt.com/2014/10/dynamically-changing-the-serilog-level/))
 - via `Serilog.Sinks.Map` (see [Hot-reload any Serilog sink](https://nblumhardt.com/2023/02/dynamically-reload-any-serilog-sink/))

But all them suffer some limitation (i.e. you cannot add a new `SourceContext` in app configuration file when it's already running [Adding new Override's on the fly](https://github.com/serilog/serilog-settings-configuration/issues/284#issuecomment-1289664499)).

In my knowledge there are two example of a different approach very promising to overcome all the limitation and allow the full runtime reconfiguration capability (**!!! obviously it's not for free: all enhancements in runtime flexibility OBVIOUSLY come at some (little?) price in runtime perfs!!!** )

 - `SwitchableLogger` from unofficial [`Serilog.Settings.Reloader`](https://github.com/tagcode/serilog-settings-reloader) package 
 - `ReloadableLogger`/`CreateBootstrapLogger()` from official [`Serilog.Extensions.Hosting`](https://github.com/serilog/serilog-extensions-hosting) package (see [Bootstrap logging with Serilog + ASP.NET Core]()https://nblumhardt.com/2020/10/bootstrap-logger/)

booth of them does some kind of "wrapping" around the real root serilog's logger pipeline allowing to swap it at runtime while the user's `ILogger` reference remain the same.

## SwitchableLogger: it's lock free... synch issues?

I used the unofficial `SwitchableLogger` for a long time: it just works very well (and IMHO the runtime performances are very high, probably the better possible of the "wrapper" approach) but i discovered a potential synchronization issue: during the (little?) time while we are "switching" the main logger, there is the chance for an already existent `ILogger` to write to a disposed sink.

See `HotReloadingSynchronization` test in this repo for details

 Is it a concern? I don't know. IMHO the real impact is sink implementation dependent... (but it's reasonable to expect in the worst case some missing events in log files)

## ReloadableLogger possible improvements

Anyway i decided to create this project deriving it from the official `ReloadableLogger` (and relying on it's strong synchronization model) with two main differences:
 
 1. It's totally independent from `Microsoft.Extensions.*` 
 2. It's slightly optimized for the "unfrozen" state (in fact the original `ReloadableLogger` typical use case is to keep the logger in a mutable "unfrozen" state just for the time of app initialization and then switch it into an high optimized (0 overhead in many cases) "frozen" state ASAP the app is ready to run)


## Instructions

Follow the 


## References

 - [Adding new Override's on the fly](https://github.com/serilog/serilog-settings-configuration/issues/284)
 - [Enables updating the configured min-level overrides at runtime](https://github.com/serilog/serilog/pull/1764)
 - [Bootstrap logging with Serilog + ASP.NET Core](https://nblumhardt.com/2020/10/bootstrap-logger/)
 - [Hot-reload any Serilog sink](https://nblumhardt.com/2023/02/dynamically-reload-any-serilog-sink/)
 - [Reload Serilog JSON Configuration on changes in .NET Core 2.1](https://stackoverflow.com/questions/53449596/reload-serilog-json-configuration-on-changes-in-net-core-2-1)
 - [Dynamically changing the Serilog level](https://nblumhardt.com/2014/10/dynamically-changing-the-serilog-level/)
 - [Serilog.Settings.Reloader](https://github.com/tagcode/serilog-settings-reloader)
 

