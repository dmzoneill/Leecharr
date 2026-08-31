using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Network;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Datastore;

public static class TableRegistration
{
    public static void RegisterTables()
    {
        SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<int>>());
        SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<string>>());
        SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<Dictionary<string, string>>());

        TableMapping.Register<CommandModel>("Commands");
        TableMapping.Register<ConfigModel>("Config");
        TableMapping.Register<ScheduledTask>("ScheduledTasks");
        TableMapping.Register<Tag>("Tags");
        TableMapping.Register<Torrent>("Torrents");
        TableMapping.Register<TorrentFile>("TorrentFiles");
        TableMapping.Register<Category>("Categories");
        TableMapping.Register<TorrentMediaMetadata>("TorrentMediaMetadata");
        TableMapping.Register<TrackerEntry>("TrackerEntries");
        TableMapping.Register<ArrConnectionDefinition>("ArrConnectionDefinitions");
        TableMapping.Register<SpeedSchedule>("SpeedSchedules");
        TableMapping.Register<DownloadHistory>("DownloadHistory");
        TableMapping.Register<NetworkSettings>("NetworkSettings");
        TableMapping.Register<NotificationDefinition>("NotificationDefinitions");
        TableMapping.Register<IndexerDefinition>("IndexerDefinitions");
        TableMapping.Register<RssRule>("RssRules");
    }
}
