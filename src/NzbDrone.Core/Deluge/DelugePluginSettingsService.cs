// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Blocklist;

namespace NzbDrone.Core.Deluge;

public sealed class DelugePluginSettingsService : IDelugePluginSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly IAppFolderInfo appFolderInfo;
    private readonly IConfigService configService;
    private readonly IBlocklistUpdateService blocklistUpdateService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly object gate = new();
    private readonly string persistencePath;

    private DelugePluginSettingsSnapshot snapshot = DelugePluginSettingsSnapshot.CreateDefault();

    public DelugePluginSettingsService(
        IAppFolderInfo appFolderInfo = null,
        IConfigService configService = null,
        IBlocklistUpdateService blocklistUpdateService = null)
    {
        this.appFolderInfo = appFolderInfo;
        this.configService = configService;
        this.blocklistUpdateService = blocklistUpdateService;

        var appData = appFolderInfo?.AppDataFolder ?? AppContext.BaseDirectory;
        this.persistencePath = Path.Combine(appData, "deluge-plugin-settings.json");
        this.LoadFromDisk();
    }

    public IReadOnlyDictionary<string, object> GetSchedulerConfig()
    {
        lock (this.gate)
        {
            return this.snapshot.BuildSchedulerConfigDictionary();
        }
    }

    public bool SetSchedulerConfig(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        lock (this.gate)
        {
            this.snapshot.MergeSchedulerConfig(config);
            this.SaveToDisk();
        }

        return true;
    }

    public bool SetSchedulerEnabled(bool enabled)
    {
        lock (this.gate)
        {
            this.snapshot.SchedulerEnabled = enabled;
            this.SaveToDisk();
        }

        return true;
    }

    public IReadOnlyList<object> GetAutoAddWatchdirs()
    {
        lock (this.gate)
        {
            return this.snapshot.Watchdirs.Select(w => w.ToDelugeObject()).ToList();
        }
    }

    public bool SetAutoAddConfig(JsonElement config)
    {
        lock (this.gate)
        {
            this.snapshot.ApplyAutoAddConfig(config);
            this.SaveToDisk();
        }

        return true;
    }

    public bool SetAutoAddOptions(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() < 2)
        {
            return false;
        }

        if (!args[0].TryGetInt32(out var watchId))
        {
            return false;
        }

        lock (this.gate)
        {
            this.snapshot.SetWatchdirOptions(watchId, args[1]);
            this.SaveToDisk();
        }

        return true;
    }

    public bool SetAutoAddEnabled(bool enabled)
    {
        lock (this.gate)
        {
            this.snapshot.AutoAddEnabled = enabled;
            this.SaveToDisk();
        }

        return true;
    }

    public IReadOnlyDictionary<string, object> GetBlocklistConfig()
    {
        lock (this.gate)
        {
            return this.snapshot.BuildBlocklistConfigDictionary(this.configService);
        }
    }

    public bool SetBlocklistConfig(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        lock (this.gate)
        {
            this.snapshot.MergeBlocklistConfig(config);
            this.SaveToDisk();
        }

        this.ApplyBlocklistConfigToCore(config);
        return true;
    }

    public async Task<int> ImportBlocklistAsync(CancellationToken cancellationToken = default)
    {
        if (this.blocklistUpdateService == null)
        {
            this.logger.Warn("Deluge blocklist.import requested but blocklist update service is not configured.");
            return 0;
        }

        var loaded = await this.blocklistUpdateService.UpdateRulesAsync(cancellationToken);
        lock (this.gate)
        {
            this.snapshot.BlocklistLastImportUtc = DateTime.UtcNow;
            this.snapshot.BlocklistLastRuleCount = loaded;
            this.SaveToDisk();
        }

        return loaded;
    }

    public IReadOnlyDictionary<string, object> GetBlocklistStatus(int rulesLoaded)
    {
        lock (this.gate)
        {
            return this.snapshot.BuildBlocklistStatusDictionary(rulesLoaded);
        }
    }

    public IReadOnlyDictionary<string, object> GetExtractorConfig(string defaultDest)
    {
        lock (this.gate)
        {
            return this.snapshot.BuildExtractorConfigDictionary(defaultDest);
        }
    }

    public bool SetExtractorConfig(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        lock (this.gate)
        {
            this.snapshot.MergeExtractorConfig(config);
            this.SaveToDisk();
        }

        return true;
    }

    public IReadOnlyList<object> GetExecuteCommands()
    {
        lock (this.gate)
        {
            return this.snapshot.ExecuteCommands.Select(c => c.ToDelugeObject()).ToList();
        }
    }

    public bool AddExecuteCommand(JsonElement args)
    {
        lock (this.gate)
        {
            if (!this.snapshot.TryAddExecuteCommand(args))
            {
                return false;
            }

            this.SaveToDisk();
        }

        return true;
    }

    public bool RemoveExecuteCommand(JsonElement args)
    {
        lock (this.gate)
        {
            if (!this.snapshot.TryRemoveExecuteCommand(args))
            {
                return false;
            }

            this.SaveToDisk();
        }

        return true;
    }

    public bool SetExecuteConfig(JsonElement config)
    {
        lock (this.gate)
        {
            this.snapshot.ApplyExecuteConfig(config);
            this.SaveToDisk();
        }

        return true;
    }

    private void ApplyBlocklistConfigToCore(JsonElement config)
    {
        if (this.configService == null)
        {
            return;
        }

        var updates = new Dictionary<string, object>();
        if (config.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
        {
            updates["BlocklistUrl"] = urlProp.GetString() ?? string.Empty;
        }

        if (config.TryGetProperty("load_on_start", out var loadProp) &&
            (loadProp.ValueKind == JsonValueKind.True || loadProp.ValueKind == JsonValueKind.False))
        {
            updates["BlocklistAutoUpdateEnabled"] = loadProp.GetBoolean();
            updates["BlocklistEnabled"] = loadProp.GetBoolean();
        }

        if (updates.Count > 0)
        {
            this.configService.SaveConfigDictionary(updates);
        }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(this.persistencePath))
            {
                return;
            }

            var json = File.ReadAllText(this.persistencePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var loaded = JsonSerializer.Deserialize<DelugePluginSettingsSnapshot>(json, JsonOptions);
            if (loaded != null)
            {
                this.snapshot = loaded;
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to load Deluge plugin settings from {0}", this.persistencePath);
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var directory = Path.GetDirectoryName(this.persistencePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this.snapshot, JsonOptions);
            File.WriteAllText(this.persistencePath, json);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to save Deluge plugin settings to {0}", this.persistencePath);
        }
    }

    private sealed class DelugePluginSettingsSnapshot
    {
        public bool SchedulerEnabled { get; set; }

        public double SchedulerLowDown { get; set; } = -1.0;

        public double SchedulerLowUp { get; set; } = -1.0;

        public int SchedulerLowActive { get; set; } = -1;

        public int SchedulerButtonState { get; set; }

        public List<List<int>> SchedulerSchedule { get; set; } = CreateEmptySchedule();

        public bool AutoAddEnabled { get; set; }

        public List<DelugeWatchdirSnapshot> Watchdirs { get; set; } = new();

        public string BlocklistUrl { get; set; } = string.Empty;

        public bool BlocklistLoadOnStart { get; set; }

        public int BlocklistCheckAfterDays { get; set; } = 4;

        public int BlocklistTimeout { get; set; } = 180;

        public int BlocklistTryTimes { get; set; } = 3;

        public string BlocklistListType { get; set; } = "PeerGuardian";

        public string BlocklistListCompression { get; set; } = string.Empty;

        public long BlocklistLastUpdate { get; set; }

        public long BlocklistFileDate { get; set; }

        public long BlocklistFileSize { get; set; }

        public string BlocklistFileUrl { get; set; } = string.Empty;

        public DateTime? BlocklistLastImportUtc { get; set; }

        public int BlocklistLastRuleCount { get; set; }

        public string ExtractorDest { get; set; } = string.Empty;

        public bool ExtractorUseDest { get; set; }

        public bool ExtractorExtractInPlace { get; set; } = true;

        public List<DelugeExecuteCommandSnapshot> ExecuteCommands { get; set; } = new();

        public static DelugePluginSettingsSnapshot CreateDefault() => new();

        public Dictionary<string, object> BuildSchedulerConfigDictionary()
        {
            var schedule = this.SchedulerSchedule ?? CreateEmptySchedule();
            if (schedule.Count != 7)
            {
                schedule = CreateEmptySchedule();
            }

            return new Dictionary<string, object>
            {
                ["enabled"] = this.SchedulerEnabled,
                ["low_down"] = this.SchedulerLowDown,
                ["low_up"] = this.SchedulerLowUp,
                ["low_active"] = this.SchedulerLowActive,
                ["button_state"] = this.SchedulerButtonState,
                ["schedule"] = schedule,
            };
        }

        public void MergeSchedulerConfig(JsonElement config)
        {
            if (config.TryGetProperty("enabled", out var enabledProp) &&
                (enabledProp.ValueKind == JsonValueKind.True || enabledProp.ValueKind == JsonValueKind.False))
            {
                this.SchedulerEnabled = enabledProp.GetBoolean();
            }

            if (config.TryGetProperty("low_down", out var lowDown) && lowDown.TryGetDouble(out var lowDownVal))
            {
                this.SchedulerLowDown = lowDownVal;
            }

            if (config.TryGetProperty("low_up", out var lowUp) && lowUp.TryGetDouble(out var lowUpVal))
            {
                this.SchedulerLowUp = lowUpVal;
            }

            if (config.TryGetProperty("low_active", out var lowActive) && lowActive.TryGetInt32(out var lowActiveVal))
            {
                this.SchedulerLowActive = lowActiveVal;
            }

            if (config.TryGetProperty("button_state", out var buttonState) && buttonState.TryGetInt32(out var buttonStateVal))
            {
                this.SchedulerButtonState = buttonStateVal;
            }

            if (config.TryGetProperty("schedule", out var scheduleProp) && scheduleProp.ValueKind == JsonValueKind.Array)
            {
                this.SchedulerSchedule = ParseScheduleMatrix(scheduleProp);
            }
        }

        public void ApplyAutoAddConfig(JsonElement config)
        {
            if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("watchdirs", out var watchdirs))
            {
                this.Watchdirs = ParseWatchdirs(watchdirs);
            }
            else if (config.ValueKind == JsonValueKind.Array)
            {
                this.Watchdirs = ParseWatchdirList(config);
            }
        }

        public void SetWatchdirOptions(int watchId, JsonElement options)
        {
            var existing = this.Watchdirs.FirstOrDefault(w => w.Id == watchId);
            if (existing == null)
            {
                existing = new DelugeWatchdirSnapshot { Id = watchId };
                this.Watchdirs.Add(existing);
            }

            existing.MergeOptions(options);
        }

        public Dictionary<string, object> BuildBlocklistConfigDictionary(IConfigService configService)
        {
            var url = this.BlocklistUrl;
            if (string.IsNullOrWhiteSpace(url) && configService != null)
            {
                url = configService.BlocklistUrl ?? string.Empty;
            }

            var loadOnStart = this.BlocklistLoadOnStart;
            if (!loadOnStart && configService != null && configService.BlocklistAutoUpdateEnabled)
            {
                loadOnStart = true;
            }

            return new Dictionary<string, object>
            {
                ["url"] = url ?? string.Empty,
                ["load_on_start"] = loadOnStart,
                ["check_after_days"] = this.BlocklistCheckAfterDays,
                ["timeout"] = this.BlocklistTimeout,
                ["try_times"] = this.BlocklistTryTimes,
                ["list_type"] = this.BlocklistListType ?? "PeerGuardian",
                ["list_compression"] = this.BlocklistListCompression ?? string.Empty,
                ["last_update"] = this.BlocklistLastUpdate,
                ["file_date"] = this.BlocklistFileDate,
                ["file_size"] = this.BlocklistFileSize,
                ["file_url"] = this.BlocklistFileUrl ?? string.Empty,
            };
        }

        public void MergeBlocklistConfig(JsonElement config)
        {
            if (config.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
            {
                this.BlocklistUrl = urlProp.GetString() ?? string.Empty;
            }

            if (config.TryGetProperty("load_on_start", out var loadProp) &&
                (loadProp.ValueKind == JsonValueKind.True || loadProp.ValueKind == JsonValueKind.False))
            {
                this.BlocklistLoadOnStart = loadProp.GetBoolean();
            }

            if (config.TryGetProperty("check_after_days", out var cad) && cad.TryGetInt32(out var cadVal))
            {
                this.BlocklistCheckAfterDays = cadVal;
            }

            if (config.TryGetProperty("timeout", out var timeout) && timeout.TryGetInt32(out var timeoutVal))
            {
                this.BlocklistTimeout = timeoutVal;
            }

            if (config.TryGetProperty("try_times", out var tryTimes) && tryTimes.TryGetInt32(out var tryTimesVal))
            {
                this.BlocklistTryTimes = tryTimesVal;
            }

            if (config.TryGetProperty("list_type", out var listType) && listType.ValueKind == JsonValueKind.String)
            {
                this.BlocklistListType = listType.GetString() ?? "PeerGuardian";
            }

            if (config.TryGetProperty("list_compression", out var compression) && compression.ValueKind == JsonValueKind.String)
            {
                this.BlocklistListCompression = compression.GetString() ?? string.Empty;
            }
        }

        public Dictionary<string, object> BuildBlocklistStatusDictionary(int rulesLoaded)
        {
            var blocked = rulesLoaded > 0 ? rulesLoaded : this.BlocklistLastRuleCount;
            return new Dictionary<string, object>
            {
                ["status"] = "Ready",
                ["num_blocked"] = blocked,
                ["file_size"] = this.BlocklistFileSize,
                ["file_date"] = this.BlocklistFileDate,
            };
        }

        public Dictionary<string, object> BuildExtractorConfigDictionary(string defaultDest)
        {
            var dest = !string.IsNullOrWhiteSpace(this.ExtractorDest) ? this.ExtractorDest : defaultDest;
            return new Dictionary<string, object>
            {
                ["dest"] = dest ?? "/downloads",
                ["use_dest"] = this.ExtractorUseDest,
                ["extract_in_place"] = this.ExtractorExtractInPlace,
            };
        }

        public void MergeExtractorConfig(JsonElement config)
        {
            if (config.TryGetProperty("dest", out var dest) && dest.ValueKind == JsonValueKind.String)
            {
                this.ExtractorDest = dest.GetString() ?? string.Empty;
            }

            if (config.TryGetProperty("use_dest", out var useDest) &&
                (useDest.ValueKind == JsonValueKind.True || useDest.ValueKind == JsonValueKind.False))
            {
                this.ExtractorUseDest = useDest.GetBoolean();
            }

            if (config.TryGetProperty("extract_in_place", out var inPlace) &&
                (inPlace.ValueKind == JsonValueKind.True || inPlace.ValueKind == JsonValueKind.False))
            {
                this.ExtractorExtractInPlace = inPlace.GetBoolean();
            }
        }

        public bool TryAddExecuteCommand(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() < 2)
            {
                return false;
            }

            var eventName = args[0].ValueKind == JsonValueKind.String ? args[0].GetString() : null;
            var command = args[1].ValueKind == JsonValueKind.String ? args[1].GetString() : null;
            if (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            var shell = false;
            if (args.GetArrayLength() > 2 &&
                (args[2].ValueKind == JsonValueKind.True || args[2].ValueKind == JsonValueKind.False))
            {
                shell = args[2].GetBoolean();
            }

            var id = this.ExecuteCommands.Count == 0 ? 1 : this.ExecuteCommands.Max(c => c.Id) + 1;
            this.ExecuteCommands.Add(new DelugeExecuteCommandSnapshot
            {
                Id = id,
                Event = eventName,
                Command = command,
                Shell = shell,
                Enabled = true,
            });

            return true;
        }

        public bool TryRemoveExecuteCommand(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() == 0)
            {
                return false;
            }

            var first = args[0];
            if (first.ValueKind == JsonValueKind.Number && first.TryGetInt32(out var id))
            {
                return this.ExecuteCommands.RemoveAll(c => c.Id == id) > 0;
            }

            if (first.ValueKind == JsonValueKind.String)
            {
                var key = first.GetString() ?? string.Empty;
                if (TryParseExecuteCommandId(key, out var parsedId))
                {
                    return this.ExecuteCommands.RemoveAll(c => c.Id == parsedId) > 0;
                }

                return this.ExecuteCommands.RemoveAll(c =>
                    string.Equals(c.Event, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Command, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(FormatExecuteCommandId(c.Id), key, StringComparison.OrdinalIgnoreCase)) > 0;
            }

            return false;
        }

        public void ApplyExecuteConfig(JsonElement config)
        {
            if (config.ValueKind == JsonValueKind.Array)
            {
                this.ExecuteCommands = ParseExecuteCommandList(config);
            }
            else if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("commands", out var commands))
            {
                this.ExecuteCommands = ParseExecuteCommandList(commands);
            }
        }

        private static List<List<int>> CreateEmptySchedule()
        {
            var matrix = new List<List<int>>();
            for (var day = 0; day < 7; day++)
            {
                var row = new List<int>();
                for (var hour = 0; hour < 24; hour++)
                {
                    row.Add(0);
                }

                matrix.Add(row);
            }

            return matrix;
        }

        private static List<List<int>> ParseScheduleMatrix(JsonElement scheduleProp)
        {
            var matrix = CreateEmptySchedule();
            var dayIndex = 0;
            foreach (var dayElem in scheduleProp.EnumerateArray())
            {
                if (dayIndex >= 7 || dayElem.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var hourIndex = 0;
                foreach (var hourElem in dayElem.EnumerateArray())
                {
                    if (hourIndex >= 24)
                    {
                        break;
                    }

                    if (hourElem.TryGetInt32(out var cell))
                    {
                        matrix[dayIndex][hourIndex] = cell;
                    }

                    hourIndex++;
                }

                dayIndex++;
            }

            return matrix;
        }

        private static List<DelugeWatchdirSnapshot> ParseWatchdirs(JsonElement watchdirs)
        {
            var list = new List<DelugeWatchdirSnapshot>();
            if (watchdirs.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in watchdirs.EnumerateObject())
                {
                    if (int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    {
                        var snapshot = new DelugeWatchdirSnapshot { Id = id };
                        snapshot.MergeOptions(property.Value);
                        list.Add(snapshot);
                    }
                }
            }

            return list;
        }

        private static List<DelugeWatchdirSnapshot> ParseWatchdirList(JsonElement listElement)
        {
            var list = new List<DelugeWatchdirSnapshot>();
            var nextId = 1;
            foreach (var item in listElement.EnumerateArray())
            {
                var snapshot = new DelugeWatchdirSnapshot { Id = nextId++ };
                snapshot.MergeOptions(item);
                list.Add(snapshot);
            }

            return list;
        }

        private static List<DelugeExecuteCommandSnapshot> ParseExecuteCommandList(JsonElement commands)
        {
            var list = new List<DelugeExecuteCommandSnapshot>();
            if (commands.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            var nextId = 1;
            foreach (var item in commands.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 3)
                {
                    list.Add(new DelugeExecuteCommandSnapshot
                    {
                        Id = item[0].TryGetInt32(out var id) ? id : nextId,
                        Event = item[1].GetString() ?? string.Empty,
                        Command = item[2].GetString() ?? string.Empty,
                        Enabled = item.GetArrayLength() > 3 && item[3].ValueKind == JsonValueKind.True,
                    });
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    var snapshot = new DelugeExecuteCommandSnapshot { Id = nextId };
                    if (item.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var parsedId))
                    {
                        snapshot.Id = parsedId;
                    }

                    if (item.TryGetProperty("event", out var eventProp) && eventProp.ValueKind == JsonValueKind.String)
                    {
                        snapshot.Event = eventProp.GetString() ?? string.Empty;
                    }

                    if (item.TryGetProperty("command", out var commandProp) && commandProp.ValueKind == JsonValueKind.String)
                    {
                        snapshot.Command = commandProp.GetString() ?? string.Empty;
                    }

                    if (item.TryGetProperty("enabled", out var enabledProp) &&
                        (enabledProp.ValueKind == JsonValueKind.True || enabledProp.ValueKind == JsonValueKind.False))
                    {
                        snapshot.Enabled = enabledProp.GetBoolean();
                    }

                    list.Add(snapshot);
                }

                nextId++;
            }

            return list;
        }
    }

    private sealed class DelugeWatchdirSnapshot
    {
        public int Id { get; set; }

        public string Path { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public Dictionary<string, JsonElement> Extra { get; set; } = new();

        public void MergeOptions(JsonElement options)
        {
            if (options.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in options.EnumerateObject())
            {
                if (string.Equals(property.Name, "path", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    this.Path = property.Value.GetString() ?? string.Empty;
                    continue;
                }

                if (string.Equals(property.Name, "enabled", StringComparison.OrdinalIgnoreCase) &&
                    (property.Value.ValueKind == JsonValueKind.True || property.Value.ValueKind == JsonValueKind.False))
                {
                    this.Enabled = property.Value.GetBoolean();
                    continue;
                }

                this.Extra[property.Name] = property.Value.Clone();
            }
        }

        public Dictionary<string, object> ToDelugeObject()
        {
            var dict = new Dictionary<string, object>
            {
                ["id"] = this.Id,
                ["path"] = this.Path,
                ["enabled"] = this.Enabled,
            };

            foreach (var pair in this.Extra)
            {
                dict[pair.Key] = JsonSerializer.Deserialize<object>(pair.Value.GetRawText()) ?? pair.Value.ToString();
            }

            return dict;
        }
    }

    private sealed class DelugeExecuteCommandSnapshot
    {
        public int Id { get; set; }

        public string Event { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public bool Shell { get; set; }

        public bool Enabled { get; set; } = true;

        public object[] ToDelugeObject() => new object[] { FormatExecuteCommandId(this.Id), this.Event, this.Command, this.Enabled };
    }

    private static string FormatExecuteCommandId(int id) => "cmd" + id.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseExecuteCommandId(string key, out int id)
    {
        if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        if (key.StartsWith("cmd", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key.AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        id = 0;
        return false;
    }
}
