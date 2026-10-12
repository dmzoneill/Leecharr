// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;

namespace Leecharr.Core.Test.ArrIntegration;

[TestFixture]
public class ArrSyncConnectionFilterTest
{
    [Test]
    public void SelectForSync_ExcludesConnectionsWithSyncDisabled()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Id = 1, SyncEnabled = true },
            new() { Id = 2, SyncEnabled = false },
        };

        var selected = ArrSyncConnectionFilter.SelectForSync(connections, DateTime.UtcNow, enforceRefreshInterval: false).ToList();

        selected.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Test]
    public void SelectForSync_WhenEnforcingInterval_SkipsConnectionsNotYetDue()
    {
        var utcNow = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var connections = new List<ArrConnectionDefinition>
        {
            new()
            {
                Id = 1,
                SyncEnabled = true,
                SyncIntervalMinutes = 30,
                LastSyncUtc = utcNow.AddMinutes(-10),
            },
            new()
            {
                Id = 2,
                SyncEnabled = true,
                SyncIntervalMinutes = 30,
                LastSyncUtc = utcNow.AddMinutes(-40),
            },
        };

        var selected = ArrSyncConnectionFilter.SelectForSync(connections, utcNow, enforceRefreshInterval: true).ToList();

        selected.Should().ContainSingle().Which.Id.Should().Be(2);
    }

    [Test]
    public void RecordSuccessfulSync_PersistsLastSyncUtc()
    {
        var repository = Substitute.For<IArrConnectionRepository>();
        var connection = new ArrConnectionDefinition { Id = 5 };
        var syncedAt = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);

        ArrSyncConnectionFilter.RecordSuccessfulSync(repository, connection, syncedAt);

        connection.LastSyncUtc.Should().Be(syncedAt);
        repository.Received(1).Update(connection);
    }
}
