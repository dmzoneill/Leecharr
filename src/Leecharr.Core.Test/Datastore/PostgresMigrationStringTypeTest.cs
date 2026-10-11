// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Data;
using System.Reflection;
using FluentAssertions;
using FluentMigrator.Runner.Generators.Postgres;
using NUnit.Framework;

namespace Leecharr.Core.Test.Datastore;

[TestFixture]
public class PostgresMigrationStringTypeTest
{
    [Test]
    public void FluentMigrator_UnsizedString_MapsToPostgresText()
    {
        MapPostgresString(null).Should().Be("text");
    }

    [Test]
    public void FluentMigrator_ExplicitStringLength_MapsToVarchar()
    {
        MapPostgresString(255).Should().Be("varchar(255)");
    }

    [Test]
    public void FluentMigrator_MaxLengthString_MapsToPostgresText()
    {
        MapPostgresString(int.MaxValue).Should().Be("text");
    }

    private static string MapPostgresString(int? size)
    {
        var typeMapType = typeof(PostgresGenerator).Assembly.GetType(
            "FluentMigrator.Runner.Generators.Postgres.PostgresTypeMap",
            throwOnError: true)!;

        var typeMap = Activator.CreateInstance(typeMapType)!;
        var getTypeMap = typeMapType.GetMethod("GetTypeMap", BindingFlags.Instance | BindingFlags.Public)!;

        return (string)getTypeMap.Invoke(typeMap, new object[] { DbType.String, size, null })!;
    }
}
