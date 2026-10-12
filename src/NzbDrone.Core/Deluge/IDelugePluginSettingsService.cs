// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Deluge;

public interface IDelugePluginSettingsService
{
    IReadOnlyDictionary<string, object> GetSchedulerConfig();

    bool SetSchedulerConfig(JsonElement config);

    bool SetSchedulerEnabled(bool enabled);

    IReadOnlyList<object> GetAutoAddWatchdirs();

    bool SetAutoAddConfig(JsonElement config);

    bool SetAutoAddOptions(JsonElement args);

    bool SetAutoAddEnabled(bool enabled);

    IReadOnlyDictionary<string, object> GetBlocklistConfig();

    bool SetBlocklistConfig(JsonElement config);

    Task<int> ImportBlocklistAsync(CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, object> GetBlocklistStatus(int rulesLoaded);

    IReadOnlyDictionary<string, object> GetExtractorConfig(string defaultDest);

    bool SetExtractorConfig(JsonElement config);

    IReadOnlyList<object> GetExecuteCommands();

    bool AddExecuteCommand(JsonElement args);

    bool RemoveExecuteCommand(JsonElement args);

    bool SetExecuteConfig(JsonElement config);
}
