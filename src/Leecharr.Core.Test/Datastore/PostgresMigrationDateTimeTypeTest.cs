// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Data;
using System.Reflection;
using FluentAssertions;
using FluentMigrator.Runner.Generators.Postgres;
using NUnit.Framework;

namespace Leecharr.Core.Test.Datastore;

[TestFixture]
public class PostgresMigrationDateTimeTypeTest
{
    [Test]
    public void FluentMigrator_DateTime_MapsToPostgresTimestampWithoutTimeZone()
    {
        MapPostgresDateTime().Should().Be("timestamp");
    }

    private static string MapPostgresDateTime()
    {
        var typeMapType = typeof(PostgresGenerator).Assembly.GetType(
            "FluentMigrator.Runner.Generators.Postgres.PostgresTypeMap",
            throwOnError: true)!;

        var typeMap = Activator.CreateInstance(typeMapType)!;
        var getTypeMap = typeMapType.GetMethod("GetTypeMap", BindingFlags.Instance | BindingFlags.Public)!;

        return (string)getTypeMap.Invoke(typeMap, new object?[] { DbType.DateTime, null, null })!;
    }
}
