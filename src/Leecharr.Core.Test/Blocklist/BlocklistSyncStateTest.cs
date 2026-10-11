// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Blocklist;

namespace Leecharr.Core.Test.Blocklist;

[TestFixture]
public class BlocklistSyncStateTest
{
    [Test]
    public void RecordSuccessfulSync_PersistsTimestampAndSchedulesNextRun()
    {
        var configService = Substitute.For<IConfigService>();
        configService.BlocklistUpdateIntervalHours.Returns(24);
        Dictionary<string, object> saved = null!;
        configService.SaveConfigDictionary(Arg.Do<Dictionary<string, object>>(x => saved = x));
        configService.BlocklistLastSuccessfulSyncUtc.Returns(_ =>
        {
            if (saved == null || !saved.TryGetValue("BlocklistLastSuccessfulSyncUtc", out var raw))
            {
                return null;
            }

            return DateTime.Parse((string)raw!, null, System.Globalization.DateTimeStyles.RoundtripKind);
        });

        var syncState = new BlocklistSyncState(configService);
        var syncedAt = new DateTime(2024, 3, 10, 9, 15, 0, DateTimeKind.Utc);

        syncState.RecordSuccessfulSync(syncedAt);

        syncState.LastSuccessfulSyncUtc.Should().Be(syncedAt);
        syncState.NextScheduledSyncUtc.Should().Be(syncedAt.AddHours(24));
    }

    [Test]
    public void ResolveNextScheduledSyncUtc_WhenAutoUpdateDisabled_ReturnsNull()
    {
        var configService = Substitute.For<IConfigService>();
        var syncState = new BlocklistSyncState(configService);

        syncState.ResolveNextScheduledSyncUtc(24, false).Should().BeNull();
    }

    [Test]
    public void ResolveNextScheduledSyncUtc_WhenNoScheduleSet_UsesLastSuccessfulSyncPlusInterval()
    {
        var configService = Substitute.For<IConfigService>();
        var lastSync = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        configService.BlocklistLastSuccessfulSyncUtc.Returns(lastSync);

        var syncState = new BlocklistSyncState(configService);

        syncState.ResolveNextScheduledSyncUtc(48, true).Should().Be(lastSync.AddHours(48));
    }
}
