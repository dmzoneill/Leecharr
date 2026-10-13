// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Linq;
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
        var okResult = (OkObjectResult)result.Result!;
        var queryResult = (DatabaseQueryResult)okResult.Value!;
        queryResult.Success.Should().BeTrue();
        queryResult.IsQuery.Should().BeTrue();
    }

    [Test]
    public void GetSchema_WhenTableHasExpressionIndex_ListsExpressionAndColumnIndexes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var setupCmd = connection.CreateCommand())
        {
            setupCmd.CommandText = @"
CREATE TABLE ExprIdxTable (Id INTEGER PRIMARY KEY, Value INTEGER);
CREATE INDEX ExprIdxTable_expr ON ExprIdxTable ((Value * 2));
CREATE INDEX ExprIdxTable_value ON ExprIdxTable (Value);";
            setupCmd.ExecuteNonQuery();
        }

        this.mainDatabase.OpenConnection().Returns(connection);

        var result = this.controller.GetSchema();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var schema = okResult.Value as DatabaseSchemaResponse;
        schema.Should().NotBeNull();

        var table = schema!.Tables.Single(t => t.Name == "ExprIdxTable");
        table.Indexes.Should().HaveCount(2);
        table.Indexes.Should().Contain(i => i.Name == "ExprIdxTable_expr" && i.Columns.Single() == "(expression)");
        table.Indexes.Should().Contain(i => i.Name == "ExprIdxTable_value" && i.Columns.Single() == "Value");
    }

    [TestCase("/*comment*/ DELETE FROM TestTable;")]
    [TestCase("-- hide\nDELETE FROM TestTable;")]
    [TestCase("WITH kept AS (SELECT 1) DELETE FROM TestTable;")]
    [TestCase("/*comment*/ WITH kept AS (SELECT 1) DELETE FROM TestTable;")]
    [TestCase("WITH kept AS (SELECT ')') DELETE FROM TestTable;")]
    [TestCase("WITH kept AS NOT MATERIALIZED (SELECT 1) DELETE FROM TestTable;")]
    [TestCase("/*comment*/ UPDATE TestTable SET Name = 'changed';")]
    [TestCase("/*comment*/ PRAGMA journal_mode = WAL;")]
    [TestCase("/*comment*/ BEGIN;")]
    [TestCase("/*comment*/ PRAGMA query_only = OFF;")]
    [TestCase("SELECT 1; /*comment*/ DELETE FROM TestTable;")]
    public void ExecuteQuery_WhenReadOnlyWriteHiddenByPrefix_ReturnsBadRequest(string query)
    {
        var result = this.controller.ExecuteQuery(new DatabaseQueryRequest
        {
            Query = query,
            ReadOnly = true
        });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        var queryResult = badRequest.Value as DatabaseQueryResult;
        queryResult.Should().NotBeNull();
        queryResult!.Success.Should().BeFalse();
        this.mainDatabase.DidNotReceive().OpenConnection();
    }

    [TestCase("/*comment*/ SELECT Name FROM TestTable;", "kept")]
    [TestCase("WITH kept AS (SELECT Name FROM TestTable) SELECT Name FROM kept;", "kept")]
    [TestCase("SELECT '/*not-a-comment*/';", "/*not-a-comment*/")]
    [TestCase("WITH x AS (SELECT ')') SELECT ')';", ")")]
    public void ExecuteQuery_WhenReadOnlySelectAfterCommentOrWith_ReturnsRows(string query, string expected)
    {
        using var connection = this.OpenSeededDatabase();

        var result = this.controller.ExecuteQuery(new DatabaseQueryRequest
        {
            Query = query,
            ReadOnly = true
        });

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var queryResult = (DatabaseQueryResult)okResult.Value!;
        queryResult.Success.Should().BeTrue();
        queryResult.IsQuery.Should().BeTrue();
        queryResult.Rows.Should().ContainSingle();
        queryResult.Rows[0][0].Should().Be(expected);
    }

    [Test]
    public void ExecuteQuery_WhenInsertAfterCommentAndNotReadOnly_ExecutesInTransactionAndSucceeds()
    {
        using var connection = this.OpenSeededDatabase();

        var result = this.controller.ExecuteQuery(new DatabaseQueryRequest
        {
            Query = "/*comment*/ INSERT INTO TestTable (Id, Name) VALUES (2, 'added');",
            ReadOnly = false
        });

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var queryResult = (DatabaseQueryResult)okResult.Value!;
        queryResult.Success.Should().BeTrue();
        queryResult.IsQuery.Should().BeFalse();
        queryResult.RowsAffected.Should().Be(1);
    }

    [Test]
    public void ExecuteQuery_WhenInsertAfterWithAndNotReadOnly_ExecutesInTransactionAndSucceeds()
    {
        using var connection = this.OpenSeededDatabase();

        var result = this.controller.ExecuteQuery(new DatabaseQueryRequest
        {
            Query = "WITH src AS (SELECT 2 AS Id, 'added' AS Name) INSERT INTO TestTable (Id, Name) SELECT Id, Name FROM src;",
            ReadOnly = false
        });

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var queryResult = (DatabaseQueryResult)okResult.Value!;
        queryResult.Success.Should().BeTrue();
        queryResult.IsQuery.Should().BeFalse();
        queryResult.RowsAffected.Should().Be(1);
    }

    [Test]
    public void ExecuteQuery_WhenVacuumAfterCommentAndNotReadOnly_ExecutesWithoutTransactionAndSucceeds()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        this.mainDatabase.OpenConnection().Returns(connection);

        var result = this.controller.ExecuteQuery(new DatabaseQueryRequest
        {
            Query = "/*comment*/ VACUUM;",
            ReadOnly = false
        });

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var queryResult = (DatabaseQueryResult)okResult.Value!;
        queryResult.Success.Should().BeTrue();
        queryResult.IsQuery.Should().BeFalse();
        queryResult.Message.Should().Contain("Query executed successfully.");
    }

    private SqliteConnection OpenSeededDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var createCmd = connection.CreateCommand())
        {
            createCmd.CommandText = "CREATE TABLE TestTable (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO TestTable (Id, Name) VALUES (1, 'kept');";
            createCmd.ExecuteNonQuery();
        }

        this.mainDatabase.OpenConnection().Returns(connection);
        return connection;
    }
}
