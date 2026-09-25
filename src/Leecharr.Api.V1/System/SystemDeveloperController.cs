// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Developer;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Api.V1.System;

[V1ApiController("system/developer")]
public class SystemDeveloperController : Controller
{
    private static readonly HashSet<string> BaseCommandProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id", "Name", "Message", "Body", "Priority", "Status", "QueuedAt", "StartedAt", "EndedAt",
        "Duration", "Trigger", "SuppressMessages", "SendUpdatesToClient", "CompletionMessage",
        "RequiresDiskAccess", "IsLongRunning", "LastExecutionTime",
    };

    private static readonly HashSet<string> SensitiveKeySubstrings = new(StringComparer.OrdinalIgnoreCase)
    {
        "key", "password", "secret", "token", "credential", "auth", "hash",
    };

    private readonly IDeveloperEventStore eventStore;
    private readonly IDeveloperHttpTrafficStore httpTrafficStore;
    private readonly IDeveloperWebhookStore webhookStore;
    private readonly IManageCommandQueue commandQueue;
    private readonly ICommandRepository commandRepository;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly IMainDatabase mainDatabase;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    public SystemDeveloperController(
        IDeveloperEventStore eventStore = null,
        IDeveloperHttpTrafficStore httpTrafficStore = null,
        IDeveloperWebhookStore webhookStore = null,
        IManageCommandQueue commandQueue = null,
        ICommandRepository commandRepository = null,
        IConfigService configService = null,
        IConfigFileProvider configFileProvider = null,
        IMainDatabase mainDatabase = null,
        IEventAggregator eventAggregator = null)
    {
        this.eventStore = eventStore;
        this.httpTrafficStore = httpTrafficStore;
        this.webhookStore = webhookStore;
        this.commandQueue = commandQueue;
        this.commandRepository = commandRepository;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.mainDatabase = mainDatabase;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    // ==========================================
    // 1. EVENT BUS WIRETAP
    // ==========================================

    [HttpGet("events")]
    public ActionResult<DeveloperEventsResponse> GetEvents([FromQuery] int limit = 100, [FromQuery] string search = null)
    {
        if (this.eventStore == null)
        {
            return new DeveloperEventsResponse();
        }

        var list = this.eventStore.GetRecentEvents(limit, search);
        return new DeveloperEventsResponse
        {
            Events = list.Select(e => new DeveloperEventItem
            {
                Id = e.Id,
                TimestampUtc = e.TimestampUtc,
                EventName = e.EventName,
                EventType = e.EventType,
                SourceNamespace = e.SourceNamespace,
                PayloadJson = e.PayloadJson,
            }).ToList(),
            TotalRecorded = list.Count,
        };
    }

    [HttpPost("events/publish")]
    public ActionResult<DeveloperEventItem> PublishSyntheticEvent([FromBody] DeveloperPublishEventRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.EventName))
        {
            return this.BadRequest("EventName cannot be empty.");
        }

        var item = new DeveloperEventEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            EventName = request.EventName,
            EventType = $"Synthetic.{request.EventName}",
            SourceNamespace = "Leecharr.Developer.Synthetic",
            PayloadJson = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson,
        };

        this.eventStore?.RecordEvent(new DeveloperSyntheticEvent(request.EventName, item.PayloadJson));

        return this.Ok(new DeveloperEventItem
        {
            Id = item.Id,
            TimestampUtc = item.TimestampUtc,
            EventName = item.EventName,
            EventType = item.EventType,
            SourceNamespace = item.SourceNamespace,
            PayloadJson = item.PayloadJson,
        });
    }

    [HttpDelete("events")]
    public IActionResult ClearEvents()
    {
        this.eventStore?.Clear();
        return this.Ok(new { message = "Developer event store cleared." });
    }

    // ==========================================
    // 2. COMMAND CONSOLE & BACKGROUND TASKS
    // ==========================================

    [HttpGet("commands")]
    public ActionResult<DeveloperCommandsResponse> GetCommands()
    {
        var commandTypes = new List<Type>();
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && (a.FullName?.Contains("Leecharr") == true || a.FullName?.Contains("NzbDrone") == true || a.FullName?.Contains("Seedarr") == true));

            foreach (var asm in assemblies)
            {
                try
                {
                    var types = asm.GetTypes()
                        .Where(t => typeof(Command).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);
                    commandTypes.AddRange(types);
                }
                catch
                {
                    // Ignore reflection type loading errors for non-matching modules
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error scanning assemblies for commands");
        }

        var descriptors = commandTypes
            .DistinctBy(t => t.Name)
            .OrderBy(t => t.Name)
            .Select(t =>
            {
                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => !BaseCommandProperties.Contains(p.Name) && p.CanWrite)
                    .Select(p => new DeveloperCommandProperty
                    {
                        Name = p.Name,
                        Type = Nullable.GetUnderlyingType(p.PropertyType)?.Name ?? p.PropertyType.Name,
                        IsNullable = Nullable.GetUnderlyingType(p.PropertyType) != null || !p.PropertyType.IsValueType,
                        DefaultValue = null,
                    }).ToList();

                return new DeveloperCommandDescriptor
                {
                    Name = t.Name,
                    FullName = t.FullName,
                    Description = FormatCommandDescription(t.Name),
                    Properties = props,
                };
            }).ToList();

        var recentHistory = new List<DeveloperCommandHistoryItem>();
        if (this.commandRepository != null)
        {
            try
            {
                var recentModels = this.commandRepository.GetRecent(50) ?? Enumerable.Empty<CommandModel>();
                recentHistory = recentModels.Select(m =>
                {
                    var dur = (m.EndedAt ?? DateTime.UtcNow) - (m.StartedAt ?? m.QueuedAt);
                    return new DeveloperCommandHistoryItem
                    {
                        Id = m.Id,
                        Name = m.Name,
                        Status = m.Status.ToString(),
                        QueuedAt = m.QueuedAt,
                        StartedAt = m.StartedAt,
                        EndedAt = m.EndedAt,
                        DurationMs = Math.Round(dur.TotalMilliseconds, 1),
                        Message = m.Message,
                        Trigger = m.Trigger,
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to load recent command history");
            }
        }

        return new DeveloperCommandsResponse
        {
            Commands = descriptors,
            RecentHistory = recentHistory,
        };
    }

    [HttpPost("commands/execute")]
    public ActionResult<DeveloperCommandHistoryItem> ExecuteCommand([FromBody] DeveloperCommandExecuteRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CommandName))
        {
            return this.BadRequest("CommandName must be provided.");
        }

        if (this.commandQueue == null)
        {
            return this.StatusCode(503, "Command queue manager is not available.");
        }

        try
        {
            var bodyJson = request.Parameters != null && request.Parameters.Count > 0
                ? JsonSerializer.Serialize(request.Parameters)
                : "{}";

            var model = this.commandQueue.PushRaw(request.CommandName, bodyJson, CommandTrigger.Manual);

            return this.Ok(new DeveloperCommandHistoryItem
            {
                Id = model.Id,
                Name = model.Name,
                Status = model.Status.ToString(),
                QueuedAt = model.QueuedAt,
                StartedAt = model.StartedAt,
                EndedAt = model.EndedAt,
                DurationMs = 0,
                Message = model.Message,
                Trigger = model.Trigger,
            });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to dispatch command {0}", request.CommandName);
            return this.BadRequest($"Failed to dispatch command: {ex.Message}");
        }
    }

    // ==========================================
    // 3. HTTP / OUTGOING NETWORK WIRETAP
    // ==========================================

    [HttpGet("network")]
    public ActionResult<DeveloperHttpTrafficResponse> GetHttpTraffic([FromQuery] int limit = 100, [FromQuery] string search = null)
    {
        if (this.httpTrafficStore == null)
        {
            return new DeveloperHttpTrafficResponse();
        }

        var items = this.httpTrafficStore.GetRecent(limit, search);
        return new DeveloperHttpTrafficResponse
        {
            Items = items.Select(t => new DeveloperHttpTrafficItem
            {
                Id = t.Id,
                TimestampUtc = t.TimestampUtc,
                Method = t.Method,
                Url = t.Url,
                Host = t.Host,
                StatusCode = t.StatusCode,
                DurationMs = t.DurationMs,
                IsSuccess = t.IsSuccess,
                TransportEngine = t.TransportEngine,
                RequestHeaders = t.RequestHeaders,
                ResponseHeaders = t.ResponseHeaders,
                ResponseBodyPreview = t.ResponseBodyPreview,
                ErrorMessage = t.ErrorMessage,
            }).ToList(),
            TotalRecorded = items.Count,
        };
    }

    [HttpDelete("network")]
    public IActionResult ClearHttpTraffic()
    {
        this.httpTrafficStore?.Clear();
        return this.Ok(new { message = "Developer HTTP traffic buffer cleared." });
    }

    // ==========================================
    // 4. ARR WEBHOOK SANDBOX & SIMULATOR
    // ==========================================

    [HttpGet("webhooks/templates")]
    public ActionResult<List<DeveloperWebhookTemplate>> GetWebhookTemplates()
    {
        return new List<DeveloperWebhookTemplate>
        {
            new()
            {
                Id = "sonarr-grab",
                Name = "Sonarr Episode Grab",
                Source = "Sonarr",
                EventType = "Grab",
                Description = "Dispatched when Sonarr sends a release to the download client.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Grab",
                    series = new { id = 42, title = "Breaking Bad", tvdbId = 81189, imdbId = "tt0903747", path = "/series/Breaking Bad" },
                    episodes = new[] { new { id = 101, episodeNumber = 1, seasonNumber = 1, title = "Pilot" } },
                    release = new { releaseTitle = "Breaking.Bad.S01E01.1080p.BluRay.x264-ROVERS", indexer = "Prowlarr", size = 1532918272 },
                    downloadClient = "Leecharr",
                    downloadId = "7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "sonarr-download",
                Name = "Sonarr Download Complete",
                Source = "Sonarr",
                EventType = "Download",
                Description = "Dispatched when Sonarr completes importing an episode file.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Download",
                    series = new { id = 42, title = "Breaking Bad", tvdbId = 81189 },
                    episodes = new[] { new { id = 101, episodeNumber = 1, seasonNumber = 1, title = "Pilot" } },
                    episodeFile = new { id = 201, relativePath = "Season 01/Breaking.Bad.S01E01.Pilot.mkv", path = "/series/Breaking Bad/Season 01/Breaking.Bad.S01E01.Pilot.mkv", quality = "1080p HDTV", size = 1532918272 },
                    downloadId = "7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "radarr-grab",
                Name = "Radarr Movie Grab",
                Source = "Radarr",
                EventType = "Grab",
                Description = "Dispatched when Radarr grabs a movie torrent.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Grab",
                    movie = new { id = 77, title = "Inception", year = 2010, tmdbId = 27205, imdbId = "tt1375666" },
                    release = new { releaseTitle = "Inception.2010.1080p.BluRay.x264.DTS-WiKi", indexer = "Prowlarr", size = 12884901888L },
                    downloadClient = "Leecharr",
                    downloadId = "8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "radarr-download",
                Name = "Radarr Movie Download",
                Source = "Radarr",
                EventType = "Download",
                Description = "Dispatched when Radarr finishes importing a movie file.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Download",
                    movie = new { id = 77, title = "Inception", year = 2010, tmdbId = 27205 },
                    movieFile = new { id = 301, relativePath = "Inception.2010.1080p.mkv", path = "/movies/Inception (2010)/Inception.2010.1080p.mkv", size = 12884901888L },
                    downloadId = "8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "prowlarr-test",
                Name = "Prowlarr Integration Test",
                Source = "Prowlarr",
                EventType = "Test",
                Description = "Test probe webhook from Prowlarr indexer connection.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Test",
                    instanceName = "Prowlarr",
                    message = "Testing connection between Prowlarr and Leecharr",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
        };
    }

    [HttpGet("webhooks/history")]
    public ActionResult<List<DeveloperWebhookHistoryItem>> GetWebhookHistory([FromQuery] int limit = 50)
    {
        if (this.webhookStore == null)
        {
            return new List<DeveloperWebhookHistoryItem>();
        }

        var items = this.webhookStore.GetRecent(limit);
        return items.Select(w => new DeveloperWebhookHistoryItem
        {
            Id = w.Id,
            TimestampUtc = w.TimestampUtc,
            Source = w.Source,
            EventType = w.EventType,
            SourceIp = w.SourceIp,
            UserAgent = w.UserAgent,
            StatusCode = w.StatusCode,
            RawPayload = w.RawPayload,
            ResultMessage = w.ResultMessage,
            Success = w.Success,
        }).ToList();
    }

    [HttpPost("webhooks/simulate")]
    public ActionResult<DeveloperWebhookSimulateResponse> SimulateWebhook([FromBody] DeveloperWebhookSimulateRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            return this.BadRequest("PayloadJson cannot be empty.");
        }

        var sw = Stopwatch.StartNew();
        var logs = new List<string>();

        try
        {
            logs.Add($"[Simulator] Parsing inbound payload for EventType='{request.EventType}'...");
            using var doc = JsonDocument.Parse(request.PayloadJson);
            var root = doc.RootElement;

            var detectedEvent = request.EventType;
            if (root.TryGetProperty("eventType", out var evProp))
            {
                detectedEvent = evProp.GetString() ?? detectedEvent;
            }

            logs.Add($"[Simulator] Detected EventType: {detectedEvent}");

            if (root.TryGetProperty("series", out var seriesProp))
            {
                var title = seriesProp.TryGetProperty("title", out var tp) ? tp.GetString() : "Unknown";
                logs.Add($"[Simulator] Identified TV Series context: '{title}'");
            }
            else if (root.TryGetProperty("movie", out var movieProp))
            {
                var title = movieProp.TryGetProperty("title", out var tp) ? tp.GetString() : "Unknown";
                logs.Add($"[Simulator] Identified Movie context: '{title}'");
            }

            if (root.TryGetProperty("release", out var relProp))
            {
                var releaseTitle = relProp.TryGetProperty("releaseTitle", out var rp) ? rp.GetString() : "Unknown";
                logs.Add($"[Simulator] Matched Release Title: '{releaseTitle}'");
            }

            sw.Stop();
            logs.Add($"[Simulator] Simulated execution completed successfully in {sw.Elapsed.TotalMilliseconds:F2}ms.");

            this.webhookStore?.Record(
                "Simulator",
                detectedEvent,
                "127.0.0.1",
                "Leecharr-Developer-Sandbox/1.0",
                200,
                request.PayloadJson,
                $"Successfully processed {detectedEvent} simulation",
                true);

            return this.Ok(new DeveloperWebhookSimulateResponse
            {
                Success = true,
                StatusCode = 200,
                Message = $"Webhook '{detectedEvent}' simulation parsed and verified successfully.",
                ExecutionTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                TraceLogs = logs,
            });
        }
        catch (JsonException jex)
        {
            sw.Stop();
            logs.Add($"[Simulator] JSON Syntax Error: {jex.Message}");
            return this.BadRequest(new DeveloperWebhookSimulateResponse
            {
                Success = false,
                StatusCode = 400,
                Message = $"Invalid JSON payload: {jex.Message}",
                ExecutionTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                TraceLogs = logs,
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            logs.Add($"[Simulator] Execution Error: {ex.Message}");
            return this.StatusCode(500, new DeveloperWebhookSimulateResponse
            {
                Success = false,
                StatusCode = 500,
                Message = $"Simulation failure: {ex.Message}",
                ExecutionTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                TraceLogs = logs,
            });
        }
    }

    // ==========================================
    // 5. CONFIGURATION & ENVIRONMENT MATRIX
    // ==========================================

    [HttpGet("config")]
    public ActionResult<DeveloperConfigResponse> GetConfiguration([FromQuery] bool unmask = false)
    {
        var entries = new List<DeveloperConfigEntry>();

        // 1. Config Database Table
        if (this.mainDatabase != null)
        {
            try
            {
                using var conn = this.mainDatabase.OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Key, Value FROM Config ORDER BY Key;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var k = reader.GetString(0);
                    var v = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    var isSecret = IsSensitiveKey(k);
                    entries.Add(new DeveloperConfigEntry
                    {
                        Key = k,
                        Value = isSecret && !unmask ? MaskSecret(v) : v,
                        Source = "Database",
                        IsSecret = isSecret,
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to read Config table for developer view");
            }
        }

        // 2. ConfigFileProvider properties
        if (this.configFileProvider != null)
        {
            try
            {
                var props = this.configFileProvider.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var p in props)
                {
                    if (p.CanRead && p.GetIndexParameters().Length == 0)
                    {
                        var val = p.GetValue(this.configFileProvider)?.ToString() ?? string.Empty;
                        var isSecret = IsSensitiveKey(p.Name);
                        if (!entries.Any(e => e.Key.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            entries.Add(new DeveloperConfigEntry
                            {
                                Key = p.Name,
                                Value = isSecret && !unmask ? MaskSecret(val) : val,
                                Source = "ConfigFile",
                                IsSecret = isSecret,
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to read ConfigFileProvider for developer view");
            }
        }

        // 3. Environment Variables (Leecharr / Seedarr / ASPNETCORE / DOTNET)
        var envVars = new Dictionary<string, string>();
        foreach (DictionaryEntry de in Environment.GetEnvironmentVariables())
        {
            var k = de.Key?.ToString() ?? string.Empty;
            var v = de.Value?.ToString() ?? string.Empty;
            if (k.StartsWith("LEECHARR", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("SEEDARR", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("DOTNET", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("ASPNETCORE", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("PUID", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("PGID", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("TZ", StringComparison.OrdinalIgnoreCase))
            {
                var isSecret = IsSensitiveKey(k);
                envVars[k] = isSecret && !unmask ? MaskSecret(v) : v;
            }
        }

        var proc = Process.GetCurrentProcess();
        var hostEnv = new DeveloperHostEnvironment
        {
            OperatingSystem = global::System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            OsArchitecture = global::System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = global::System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            FrameworkDescription = global::System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            HostName = Environment.MachineName,
            ProcessId = proc.Id,
            ProcessStartTimeUtc = proc.StartTime.ToUniversalTime(),
            ProcessUptimeSeconds = Math.Round((DateTime.UtcNow - proc.StartTime.ToUniversalTime()).TotalSeconds, 1),
            WorkingSetBytes = proc.WorkingSet64,
            AppDataDirectory = Environment.GetEnvironmentVariable("LEECHARR__APP_DATA") ?? Environment.GetEnvironmentVariable("SEEDARR__APP_DATA") ?? "/config",
            TempDirectory = Path.GetTempPath(),
            CurrentDirectory = Directory.GetCurrentDirectory(),
            EnvironmentVariables = envVars,
        };

        return new DeveloperConfigResponse
        {
            Entries = entries.OrderBy(e => e.Key).ToList(),
            Environment = hostEnv,
        };
    }

    private static string FormatCommandDescription(string name)
    {
        if (name.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - 7);
        }

        return global::System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
    }

    private static bool IsSensitiveKey(string key)
    {
        return SensitiveKeySubstrings.Any(s => key.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    private static string MaskSecret(string val)
    {
        if (string.IsNullOrEmpty(val))
        {
            return string.Empty;
        }

        if (val.Length <= 4)
        {
            return "****";
        }

        return string.Concat(val.AsSpan(0, 2), "****", val.AsSpan(val.Length - 2));
    }
}

public class DeveloperSyntheticEvent : IEvent
{
    public string Name { get; }

    public string Payload { get; }

    public DeveloperSyntheticEvent(string name, string payload)
    {
        this.Name = name;
        this.Payload = payload;
    }
}
