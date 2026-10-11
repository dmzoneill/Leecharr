// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using Leecharr.Api.V1.System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.SystemServices;

[TestFixture]
public class SystemDatabaseControllerTest
{
    private IMainDatabase mainDatabase = null!;
    private SystemDatabaseController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.mainDatabase = Substitute.For<IMainDatabase>();
        this.controller = new SystemDatabaseController(this.mainDatabase);
    }

    [Test]
    public void ExecuteQuery_WhenVacuumQueryAndNotReadOnly_ExecutesWithoutTransactionAndSucceeds()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;");
        connection.Open();
        this.mainDatabase.OpenConnection().Returns(connection);

        var request = new DatabaseQueryRequest
        {
            Query = "VACUUM;",
            ReadOnly = false
        };

        var result = this.controller.ExecuteQuery(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var queryResult = okResult.Value as DatabaseQueryResult;
        queryResult.Should().NotBeNull();
        queryResult!.Success.Should().BeTrue();
        queryResult.IsQuery.Should().BeFalse();
        queryResult.Message.Should().Contain("Query executed successfully.");
    }

    [Test]
    public void ExecuteQuery_WhenVacuumQueryAndReadOnly_ReturnsBadRequest()
    {
        var request = new DatabaseQueryRequest
        {
            Query = "VACUUM;",
            ReadOnly = true
        };

        var result = this.controller.ExecuteQuery(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        var queryResult = badRequest.Value as DatabaseQueryResult;
        queryResult.Should().NotBeNull();
        queryResult!.Success.Should().BeFalse();
        queryResult.ErrorMessage.Should().Contain("Safe Mode (Read-Only) is enabled");
    }

    [Test]
    public void ExecuteQuery_WhenWriteMutationAndNotReadOnly_ExecutesInTransactionAndSucceeds()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;");
        connection.Open();
        using (var createCmd = connection.CreateCommand())
        {
            createCmd.CommandText = "CREATE TABLE TestTable (Id INTEGER PRIMARY KEY, Name TEXT);";
            createCmd.ExecuteNonQuery();
        }

        this.mainDatabase.OpenConnection().Returns(connection);

        var request = new DatabaseQueryRequest
        {
            Query = "INSERT INTO TestTable (Id, Name) VALUES (1, 'TestName');",
            ReadOnly = false
        };

        var result = this.controller.ExecuteQuery(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var queryResult = okResult.Value as DatabaseQueryResult;
        queryResult.Should().NotBeNull();
        queryResult!.Success.Should().BeTrue();
        queryResult.IsQuery.Should().BeFalse();
        queryResult.RowsAffected.Should().Be(1);
    }

    [Test]
    public void ExecuteQuery_WhenReadOnlyStackedSelectAndInsert_ReturnsBadRequest()
    {
        var request = new DatabaseQueryRequest
        {
            Query = "SELECT 1; INSERT INTO TestTable (Id, Name) VALUES (1, 'x');",
            ReadOnly = true
        };

        var result = this.controller.ExecuteQuery(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void ExecuteQuery_WhenReadOnlyMutatingPragma_ReturnsBadRequest()
    {
        var request = new DatabaseQueryRequest
        {
            Query = "PRAGMA user_version = 7;",
            ReadOnly = true
        };

        var result = this.controller.ExecuteQuery(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void ExecuteQuery_WhenReadOnlyTransactionBatch_ReturnsBadRequest()
    {
        var request = new DatabaseQueryRequest
        {
            Query = "BEGIN IMMEDIATE; INSERT INTO TestTable (Id, Name) VALUES (1, 'x'); COMMIT;",
            ReadOnly = true
        };

        var result = this.controller.ExecuteQuery(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void ExecuteQuery_WhenReadOnlySelect_DoesNotMutateDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var createCmd = connection.CreateCommand())
        {
            createCmd.CommandText = "CREATE TABLE TestTable (Id INTEGER PRIMARY KEY, Name TEXT);";
            createCmd.ExecuteNonQuery();
        }

        this.mainDatabase.OpenConnection().Returns(connection);

        var request = new DatabaseQueryRequest
        {
            Query = "SELECT 1;",
            ReadOnly = true
        };

        var result = this.controller.ExecuteQuery(request);

        result.Result.Should().BeOfType<OkObjectResult>();

        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM TestTable;";
        Convert.ToInt32(countCmd.ExecuteScalar()).Should().Be(0);
    }
}
