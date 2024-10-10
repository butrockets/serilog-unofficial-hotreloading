// Copyright 2020 Serilog Contributors
// Copyright 2024 Giuseppe Marazzi
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace Serilog.Unofficial.HotReloading;

/// <summary>
/// A Serilog <see cref="ILogger"/> that can be reconfigured without invalidating existing <see cref="ILogger"/>
/// instances derived from it.
/// </summary>
public sealed class ReloadableLogger : ILogger, IReloadableLogger, IDisposable
{
    readonly ReaderWriterLockSlim _sync = new();
    readonly object _disposeSync = new object();
    Logger _logger;
    
    // One-way; if the value is `true` it can never again be made `false`, allowing "double-checked" reads. If
    // `true`, `_logger` is final and a memory barrier ensures the final value is seen by all threads.
    bool _frozen;

    // Unsure whether this should be exposed; currently going for minimal API surface.
    internal ReloadableLogger(Logger initial)
    {
        _logger = initial ?? throw new ArgumentNullException(nameof(initial));
    }
    
    ILogger IReloadableLogger.ReloadLogger()
    {
        return _logger;
    }

    /// <summary>
    /// Reload the logger using the supplied configuration delegate.
    /// </summary>
    /// <param name="configure">A callback in which the logger is reconfigured.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    public void Reload(Func<LoggerConfiguration, LoggerConfiguration> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        using (_sync.WriteLock())
        {
            _logger.Dispose();
            _logger = configure(new LoggerConfiguration()).CreateLogger();
        }
    }

    /// <summary>
    /// Freeze the logger, so that no further reconfiguration is possible. Once the logger is frozen, logging through
    /// new contextual loggers will have no additional cost, and logging directly through this logger will not require
    /// any synchronization.
    /// </summary>
    /// <returns>The <see cref="Logger"/> configured with the final settings.</returns>
    /// <exception cref="InvalidOperationException">The logger is already frozen.</exception>
    public Logger Freeze()
    {
        using (_sync.WriteLock())
        {
            if (_frozen)
                throw new InvalidOperationException("The logger is already frozen.");

            _frozen = true;

            // https://github.com/dotnet/runtime/issues/20500#issuecomment-284774431
            // Publish `_logger` and `_frozen`. This is useful here because it means that once the logger is frozen - which
            // we always expect - reads don't require any synchronization/interlocked instructions.
            Interlocked.MemoryBarrierProcessWide();
            
            return _logger;
        }
    }

    private bool _isDisposed = false;
    /// <inheritdoc />
    public void Dispose()
    {
        lock (_disposeSync)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            using (_sync.WriteLock())
            {
                _logger.Dispose();
            }
            try
            {
                _sync.Dispose();
            }
            catch
            {
            }
        }
    }

    /// <inheritdoc />
    public ILogger ForContext(ILogEventEnricher enricher)
    {
        if (enricher == null) return this;
        
        if (_frozen)
            return _logger.ForContext(enricher);

        using (_sync.ReadLock())
            return new CachingReloadableLogger(this, _logger, this, p => p.ForContext(enricher));
    }

    /// <inheritdoc />
    public ILogger ForContext(IEnumerable<ILogEventEnricher> enrichers)
    {
        if (enrichers == null) return this;
        
        if (_frozen)
            return _logger.ForContext(enrichers);

        using (_sync.ReadLock())
            return new CachingReloadableLogger(this, _logger, this, p => p.ForContext(enrichers));
    }

    /// <inheritdoc />
    public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
    {
        if (propertyName == null) return this;
        
        if (_frozen)
            return _logger.ForContext(propertyName, value, destructureObjects);

        using (_sync.ReadLock())
            return new CachingReloadableLogger(this, _logger, this, p => p.ForContext(propertyName, value, destructureObjects));
    }

    /// <inheritdoc />
    public ILogger ForContext<TSource>()
    {
        if (_frozen)
            return _logger.ForContext<TSource>();

        using (_sync.ReadLock())
            return new CachingReloadableLogger(this, _logger, this, p => p.ForContext<TSource>());
    }

    /// <inheritdoc />
    public ILogger ForContext(Type source)
    {
        if (source == null) return this;
        
        if (_frozen)
            return _logger.ForContext(source);

        using (_sync.ReadLock())
            return new CachingReloadableLogger(this, _logger, this, p => p.ForContext(source));
    }

    /// <inheritdoc />
    public void Write(LogEvent logEvent)
    {
        if (_frozen)
        {
            _logger.Write(logEvent);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(logEvent);
        }
    }

    /// <inheritdoc />
    public void Write(LogEventLevel level, string messageTemplate)
    {
        if (_frozen)
        {
            _logger.Write(level, messageTemplate);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(level, messageTemplate);
        }
    }

    /// <inheritdoc />
    public void Write<T>(LogEventLevel level, string messageTemplate, T propertyValue)
    {
        if (_frozen)
        {
            _logger.Write(level, messageTemplate, propertyValue);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(level, messageTemplate, propertyValue);
        }
    }

    /// <inheritdoc />
    public void Write<T0, T1>(LogEventLevel level, string messageTemplate, T0 propertyValue0, T1 propertyValue1)
    {
        if (_frozen)
        {
            _logger.Write(level, messageTemplate, propertyValue0, propertyValue1);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(level, messageTemplate, propertyValue0, propertyValue1);
        }
    }

    /// <inheritdoc />
    public void Write<T0, T1, T2>(LogEventLevel level, string messageTemplate, T0 propertyValue0, T1 propertyValue1,
        T2 propertyValue2)
    {
        if (_frozen)
        {
            _logger.Write(level, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(level, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
        }
    }

    /// <inheritdoc />
    public void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues)
    {
        if (_frozen)
        {
            _logger.Write(level, messageTemplate, propertyValues);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(level, messageTemplate, propertyValues);
        }
    }

    /// <inheritdoc />
    public void Write(LogEventLevel level, Exception? exception, string messageTemplate)
    {
        if (_frozen)
        {
            _logger.Write(level, exception, messageTemplate);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(level, exception, messageTemplate);
        }
    }

    /// <inheritdoc />
    public void Write<T>(LogEventLevel level, Exception? exception, string messageTemplate, T propertyValue)
    {
        if (_frozen)
        {
            _logger.Write(level, exception, messageTemplate, propertyValue);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(level, exception, messageTemplate, propertyValue);
        }
    }

    /// <inheritdoc />
    public void Write<T0, T1>(LogEventLevel level, Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1)
    {
        if (_frozen)
        {
            _logger.Write(level, exception, messageTemplate, propertyValue0, propertyValue1);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(level, exception, messageTemplate, propertyValue0, propertyValue1);
        }
    }

    /// <inheritdoc />
    public void Write<T0, T1, T2>(LogEventLevel level, Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1,
        T2 propertyValue2)
    {
        if (_frozen)
        {
            _logger.Write(level, exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(level, exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
        }
    }

    /// <inheritdoc />
    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues)
    {
        if (_frozen)
        {
            _logger.Write(level, exception, messageTemplate, propertyValues);
            return;
        }

        using (_sync.ReadLock())
        {
            _logger.Write(level, exception, messageTemplate, propertyValues);
        }
    }

    /// <inheritdoc />
    public bool IsEnabled(LogEventLevel level)
    {
        if (_frozen)
        {
            return _logger.IsEnabled(level);
        }

        using (_sync.ReadLock())
        {
            return _logger.IsEnabled(level);
        }
    }
    
    /// <inheritdoc />
    public bool BindMessageTemplate(string messageTemplate, object?[]? propertyValues, [MaybeNullWhen(false)] out MessageTemplate parsedTemplate,
        [MaybeNullWhen(false)] out IEnumerable<LogEventProperty> boundProperties)
    {
        if (_frozen)
        {
            return _logger.BindMessageTemplate(messageTemplate, propertyValues, out parsedTemplate, out boundProperties);
        }

        using (_sync.ReadLock())
        {
            return _logger.BindMessageTemplate(messageTemplate, propertyValues, out parsedTemplate, out boundProperties);
        }
    }

    /// <inheritdoc />
    public bool BindProperty(string? propertyName, object? value, bool destructureObjects, [MaybeNullWhen(false)] out LogEventProperty property)
    {
        if (_frozen)
        {
            return _logger.BindProperty(propertyName, value, destructureObjects, out property);
        }

        using (_sync.ReadLock())
        {
            return _logger.BindProperty(propertyName, value, destructureObjects, out property);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ILogger UpdateForCaller(ICachingReloadableLogger caller)
    {
        // Synchronization on `_sync` is not required in this method; it will be called without a lock
        // if `_frozen` and under a lock if not.
        ILogger newCached;
        if (_frozen)
        {
            // If we're frozen, then the caller hasn't observed this yet and should update. We could optimize a little here
            // and only signal an update if the cached logger is stale (as per the next condition below).
            newCached = caller.ReloadLogger();
            caller.Update(newRoot: _logger, newCached, frozen: true);
            return newCached;
        }

        // here we are not `_frozen`: so we are under a lock
        // since we are using a ReaderWriterLockSlim
        // the access here is exclusive only between writers (i.e. reload)
        // and readers (i.e. when using logger)
        // concurrent access by readers is allowed: so we need
        // a fine grained lock on the caller to (eventually) sync the cache update
        lock (caller.UpdateSync)
        {
            if (caller.Cached != null && caller.Root == _logger)
            {
                // this is the fast path used most the times
                // when nothing new has appened
                return caller.Cached;
            }

            // but sometimes i.e.
            // 1 - after a reload (caller.Root != _logger)
            // OR
            // 2 - at the very first use o a new child logger (caller.Cached == null)
            // we need to update (or "lazy initialize" if 2) the cached wrapper
            newCached = caller.ReloadLogger();
            caller.Update(newRoot: _logger, newCached, frozen: false);
            return newCached;
        }
    }

    internal bool InvokeIsEnabled(ICachingReloadableLogger caller, LogEventLevel level)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);            
            return logger.IsEnabled(level);
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            return logger.IsEnabled(level);
        }
    }

    internal bool InvokeBindMessageTemplate(ICachingReloadableLogger caller, string messageTemplate, 
        object?[]? propertyValues, out MessageTemplate? parsedTemplate, out IEnumerable<LogEventProperty>? boundProperties)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            return logger.BindMessageTemplate(messageTemplate, propertyValues, out parsedTemplate, out boundProperties);
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            return logger.BindMessageTemplate(messageTemplate, propertyValues, out parsedTemplate, out boundProperties);
        }
    }
    
    internal bool InvokeBindProperty(ICachingReloadableLogger caller, string? propertyName, 
        object? propertyValue, bool destructureObjects, out LogEventProperty? property)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            return logger.BindProperty(propertyName, propertyValue, destructureObjects, out property);
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            return logger.BindProperty(propertyName, propertyValue, destructureObjects, out property);
        }
    }

    internal void InvokeWrite(ICachingReloadableLogger caller, LogEvent logEvent)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(logEvent);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(logEvent);
        }
    }

    internal void InvokeWrite(ICachingReloadableLogger caller, LogEventLevel level, string messageTemplate)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, messageTemplate);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, messageTemplate);
        }
    }

    internal void InvokeWrite<T>(ICachingReloadableLogger caller, LogEventLevel level, string messageTemplate,
        T propertyValue)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, messageTemplate, propertyValue);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, messageTemplate, propertyValue);
        }
    }

    internal void InvokeWrite<T0, T1>(ICachingReloadableLogger caller, LogEventLevel level, string messageTemplate,
        T0 propertyValue0, T1 propertyValue1)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, messageTemplate, propertyValue0, propertyValue1);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, messageTemplate, propertyValue0, propertyValue1);
        }
    }

    internal void InvokeWrite<T0, T1, T2>(ICachingReloadableLogger caller, LogEventLevel level, string messageTemplate,
        T0 propertyValue0, T1 propertyValue1, T2 propertyValue2)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
        }
    }

    internal void InvokeWrite(ICachingReloadableLogger caller, LogEventLevel level, string messageTemplate,
        object?[]? propertyValues)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, messageTemplate, propertyValues);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, messageTemplate, propertyValues);
        }
    }

    internal void InvokeWrite(ICachingReloadableLogger caller, LogEventLevel level, Exception? exception, string messageTemplate)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, exception, messageTemplate);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, exception, messageTemplate);
        }
    }

    internal void InvokeWrite<T>(ICachingReloadableLogger caller, LogEventLevel level, Exception? exception, string messageTemplate,
        T propertyValue)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, exception, messageTemplate, propertyValue);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, exception, messageTemplate, propertyValue);
        }
    }

    internal void InvokeWrite<T0, T1>(ICachingReloadableLogger caller, LogEventLevel level, Exception? exception, string messageTemplate,
        T0 propertyValue0, T1 propertyValue1)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, exception, messageTemplate, propertyValue0, propertyValue1);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, exception, messageTemplate, propertyValue0, propertyValue1);
        }
    }

    internal void InvokeWrite<T0, T1, T2>(ICachingReloadableLogger caller, LogEventLevel level, Exception? exception, string messageTemplate,
        T0 propertyValue0, T1 propertyValue1, T2 propertyValue2)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
        }
    }

    internal void InvokeWrite(ICachingReloadableLogger caller, LogEventLevel level, Exception? exception, string messageTemplate,
        object?[]? propertyValues)
    {
        if (_frozen)
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, exception, messageTemplate, propertyValues);
            return;
        }

        using (_sync.ReadLock())
        {
            var logger = UpdateForCaller(caller);
            logger.Write(level, exception, messageTemplate, propertyValues);
        }
    }

    internal ILogger CreateChild(
        ICachingReloadableLogger parent,
        Func<ILogger, ILogger> configureChild)
    {
        if (_frozen)
        {
            var newCachedParent = parent.ReloadLogger();
            // Always an update, since the caller has not observed that the reloadable logger is frozen.
            parent.Update(newRoot: _logger, newCachedParent, frozen: true);
            return configureChild(newCachedParent);
        }

        // No synchronization, here - a lot of loggers are created and thrown away again without ever being used,
        // so we just return a lazy wrapper.
        return new CachingReloadableLogger(this, null, parent, configureChild);
    }
}
