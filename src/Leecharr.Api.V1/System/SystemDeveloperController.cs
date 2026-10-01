// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Leecharr.Api.V1.Webhooks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
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

    private static readonly Regex CommandNameRegex = new(
        "([a-z])([A-Z])",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private readonly IDeveloperEventStore eventStore;
    private readonly IDeveloperHttpTrafficStore httpTrafficStore;
    private readonly IDeveloperWebhookStore webhookStore;
    private readonly IManageCommandQueue commandQueue;
    private readonly ICommandRepository commandRepository;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly IMainDatabase mainDatabase;
    private readonly IEventAggregator eventAggregator;
    private readonly ArrWebhookController arrWebhookController;
    private readonly IServiceProvider serviceProvider;
    private readonly IAppFolderInfo appFolderInfo;
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
        IEventAggregator eventAggregator = null,
        ArrWebhookController arrWebhookController = null,
        IServiceProvider serviceProvider = null,
        IAppFolderInfo appFolderInfo = null)
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
        this.arrWebhookController = arrWebhookController;
        this.serviceProvider = serviceProvider;
        this.appFolderInfo = appFolderInfo;
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

        this.eventStore?.RecordEvent(item);
        this.eventAggregator?.PublishEvent(new DeveloperSyntheticEvent(request.EventName, item.PayloadJson));

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
                catch (Exception ex)
                {
                    this.logger.Trace(ex, "Failed to load command types from assembly {0}", asm.FullName);
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
                ? STJson.ToJson(request.Parameters)
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
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "ASP.NET Core MVC controller actions cannot be static")]
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
            new()
            {
                Id = "sonarr-rename",
                Name = "Sonarr File Rename",
                Source = "Sonarr",
                EventType = "Rename",
                Description = "Dispatched when Sonarr renames series media files on disk.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Rename",
                    series = new { id = 42, title = "Breaking Bad", tvdbId = 81189 },
                    renamedFiles = new[]
                    {
                        new { id = 201, previousRelativePath = "Season 1/ep1.mkv", relativePath = "Season 01/Breaking.Bad.S01E01.Pilot.mkv" },
                    },
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "radarr-delete",
                Name = "Radarr Movie Delete",
                Source = "Radarr",
                EventType = "MovieDelete",
                Description = "Dispatched when Radarr deletes a movie and its media files.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "MovieDelete",
                    movie = new { id = 77, title = "Inception", year = 2010 },
                    deletedFiles = true,
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "lidarr-grab",
                Name = "Lidarr Album Grab",
                Source = "Lidarr",
                EventType = "Grab",
                Description = "Dispatched when Lidarr grabs a music album release.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Grab",
                    artist = new { id = 12, name = "Daft Punk" },
                    album = new { id = 305, title = "Random Access Memories", releaseDate = "2013-05-17" },
                    release = new { releaseTitle = "Daft Punk - Random Access Memories (2013) [FLAC]", indexer = "Prowlarr", size = 524288000 },
                    downloadClient = "Leecharr",
                    downloadId = "9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "lidarr-download",
                Name = "Lidarr Track Download",
                Source = "Lidarr",
                EventType = "TrackDownload",
                Description = "Dispatched when Lidarr imports audio tracks.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "TrackDownload",
                    artist = new { id = 12, name = "Daft Punk" },
                    album = new { id = 305, title = "Random Access Memories" },
                    trackFiles = new[]
                    {
                        new { id = 501, path = "/music/Daft Punk/Random Access Memories/01 - Give Life Back to Music.flac", size = 41943040 },
                    },
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "readarr-grab",
                Name = "Readarr Book Grab",
                Source = "Readarr",
                EventType = "Grab",
                Description = "Dispatched when Readarr grabs an ebook or audiobook release.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Grab",
                    author = new { id = 9, name = "Frank Herbert" },
                    book = new { id = 88, title = "Dune", releaseDate = "1965-08-01" },
                    release = new { releaseTitle = "Frank Herbert - Dune [EPUB]", indexer = "Prowlarr", size = 2097152 },
                    downloadClient = "Leecharr",
                    downloadId = "0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "prowlarr-health",
                Name = "Prowlarr Indexer Health Warning",
                Source = "Prowlarr",
                EventType = "HealthIssue",
                Description = "Notification emitted when an indexer reports rate limits or network degradation.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "HealthIssue",
                    level = "Warning",
                    source = "IndexerCheck",
                    message = "Indexer 'PublicTracker' reported 503 Service Unavailable temporarily.",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "jellyseerr-request",
                Name = "Jellyseerr Media Request",
                Source = "Jellyseerr",
                EventType = "MediaRequest",
                Description = "Dispatched when a user requests a movie or series via Jellyseerr.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    notification_type = "MEDIA_PENDING",
                    @event = "New Media Request",
                    subject = "Inception (2010)",
                    requestedBy_username = "admin",
                    media = new { tmdbId = 27205, media_type = "movie", status = "PENDING" },
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "overseerr-approved",
                Name = "Overseerr Request Auto-Approved",
                Source = "Overseerr",
                EventType = "MediaApproved",
                Description = "Emitted when a media request is automatically approved for download.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    notification_type = "MEDIA_APPROVED",
                    @event = "Media Request Approved",
                    subject = "Interstellar (2014)",
                    media = new { tmdbId = 157336, media_type = "movie", status = "PROCESSING" },
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "discord-webhook",
                Name = "Discord Webhook Notification",
                Source = "Discord",
                EventType = "Notification",
                Description = "Standard Discord webhook embed format for notifications.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    username = "Leecharr Bot",
                    avatar_url = "https://leecharr.net/favicon.png",
                    embeds = new[]
                    {
                        new
                        {
                            title = "Download Completed: Ubuntu-24.04-live-server.iso",
                            description = "Torrent successfully completed and verified.",
                            color = 5814783,
                            fields = new[]
                            {
                                new { name = "Size", value = "2.6 GB", inline = true },
                                new { name = "Ratio", value = "1.52", inline = true },
                            },
                        },
                    },
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "slack-webhook",
                Name = "Slack Channel Alert",
                Source = "Slack",
                EventType = "Alert",
                Description = "Slack incoming webhook message payload with block formatting.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    text = "Leecharr Alert: Torrent Download Finished",
                    blocks = new object[]
                    {
                        new { type = "section", text = new { type = "mrkdwn", text = "*Download Finished*: `Debian-12-netinst.iso` (750 MB)" } },
                    },
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "generic-torrent-completed",
                Name = "Generic Torrent Completed Webhook",
                Source = "Generic",
                EventType = "TorrentCompleted",
                Description = "Generic JSON payload dispatched on download completion.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "TorrentCompleted",
                    torrentId = 101,
                    infoHash = "b123456789abcdef0123456789abcdef01234567",
                    name = "Fedora-Workstation-Live-40.iso",
                    sizeBytes = 2147483648L,
                    savePath = "/downloads/isos",
                    completedAtUtc = "2026-09-28T20:30:00Z",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "whisparr-grab",
                Name = "Whisparr Media Release Grab",
                Source = "Whisparr",
                EventType = "Grab",
                Description = "Dispatched when Whisparr sends an adult media release to the download client.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Grab",
                    site = new { id = 15, name = "StudioX", url = "https://studiox.example.com" },
                    movie = new { id = 401, title = "Summer Vacation", releaseDate = "2024-06-15" },
                    release = new { releaseTitle = "Summer.Vacation.1080p.MP4", indexer = "Prowlarr", size = 2147483648L },
                    downloadClient = "Leecharr",
                    downloadId = "1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "whisparr-download",
                Name = "Whisparr Media File Imported",
                Source = "Whisparr",
                EventType = "Download",
                Description = "Dispatched when Whisparr successfully imports an adult media file.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Download",
                    movie = new { id = 401, title = "Summer Vacation" },
                    movieFile = new { id = 801, path = "/adult/Summer Vacation/Summer Vacation (2024).mp4", size = 2147483648L },
                    downloadId = "1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "bazarr-subtitle",
                Name = "Bazarr Subtitle Download Complete",
                Source = "Bazarr",
                EventType = "SubtitleDownload",
                Description = "Dispatched when Bazarr retrieves and applies a new subtitle file.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "SubtitleDownload",
                    mediaType = "episode",
                    title = "Breaking Bad - S01E01 - Pilot",
                    language = "en",
                    provider = "OpenSubtitles",
                    score = 98.5,
                    subtitlePath = "/series/Breaking Bad/Season 01/Breaking.Bad.S01E01.Pilot.en.srt",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "bazarr-sync",
                Name = "Bazarr Subtitle Verification Sync",
                Source = "Bazarr",
                EventType = "SubtitleSync",
                Description = "Emitted when Bazarr completes audio-sync alignment for subtitles.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "SubtitleSync",
                    mediaType = "movie",
                    title = "Inception (2010)",
                    language = "en",
                    appliedOffsetMs = 450,
                    syncMethod = "ffsubsync",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "prowlarr-sync",
                Name = "Prowlarr App Sync Complete",
                Source = "Prowlarr",
                EventType = "AppSync",
                Description = "Dispatched when Prowlarr pushes updated tracker definitions to Leecharr.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "AppSync",
                    client = "Leecharr",
                    syncAction = "UpdateTrackers",
                    syncedIndexers = new[] { "1337x", "EZTV", "Nyaa", "TorrentGalaxy" },
                    totalIndexers = 4,
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "prowlarr-indexer-down",
                Name = "Prowlarr Indexer Down Circuit Breaker",
                Source = "Prowlarr",
                EventType = "IndexerFailure",
                Description = "Emitted when Prowlarr temporarily disables a failing tracker.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "IndexerFailure",
                    indexer = "PublicFlakyTracker",
                    failureReason = "Consecutive HTTP 504 Gateway Timeout errors (threshold: 5)",
                    disabledUntilUtc = DateTime.UtcNow.AddMinutes(15).ToString("o"),
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "sonarr-series-add",
                Name = "Sonarr Series Monitored",
                Source = "Sonarr",
                EventType = "SeriesAdd",
                Description = "Dispatched when a new television show is added to Sonarr.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "SeriesAdd",
                    series = new { id = 88, title = "Severance", tvdbId = 371980, year = 2022, path = "/series/Severance" },
                    monitored = true,
                    seasonCount = 1,
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "sonarr-health",
                Name = "Sonarr Storage Health Alert",
                Source = "Sonarr",
                EventType = "HealthIssue",
                Description = "Emitted when Sonarr detects root storage or path permission issues.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "HealthIssue",
                    level = "Error",
                    source = "RootFolderCheck",
                    message = "Root folder '/series' is currently inaccessible or read-only.",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "sonarr-upgrade",
                Name = "Sonarr File Quality Upgrade",
                Source = "Sonarr",
                EventType = "Upgrade",
                Description = "Dispatched when an existing episode file is replaced with higher quality.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Upgrade",
                    series = new { id = 42, title = "Breaking Bad" },
                    episode = new { seasonNumber = 1, episodeNumber = 1, title = "Pilot" },
                    previousQuality = "720p HDTV",
                    newQuality = "1080p BluRay Remux",
                    sizeDifferenceBytes = 2849182720L,
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "radarr-movie-add",
                Name = "Radarr Movie Monitored",
                Source = "Radarr",
                EventType = "MovieAdd",
                Description = "Dispatched when a new movie is added to Radarr's monitoring list.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "MovieAdd",
                    movie = new { id = 105, title = "Dune: Part Two", year = 2024, tmdbId = 693134, path = "/movies/Dune Part Two (2024)" },
                    monitored = true,
                    minimumAvailability = "Released",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "radarr-health",
                Name = "Radarr Disk Space Alert",
                Source = "Radarr",
                EventType = "HealthIssue",
                Description = "Emitted when target storage volume free capacity is critically low.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "HealthIssue",
                    level = "Warning",
                    source = "DiskSpaceCheck",
                    message = "Drive '/movies' has less than 15 GB of free storage remaining.",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "radarr-upgrade",
                Name = "Radarr Movie Quality Upgrade",
                Source = "Radarr",
                EventType = "Upgrade",
                Description = "Dispatched when a movie file is replaced with a higher-definition release.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Upgrade",
                    movie = new { id = 77, title = "Inception", year = 2010 },
                    previousQuality = "1080p WebDL",
                    newQuality = "2160p UHD HDR Remux",
                    sizeDifferenceBytes = 41284901888L,
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "lidarr-artist-add",
                Name = "Lidarr Artist Monitored",
                Source = "Lidarr",
                EventType = "ArtistAdd",
                Description = "Dispatched when a music artist is added to Lidarr.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "ArtistAdd",
                    artist = new { id = 44, name = "Pink Floyd", mbId = "83d91898-7763-47d7-b03b-b92132375c47" },
                    monitored = true,
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "readarr-download",
                Name = "Readarr Book Imported",
                Source = "Readarr",
                EventType = "Download",
                Description = "Dispatched when Readarr organizes and stores a completed book release.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "Download",
                    author = new { id = 9, name = "Frank Herbert" },
                    book = new { id = 88, title = "Dune" },
                    bookFile = new { id = 402, path = "/books/Frank Herbert/Dune (1965)/Dune.epub", format = "EPUB", size = 2097152 },
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "jellyfin-playback",
                Name = "Jellyfin Playback Started",
                Source = "Jellyfin",
                EventType = "PlaybackStart",
                Description = "Emitted by Jellyfin media server when a client initiates streaming.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    NotificationType = "PlaybackStart",
                    ServerId = "jellyfin-srv-01",
                    ItemName = "Breaking Bad - S01E01 - Pilot",
                    ItemType = "Episode",
                    UserId = "user_42",
                    ClientName = "Jellyfin Web",
                    DeviceName = "Chrome Linux",
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "ntfy-publish",
                Name = "ntfy.sh Topic Notification",
                Source = "ntfy",
                EventType = "Publish",
                Description = "Standard HTTP POST notification format for ntfy.sh servers.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    topic = "leecharr_downloads",
                    title = "Download Complete: Arch Linux ISO",
                    message = "Torrent archlinux-2026.09.01-x86_64.iso finished downloading.",
                    priority = 3,
                    tags = new[] { "arrow_down", "package" },
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "gotify-push",
                Name = "Gotify Server Push Message",
                Source = "Gotify",
                EventType = "Message",
                Description = "Gotify self-hosted push notification message payload.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    title = "Leecharr Alert",
                    message = "Torrent download speed reached 85 MB/s.",
                    priority = 5,
                    extras = new
                    {
                        client = new { name = "Leecharr", version = "1.32.0" },
                    },
                }, new JsonSerializerOptions { WriteIndented = true }),
            },
            new()
            {
                Id = "pushover-alert",
                Name = "Pushover High Priority Alert",
                Source = "Pushover",
                EventType = "Alert",
                Description = "Pushover push notification API payload with emergency priority.",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    token = "a1b2c3d4e5f6g7h8",
                    user = "u1v2w3x4y5z6a7b8",
                    title = "Leecharr Kill Switch Engaged",
                    message = "VPN interface dropped. BitTorrent engine immediately halted to prevent IP leak.",
                    priority = 1,
                    sound = "siren",
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
    public async Task<ActionResult<DeveloperWebhookSimulateResponse>> SimulateWebhook([FromBody] DeveloperWebhookSimulateRequest request)
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
            if (root.TryGetProperty("eventType", out var evProp) && !string.IsNullOrWhiteSpace(evProp.GetString()))
            {
                detectedEvent = evProp.GetString();
            }

            logs.Add($"[Simulator] Detected EventType: {detectedEvent}");

            var detectedArrType = request.ArrType;
            if (string.IsNullOrWhiteSpace(detectedArrType) && root.TryGetProperty("instanceName", out var instProp) && !string.IsNullOrWhiteSpace(instProp.GetString()))
            {
                var inst = instProp.GetString();
                if (inst.Contains("Sonarr", StringComparison.OrdinalIgnoreCase))
                {
                    detectedArrType = "Sonarr";
                }
                else if (inst.Contains("Radarr", StringComparison.OrdinalIgnoreCase))
                {
                    detectedArrType = "Radarr";
                }
                else if (inst.Contains("Lidarr", StringComparison.OrdinalIgnoreCase))
                {
                    detectedArrType = "Lidarr";
                }
                else if (inst.Contains("Readarr", StringComparison.OrdinalIgnoreCase))
                {
                    detectedArrType = "Readarr";
                }
                else if (inst.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase))
                {
                    detectedArrType = "Prowlarr";
                }
            }

            if (root.TryGetProperty("series", out var seriesProp))
            {
                var title = seriesProp.TryGetProperty("title", out var tp) ? tp.GetString() : "Unknown";
                logs.Add($"[Simulator] Identified TV Series context: '{title}'");
                detectedArrType ??= "Sonarr";
            }
            else if (root.TryGetProperty("movie", out var movieProp))
            {
                var title = movieProp.TryGetProperty("title", out var tp) ? tp.GetString() : "Unknown";
                logs.Add($"[Simulator] Identified Movie context: '{title}'");
                detectedArrType ??= "Radarr";
            }
            else if (root.TryGetProperty("artist", out var artistProp))
            {
                var name = artistProp.TryGetProperty("name", out var np) ? np.GetString() : "Unknown";
                logs.Add($"[Simulator] Identified Music Artist context: '{name}'");
                detectedArrType ??= "Lidarr";
            }
            else if (root.TryGetProperty("author", out var authorProp))
            {
                var name = authorProp.TryGetProperty("name", out var np) ? np.GetString() : "Unknown";
                logs.Add($"[Simulator] Identified Book Author context: '{name}'");
                detectedArrType ??= "Readarr";
            }

            if (string.IsNullOrWhiteSpace(detectedArrType) &&
                (detectedEvent.StartsWith("Indexer", StringComparison.OrdinalIgnoreCase) ||
                 detectedEvent.Equals("Sync", StringComparison.OrdinalIgnoreCase) ||
                 detectedEvent.Equals("SyncAll", StringComparison.OrdinalIgnoreCase)))
            {
                detectedArrType = "Prowlarr";
            }

            if (root.TryGetProperty("release", out var relProp))
            {
                var releaseTitle = relProp.TryGetProperty("releaseTitle", out var rp) ? rp.GetString() : "Unknown";
                logs.Add($"[Simulator] Matched Release Title: '{releaseTitle}'");
            }

            var resolvedArrType = detectedArrType ?? "Arr";
            var shouldDispatch = request.DispatchToPipeline;
            var webhookController = this.GetWebhookController();

            var statusCode = 200;
            var isSuccess = true;
            string resultMessage;

            if (shouldDispatch && webhookController != null)
            {
                logs.Add($"[Simulator] Dispatching payload to ArrWebhookController pipeline (ArrType: '{resolvedArrType}')...");

                ArrWebhookPayload payload = null;
                try
                {
                    payload = JsonSerializer.Deserialize<ArrWebhookPayload>(
                        request.PayloadJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception pex)
                {
                    logs.Add($"[Simulator] Payload deserialization warning: {pex.Message}");
                }

                if (payload != null && string.IsNullOrWhiteSpace(payload.EventType))
                {
                    payload.EventType = detectedEvent;
                }

                var actionResult = await webhookController.ProcessWebhookAsync(
                    resolvedArrType,
                    payload,
                    rawPayload: request.PayloadJson,
                    clientIp: "127.0.0.1",
                    userAgent: "Leecharr-Developer-Sandbox/1.0",
                    recordReceipt: false);

                ArrWebhookResult pipelineResult = null;
                if (actionResult.Result is ObjectResult objResult)
                {
                    statusCode = objResult.StatusCode ?? 200;
                    pipelineResult = objResult.Value as ArrWebhookResult;
                }
                else if (actionResult.Result is StatusCodeResult statusResult)
                {
                    statusCode = statusResult.StatusCode;
                }
                else if (actionResult.Value != null)
                {
                    pipelineResult = actionResult.Value;
                    statusCode = 200;
                }

                isSuccess = statusCode >= 200 && statusCode < 300 && (pipelineResult == null || pipelineResult.Success);
                if (pipelineResult != null)
                {
                    logs.Add($"[Simulator] Pipeline execution completed: Success={pipelineResult.Success}, Message='{pipelineResult.Message}', Updated={pipelineResult.Updated}");
                    if (pipelineResult.TorrentId != null)
                    {
                        logs.Add($"[Simulator] Matched Torrent ID: {pipelineResult.TorrentId}, InfoHash: {pipelineResult.InfoHash}");
                    }

                    resultMessage = $"Webhook '{detectedEvent}' simulation parsed and processed through pipeline: {pipelineResult.Message}";
                }
                else
                {
                    logs.Add($"[Simulator] Pipeline finished with HTTP status {statusCode}");
                    resultMessage = $"Webhook '{detectedEvent}' simulation pipeline completed with status {statusCode}.";
                }
            }
            else
            {
                if (!shouldDispatch)
                {
                    logs.Add("[Simulator] Pipeline dispatch skipped per request configuration.");
                }
                else
                {
                    logs.Add("[Simulator] ArrWebhookController is not registered; running in standalone verification mode.");
                }

                resultMessage = $"Webhook '{detectedEvent}' simulation parsed and verified successfully.";
            }

            sw.Stop();
            logs.Add($"[Simulator] Simulated execution completed in {sw.Elapsed.TotalMilliseconds:F2}ms.");

            this.webhookStore?.Record(
                "Simulator",
                detectedEvent,
                "127.0.0.1",
                "Leecharr-Developer-Sandbox/1.0",
                statusCode,
                request.PayloadJson,
                resultMessage,
                isSuccess);

            var response = new DeveloperWebhookSimulateResponse
            {
                Success = isSuccess,
                StatusCode = statusCode,
                Message = resultMessage,
                ExecutionTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                TraceLogs = logs,
            };

            return statusCode == 200 ? this.Ok(response) : this.StatusCode(statusCode, response);
        }
        catch (JsonException jex)
        {
            sw.Stop();
            logs.Add($"[Simulator] JSON Syntax Error: {jex.Message}");
            var errMessage = $"Invalid JSON payload: {jex.Message}";
            this.webhookStore?.Record(
                "Simulator",
                request?.EventType ?? "Unknown",
                "127.0.0.1",
                "Leecharr-Developer-Sandbox/1.0",
                400,
                request?.PayloadJson ?? string.Empty,
                errMessage,
                false);

            return this.BadRequest(new DeveloperWebhookSimulateResponse
            {
                Success = false,
                StatusCode = 400,
                Message = errMessage,
                ExecutionTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                TraceLogs = logs,
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            logs.Add($"[Simulator] Execution Error: {ex.Message}");
            var errMessage = $"Simulation failure: {ex.Message}";
            this.webhookStore?.Record(
                "Simulator",
                request?.EventType ?? "Unknown",
                "127.0.0.1",
                "Leecharr-Developer-Sandbox/1.0",
                500,
                request?.PayloadJson ?? string.Empty,
                errMessage,
                false);

            return this.StatusCode(500, new DeveloperWebhookSimulateResponse
            {
                Success = false,
                StatusCode = 500,
                Message = errMessage,
                ExecutionTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                TraceLogs = logs,
            });
        }
    }

    private ArrWebhookController GetWebhookController()
    {
        if (this.arrWebhookController != null)
        {
            return this.arrWebhookController;
        }

        if (this.serviceProvider != null)
        {
            try
            {
                var controller = this.serviceProvider.GetService(typeof(ArrWebhookController)) as ArrWebhookController;
                if (controller != null)
                {
                    return controller;
                }

                return ActivatorUtilities.CreateInstance<ArrWebhookController>(this.serviceProvider);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to resolve ArrWebhookController from service provider");
            }
        }

        return null;
    }

    // ==========================================
    // 5. CONFIGURATION & ENVIRONMENT MATRIX
    // ==========================================

    [HttpGet("config")]
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5443:Publicly writable directories", Justification = "Secure application subfolder is used instead of system temp.")]
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

        var appData = this.appFolderInfo?.AppDataFolder
            ?? Environment.GetEnvironmentVariable("LEECHARR__APP_DATA")
            ?? Environment.GetEnvironmentVariable("SEEDARR__APP_DATA")
            ?? "/config";
        var tempDirectory = Path.Combine(appData, "temp");
        if (!Directory.Exists(tempDirectory))
        {
            try
            {
                Directory.CreateDirectory(tempDirectory);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Could not pre-create developer temp directory: {0}", tempDirectory);
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
            AppDataDirectory = appData,
            TempDirectory = tempDirectory,
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

        return CommandNameRegex.Replace(name, "$1 $2");
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

    public string EventName => this.Name;

    public string PayloadJson => this.Payload;

    public DeveloperSyntheticEvent(string name, string payload)
    {
        this.Name = name;
        this.Payload = payload;
    }
}
