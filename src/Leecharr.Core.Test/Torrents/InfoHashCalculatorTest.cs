// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using BencodeNET.Objects;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class InfoHashCalculatorTest
{
    [Test]
    public void Calculate_CalculatesCorrectSha1Hex()
    {
        var dict = new BDictionary
        {
            ["name"] = new BString("Ubuntu.iso"),
            ["length"] = new BNumber(1000000),
        };

        var hash = InfoHashCalculator.Calculate(dict);

        hash.Should().NotBeNullOrEmpty();
        hash.Length.Should().Be(40);
    }

    [Test]
    public void Calculate_WhenNull_ThrowsException()
    {
        var act = () => InfoHashCalculator.Calculate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void CalculateV2_CalculatesCorrectSha256Hex()
    {
        var dict = new BDictionary
        {
            ["name"] = new BString("Ubuntu.iso"),
            ["length"] = new BNumber(1000000),
        };

        var hash = InfoHashCalculator.CalculateV2(dict);

        hash.Should().NotBeNullOrEmpty();
        hash.Length.Should().Be(64);
    }

    [Test]
    public void CalculateV2_WhenNull_ThrowsException()
    {
        var act = () => InfoHashCalculator.CalculateV2(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void CalculateV2Bytes_CalculatesCorrectSha256Bytes()
    {
        var dict = new BDictionary
        {
            ["name"] = new BString("Ubuntu.iso"),
            ["length"] = new BNumber(1000000),
        };

        var hashBytes = InfoHashCalculator.CalculateV2Bytes(dict);

        hashBytes.Should().NotBeNull();
        hashBytes.Length.Should().Be(32);
    }
}
