// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.Datastore;

[TestFixture]
public class DatabaseTest
{
    private string tempFolder = null!;
    private string dbPath = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempFolder = Path.Combine(Path.GetTempPath(), "DatabaseTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempFolder);
        this.dbPath = Path.Combine(this.tempFolder, "test.db");
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(this.tempFolder))
            {
                Directory.Delete(this.tempFolder, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Test]
    public void OpenConnection_WhenSqlite_ConfiguresWalAutocheckpointPragma()
    {
        var connectionString = $"Data Source={this.dbPath};";
        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);

        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "PRAGMA wal_autocheckpoint;";
        var walAutocheckpoint = Convert.ToInt32(cmd.ExecuteScalar());
        walAutocheckpoint.Should().Be(1000);
    }

    [Test]
    public void DbFactory_Create_WhenSqlite_ConfiguresWalAndAutocheckpoint()
    {
        var factory = new DbFactory();
        var connectionString = $"Data Source={this.dbPath};";
        var database = factory.Create(DatabaseType.SQLite, connectionString);

        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "PRAGMA journal_mode;";
        var journalMode = (string)cmd.ExecuteScalar();
        journalMode.Should().BeEquivalentTo("wal");

        cmd.CommandText = "PRAGMA wal_autocheckpoint;";
        var walAutocheckpoint = Convert.ToInt32(cmd.ExecuteScalar());
        walAutocheckpoint.Should().Be(1000);
    }
}
