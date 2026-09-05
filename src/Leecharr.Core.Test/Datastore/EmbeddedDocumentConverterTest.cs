// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Data;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.Datastore;

[TestFixture]
public class EmbeddedDocumentConverterTest
{
    [Test]
    public void Parse_WhenValueIsNull_ReturnsEmptyListInstance()
    {
        var handler = new EmbeddedDocumentConverter<List<int>>();
        var result = handler.Parse(null!);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Test]
    public void Parse_WhenValueIsEmptyString_ReturnsEmptyListInstance()
    {
        var handler = new EmbeddedDocumentConverter<List<string>>();
        var result = handler.Parse(string.Empty);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Test]
    public void Parse_WhenValueIsLiteralNullString_ReturnsEmptyDictionaryInstance()
    {
        var handler = new EmbeddedDocumentConverter<Dictionary<string, string>>();
        var result = handler.Parse("null");

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Test]
    public void Parse_WhenValueIsValidJson_DeserializesCorrectly()
    {
        var handler = new EmbeddedDocumentConverter<List<string>>();
        var result = handler.Parse("[\"alpha\",\"beta\"]");

        result.Should().NotBeNull();
        result.Should().ContainInOrder("alpha", "beta");
    }

    [Test]
    public void SetValue_WhenValueIsNull_SerializesDefaultInstanceJson()
    {
        var handler = new EmbeddedDocumentConverter<List<int>>();
        var parameter = Substitute.For<IDbDataParameter>();

        handler.SetValue(parameter, null!);

        parameter.Received(1).Value = "[]";
    }

    [Test]
    public void SetValue_WhenValueIsNonNull_SerializesJson()
    {
        var handler = new EmbeddedDocumentConverter<List<int>>();
        var parameter = Substitute.For<IDbDataParameter>();

        handler.SetValue(parameter, new List<int> { 1, 2, 3 });

        parameter.Received(1).Value = "[1,2,3]";
    }
}
