// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Data;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.Datastore;

[TestFixture]
public class TimeOnlyTypeHandlerTest
{
    private TimeOnlyTypeHandler handler = null!;

    [SetUp]
    public void SetUp()
    {
        this.handler = new TimeOnlyTypeHandler();
    }

    [Test]
    public void Parse_WhenValueIsTimeOnly_ReturnsSameValue()
    {
        var expected = new TimeOnly(14, 30, 45);
        var result = this.handler.Parse(expected);

        result.Should().Be(expected);
    }

    [Test]
    public void Parse_WhenValueIsTimeSpan_ConvertsCorrectly()
    {
        var timeSpan = new TimeSpan(14, 30, 45);
        var result = this.handler.Parse(timeSpan);

        result.Should().Be(new TimeOnly(14, 30, 45));
    }

    [Test]
    public void Parse_WhenValueIsDateTime_ConvertsCorrectly()
    {
        var dateTime = new DateTime(2026, 9, 18, 14, 30, 45);
        var result = this.handler.Parse(dateTime);

        result.Should().Be(new TimeOnly(14, 30, 45));
    }

    [TestCase("14:30:45", 14, 30, 45)]
    [TestCase("00:00:00", 0, 0, 0)]
    [TestCase("23:59:59", 23, 59, 59)]
    [TestCase("08:15", 8, 15, 0)]
    public void Parse_WhenValueIsString_ParsesCorrectly(string input, int hour, int minute, int second)
    {
        var result = this.handler.Parse(input);

        result.Should().Be(new TimeOnly(hour, minute, second));
    }

    [Test]
    public void Parse_WhenValueIsNull_ReturnsMidnightFallback()
    {
        var result = this.handler.Parse(null!);

        result.Should().Be(new TimeOnly(0, 0, 0));
    }

    [Test]
    public void Parse_WhenValueIsFormattableObject_ParsesViaToString()
    {
        object obj = new TimeSpan(9, 45, 0);
        var result = this.handler.Parse(obj);

        result.Should().Be(new TimeOnly(9, 45, 0));
    }

    [Test]
    public void SetValue_WhenParameterNotNull_SetsFormattedString()
    {
        var param = Substitute.For<IDbDataParameter>();
        var value = new TimeOnly(14, 30, 45);

        this.handler.SetValue(param, value);

        param.Value.Should().Be("14:30:45");
    }

    [Test]
    public void SetValue_WhenParameterNull_DoesNotThrow()
    {
        var act = () => this.handler.SetValue(null!, new TimeOnly(14, 30, 45));

        act.Should().NotThrow();
    }
}
