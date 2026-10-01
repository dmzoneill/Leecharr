// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jint;
using Jint.Native;
using Jint.Runtime;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Developer.Repl;

public class DeveloperReplService : IDeveloperReplService
{
    private const int MaxHistory = 500;
    private const int DefaultTimeoutSeconds = 15;
    private const int MaxTimeoutSeconds = 60;
    private const int MemoryLimitBytes = 64 * 1024 * 1024; // 64MB

    private static readonly JsonSerializerOptions JsonSerializationOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentEngineManager _torrentEngineManager;
    private readonly IDownloadEngine _downloadEngine;
    private readonly IConfigService _configService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly IMainDatabase _mainDatabase;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly object _sessionLock = new();
    private readonly object _historyLock = new();
    private readonly LinkedList<ReplHistoryEntry> _history = new();

    private Engine _engine;
    private StringBuilder _outputBuffer;
    private int _currentTimeoutSeconds;

    public DeveloperReplService(
        IServiceProvider serviceProvider = null,
        ITorrentService torrentService = null,
        ITorrentEngineManager torrentEngineManager = null,
        IDownloadEngine downloadEngine = null,
        IConfigService configService = null,
        IConfigFileProvider configFileProvider = null,
        IMainDatabase mainDatabase = null)
    {
        _serviceProvider = serviceProvider;
        _torrentService = torrentService;
        _torrentEngineManager = torrentEngineManager;
        _downloadEngine = downloadEngine;
        _configService = configService;
        _configFileProvider = configFileProvider;
        _mainDatabase = mainDatabase;
    }

    public ReplExecutionResponse Execute(ReplExecutionRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var sw = Stopwatch.StartNew();
        var response = new ReplExecutionResponse();

        var language = request.Language?.Trim().ToLowerInvariant() ?? "javascript";
        if (language != "javascript" && language != "js")
        {
            sw.Stop();
            response.Success = false;
            response.ErrorMessage = $"Language '{request.Language}' is not supported. Supported language: 'javascript'.";
            response.ResultType = "unsupported_language";
            response.DurationMs = sw.ElapsedMilliseconds;

            RecordHistory(request.Code, request.Language, false, response.DurationMs);
            return response;
        }

        var timeoutSec = request.TimeoutSeconds > 0
            ? Math.Clamp(request.TimeoutSeconds, 1, MaxTimeoutSeconds)
            : DefaultTimeoutSeconds;

        lock (_sessionLock)
        {
            try
            {
                EnsureEngineInitialized(timeoutSec);
                _outputBuffer.Clear();

                if (string.IsNullOrWhiteSpace(request.Code))
                {
                    sw.Stop();
                    response.Success = true;
                    response.ResultType = "undefined";
                    response.ResultJson = null;
                    response.Output = string.Empty;
                    response.DurationMs = sw.ElapsedMilliseconds;

                    RecordHistory(request.Code, request.Language, true, response.DurationMs);
                    return response;
                }

                var completion = _engine.Evaluate(request.Code);
                sw.Stop();

                response.Success = true;
                response.Output = _outputBuffer.ToString();
                response.DurationMs = sw.ElapsedMilliseconds;

                SetCompletionResult(response, completion);
            }
            catch (JavaScriptException jse)
            {
                sw.Stop();
                response.Success = false;
                response.ResultType = "error";
                response.ErrorMessage = $"JavaScript Error: {jse.Message}";
                _outputBuffer.AppendLine($"[ERROR] {response.ErrorMessage}");
                response.Output = _outputBuffer.ToString();
                response.DurationMs = sw.ElapsedMilliseconds;
            }
            catch (TimeoutException te)
            {
                sw.Stop();
                response.Success = false;
                response.ResultType = "timeout";
                response.ErrorMessage = $"Execution timed out: {te.Message}";
                _outputBuffer.AppendLine($"[ERROR] {response.ErrorMessage}");
                response.Output = _outputBuffer.ToString();
                response.DurationMs = sw.ElapsedMilliseconds;

                // Reset engine on timeout to clear interrupted state
                ResetSessionInternal();
            }
            catch (MemoryLimitExceededException mle)
            {
                sw.Stop();
                response.Success = false;
                response.ResultType = "memory_limit";
                response.ErrorMessage = $"Memory limit exceeded: {mle.Message}";
                _outputBuffer.AppendLine($"[ERROR] {response.ErrorMessage}");
                response.Output = _outputBuffer.ToString();
                response.DurationMs = sw.ElapsedMilliseconds;

                // Reset engine on memory exhaustion
                ResetSessionInternal();
            }
            catch (Exception ex)
            {
                sw.Stop();
                response.Success = false;
                response.ResultType = "error";
                response.ErrorMessage = $"Execution error: {ex.Message}";
                _outputBuffer.AppendLine($"[ERROR] {response.ErrorMessage}");
                response.Output = _outputBuffer.ToString();
                response.DurationMs = sw.ElapsedMilliseconds;
            }
        }

        RecordHistory(request.Code, request.Language, response.Success, response.DurationMs);
        return response;
    }

    public IReadOnlyList<ReplHistoryEntry> GetHistory(int limit = 50)
    {
        lock (_historyLock)
        {
            var count = Math.Clamp(limit, 1, MaxHistory);
            return _history.Reverse().Take(count).ToList();
        }
    }

    public void ClearHistory()
    {
        lock (_historyLock)
        {
            _history.Clear();
        }
    }

    public void ResetSession()
    {
        lock (_sessionLock)
        {
            ResetSessionInternal();
        }
    }

    private void ResetSessionInternal()
    {
        _engine?.Dispose();
        _engine = null;
        _outputBuffer?.Clear();
    }

    private void EnsureEngineInitialized(int timeoutSeconds)
    {
        if (_engine != null && _currentTimeoutSeconds == timeoutSeconds)
        {
            return;
        }

        _engine?.Dispose();
        _currentTimeoutSeconds = timeoutSeconds;
        _outputBuffer = new StringBuilder();

        _engine = new Engine(options =>
        {
            options.LimitMemory(MemoryLimitBytes);
            options.TimeoutInterval(TimeSpan.FromSeconds(timeoutSeconds));
            options.LimitRecursion(128);
            options.AllowClr();
        });

        // 1. console.log -> captures into output buffer
        var consoleContext = new ReplConsoleContext(_outputBuffer);
        _engine.SetValue("console", consoleContext);

        // 2. services (IServiceProvider reference or service resolver function)
        var resolver = new ReplServicesResolver(_serviceProvider);
        _engine.SetValue("services", resolver);
        _engine.SetValue("resolveService", new Func<string, object>(resolver.Resolve));
        _engine.SetValue("getService", new Func<string, object>(resolver.Resolve));

        // 3. torrents (ITorrentService)
        var torrents = ResolveTorrents();
        _engine.SetValue("torrents", torrents);

        // 4. engine (IDownloadEngine / ITorrentEngineManager)
        var engine = ResolveEngine();
        _engine.SetValue("engine", engine);

        // 5. config (IConfigService / IConfigFileProvider)
        var config = ResolveConfig();
        _engine.SetValue("config", config);

        // 6. db (IMainDatabase)
        var db = ResolveDatabase();
        _engine.SetValue("db", db);
    }

    private ITorrentService ResolveTorrents()
    {
        return _torrentService
            ?? (_serviceProvider?.GetService(typeof(ITorrentService)) as ITorrentService);
    }

    private object ResolveEngine()
    {
        return _downloadEngine
            ?? (object)_torrentEngineManager
            ?? _serviceProvider?.GetService(typeof(IDownloadEngine))
            ?? _serviceProvider?.GetService(typeof(ITorrentEngineManager));
    }

    private object ResolveConfig()
    {
        return _configService
            ?? (object)_configFileProvider
            ?? _serviceProvider?.GetService(typeof(IConfigService))
            ?? _serviceProvider?.GetService(typeof(IConfigFileProvider));
    }

    private IMainDatabase ResolveDatabase()
    {
        return _mainDatabase
            ?? (_serviceProvider?.GetService(typeof(IMainDatabase)) as IMainDatabase);
    }

    private void SetCompletionResult(ReplExecutionResponse response, JsValue completion)
    {
        if (completion == null || completion.IsUndefined())
        {
            response.ResultType = "undefined";
            response.ResultJson = null;
        }
        else if (completion.IsNull())
        {
            response.ResultType = "null";
            response.ResultJson = "null";
        }
        else if (completion.IsBoolean())
        {
            response.ResultType = "boolean";
            response.ResultJson = completion.AsBoolean() ? "true" : "false";
        }
        else if (completion.IsNumber())
        {
            response.ResultType = "number";
            response.ResultJson = completion.AsNumber().ToString(CultureInfo.InvariantCulture);
        }
        else if (completion.IsString())
        {
            response.ResultType = "string";
            response.ResultJson = JsonSerializer.Serialize(completion.AsString());
        }
        else if (completion.IsArray())
        {
            response.ResultType = "array";
            response.ResultJson = SerializeJsValue(completion);
        }
        else if (completion.IsObject())
        {
            var obj = completion.AsObject();
            response.ResultType = obj.GetType().Name.Contains("Function", StringComparison.OrdinalIgnoreCase) ? "function" : "object";
            response.ResultJson = SerializeJsValue(completion);
        }
        else
        {
            response.ResultType = completion.Type.ToString().ToLowerInvariant();
            response.ResultJson = SerializeJsValue(completion);
        }
    }

    private string SerializeJsValue(JsValue value)
    {
        try
        {
            var clrObject = value.ToObject();
            if (clrObject == null)
            {
                return "null";
            }

            return JsonSerializer.Serialize(clrObject, JsonSerializationOptions);
        }
        catch
        {
            try
            {
                return JsonSerializer.Serialize(value.ToString());
            }
            catch
            {
                return $"\"{value.ToString().Replace("\"", "\\\"")}\"";
            }
        }
    }

    private void RecordHistory(string code, string language, bool success, long durationMs)
    {
        var entry = new ReplHistoryEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = code ?? string.Empty,
            Language = language ?? "javascript",
            Success = success,
            ExecutedAtUtc = DateTime.UtcNow,
            DurationMs = durationMs,
        };

        lock (_historyLock)
        {
            _history.AddLast(entry);
            while (_history.Count > MaxHistory)
            {
                _history.RemoveFirst();
            }
        }
    }
}

public class ReplConsoleContext
{
    private readonly StringBuilder _buffer;

    public ReplConsoleContext(StringBuilder buffer)
    {
        _buffer = buffer;
    }

    public void log(params object[] args) => Append(null, args);

    public void info(params object[] args) => Append("INFO", args);

    public void warn(params object[] args) => Append("WARN", args);

    public void error(params object[] args) => Append("ERROR", args);

    private void Append(string prefix, object[] args)
    {
        if (args == null || args.Length == 0)
        {
            _buffer.AppendLine();
            return;
        }

        var line = new StringBuilder();
        if (!string.IsNullOrEmpty(prefix))
        {
            line.Append($"[{prefix}] ");
        }

        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                line.Append(' ');
            }

            var arg = args[i];
            if (arg is JsValue jsVal)
            {
                line.Append(FormatJsValue(jsVal));
            }
            else
            {
                line.Append(arg?.ToString() ?? "null");
            }
        }

        _buffer.AppendLine(line.ToString());
    }

    private static string FormatJsValue(JsValue val)
    {
        if (val.IsUndefined())
        {
            return "undefined";
        }

        if (val.IsNull())
        {
            return "null";
        }

        if (val.IsString())
        {
            return val.AsString();
        }

        return val.ToString();
    }
}

public class ReplServicesResolver
{
    private readonly IServiceProvider _serviceProvider;

    public ReplServicesResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object GetService(Type type)
    {
        return _serviceProvider?.GetService(type);
    }

    public object get(string serviceName) => Resolve(serviceName);

    public object resolve(string serviceName) => Resolve(serviceName);

    public object Resolve(string serviceName)
    {
        if (_serviceProvider == null || string.IsNullOrWhiteSpace(serviceName))
        {
            return null;
        }

        var directType = Type.GetType(serviceName, false, true);
        if (directType != null)
        {
            return _serviceProvider.GetService(directType);
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var match = assembly.GetTypes().FirstOrDefault(t =>
                    string.Equals(t.Name, serviceName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.FullName, serviceName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Name, "I" + serviceName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    var resolved = _serviceProvider.GetService(match);
                    if (resolved != null)
                    {
                        return resolved;
                    }
                }
            }
            catch
            {
                // Ignore load exceptions from dynamic assemblies
            }
        }

        return null;
    }
}
