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
using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace Serilog.Unofficial.HotReloading;

interface IReloadableLogger
{
    ILogger ReloadLogger();
}
interface ICachingReloadableLogger : IReloadableLogger
{
    ILogger? Root { get; }
    ILogger? Cached { get; }
    object UpdateSync { get; }
    void Update(ILogger newRoot, ILogger newCached, bool frozen);
}

class CachingReloadableLogger : ILogger, ICachingReloadableLogger
{
    readonly ReloadableLogger _reloadableLogger;
    readonly Func<ILogger, ILogger> _configure;
    readonly IReloadableLogger _parent;

    ILogger? _root;    
    ILogger? _cached;

    bool _frozen;

    public CachingReloadableLogger(ReloadableLogger reloadableLogger, ILogger? root, IReloadableLogger parent, Func<ILogger, ILogger> configure)
    {
        _reloadableLogger = reloadableLogger;
        _parent = parent;
        _configure = configure;
        _root = root;
        _cached = null;
        _frozen = false;
    }

    ILogger? ICachingReloadableLogger.Root => _root;
    ILogger? ICachingReloadableLogger.Cached => _cached;

    public ILogger ReloadLogger()
    {
        return _configure(_parent.ReloadLogger());
    }

    public ILogger ForContext(ILogEventEnricher enricher)
    {
        if (enricher == null) return this;
        
        if (_frozen)
            return _cached!.ForContext(enricher);

        return _reloadableLogger.CreateChild(            
            this,            
            p => p.ForContext(enricher));
    }

    public ILogger ForContext(IEnumerable<ILogEventEnricher> enrichers)
    {
        if (enrichers == null) return this;
        
        if (_frozen)
            return _cached!.ForContext(enrichers);

        return _reloadableLogger.CreateChild(
            this,
            p => p.ForContext(enrichers));
    }

    public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
    {
        if (propertyName == null) return this;
        
        if (_frozen)
            return _cached!.ForContext(propertyName, value, destructureObjects);

        if (value == null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum)
        {
            // Safe to extend the lifetime of `value` by closing over it.
            // This ensures `SourceContext` is passed through appropriately and triggers minimum level overrides.
            return _reloadableLogger.CreateChild(
                this,
                p => p.ForContext(propertyName, value, destructureObjects));
        }
        else
        {
            // It's not safe to extend the lifetime of `value` or pass it unexpectedly between threads.
            // Changes to destructuring configuration won't be picked up by the cached logger.
            var eager = ReloadLogger();
            if (!eager.BindProperty(propertyName, value, destructureObjects, out var property))
                return this;

            var enricher = new FixedPropertyEnricher(property);

            return _reloadableLogger.CreateChild(
                this,
                p => p.ForContext(enricher));
        }
    }

    public ILogger ForContext<TSource>()
    {
        if (_frozen)
            return _cached!.ForContext<TSource>();

        return _reloadableLogger.CreateChild(
            this,
            p => p.ForContext<TSource>());
    }

    public ILogger ForContext(Type source)
    {
        if (_frozen)
            return _cached!.ForContext(source);

        return _reloadableLogger.CreateChild(
            this,
            p => p.ForContext(source));
    }

    object ICachingReloadableLogger.UpdateSync { get; } = new();
    void ICachingReloadableLogger.Update(ILogger newRoot, ILogger newCached, bool frozen)
    {
        _root = newRoot;
        _cached = newCached;
        
        if (!frozen)
            return; //in this case we are under the `ReloadLogger` lock!!!


        // https://github.com/dotnet/runtime/issues/20500#issuecomment-284774431
        // Publish `_cached` and then `_frozen`. This is useful here because it means that once the logger is frozen - which
        // we always expect - reads don't require any synchronization/interlocked instructions.
        Interlocked.MemoryBarrierProcessWide();

        _frozen = frozen;

        Interlocked.MemoryBarrierProcessWide();
    }

    public void Write(LogEvent logEvent)
    {
        if (_frozen)
        {
            _cached!.Write(logEvent);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            logEvent);
    }

    public void Write(LogEventLevel level, string messageTemplate)
    {
        if (_frozen)
        {
            _cached!.Write(level, messageTemplate);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            level,
            messageTemplate);
    }

    public void Write<T>(LogEventLevel level, string messageTemplate, T propertyValue)
    {
        if (_frozen)
        {
            _cached!.Write(level, messageTemplate, propertyValue);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            level,
            messageTemplate,
            propertyValue);
    }

    public void Write<T0, T1>(LogEventLevel level, string messageTemplate, T0 propertyValue0, T1 propertyValue1)
    {
        if (_frozen)
        {
            _cached!.Write(level, messageTemplate, propertyValue0, propertyValue1);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            level,
            messageTemplate,
            propertyValue0,
            propertyValue1);
    }

    public void Write<T0, T1, T2>(LogEventLevel level, string messageTemplate, T0 propertyValue0, T1 propertyValue1,
        T2 propertyValue2)
    {
        if (_frozen)
        {
            _cached!.Write(level, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            level,
            messageTemplate,
            propertyValue0,
            propertyValue1,
            propertyValue2);
    }

    public void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues)
    {
        if (_frozen)
        {
            _cached!.Write(level, messageTemplate, propertyValues);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            level,
            messageTemplate,
            propertyValues);
    }

    public void Write(LogEventLevel level, Exception? exception, string messageTemplate)
    {
        if (_frozen)
        {
            _cached!.Write(level, exception, messageTemplate);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            level,
            exception,
            messageTemplate);
    }

    public void Write<T>(LogEventLevel level, Exception? exception, string messageTemplate, T propertyValue)
    {
        if (_frozen)
        {
            _cached!.Write(level, exception, messageTemplate, propertyValue);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            level,
            exception,
            messageTemplate,
            propertyValue);
    }

    public void Write<T0, T1>(LogEventLevel level, Exception? exception, string messageTemplate, T0 propertyValue0,
        T1 propertyValue1)
    {
        if (_frozen)
        {
            _cached!.Write(level, exception, messageTemplate, propertyValue0, propertyValue1);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            level,
            exception,
            messageTemplate,
            propertyValue0,
            propertyValue1);
    }

    public void Write<T0, T1, T2>(LogEventLevel level, Exception? exception, string messageTemplate, T0 propertyValue0,
        T1 propertyValue1, T2 propertyValue2)
    {
        if (_frozen)
        {
            _cached!.Write(level, exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            level,
            exception,
            messageTemplate,
            propertyValue0,
            propertyValue1,
            propertyValue2);
    }

    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues)
    {
        if (_frozen)
        {
            _cached!.Write(level, exception, messageTemplate, propertyValues);
            return;
        }

        _reloadableLogger.InvokeWrite(
            this,
            level,
            exception,
            messageTemplate,
            propertyValues);
    }

    public bool IsEnabled(LogEventLevel level)
    {
        if (_frozen)
        {
            return _cached!.IsEnabled(level);
        }

        return _reloadableLogger.InvokeIsEnabled(
            this,
            level);
    }
    
    public bool BindMessageTemplate(string messageTemplate, object?[]? propertyValues, [MaybeNullWhen(false)] out MessageTemplate parsedTemplate,
        [MaybeNullWhen(false)] out IEnumerable<LogEventProperty> boundProperties)
    {
        if (_frozen)
        {
            return _cached!.BindMessageTemplate(messageTemplate, propertyValues, out parsedTemplate, out boundProperties);
        }

        return _reloadableLogger.InvokeBindMessageTemplate(
            this,
            messageTemplate,
            propertyValues,
            out parsedTemplate,
            out boundProperties);
    }

    public bool BindProperty(string? propertyName, object? value, bool destructureObjects, [MaybeNullWhen(false)] out LogEventProperty property)
    {
        if (_frozen)
        {
            return _cached!.BindProperty(propertyName, value, destructureObjects, out property);
        }

        return _reloadableLogger.InvokeBindProperty(
            this,
            propertyName,
            value,
            destructureObjects,
            out property);
    }
}
