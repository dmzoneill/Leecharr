// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.Datastore;

[TestFixture]
public class SqlLikeHelperTest
{
    [Test]
    public void EscapeLikeValue_EscapesWildcardsAndEscapeCharacter()
    {
        SqlLikeHelper.EscapeLikeValue("100%").Should().Be("100\\%");
        SqlLikeHelper.EscapeLikeValue("foo_bar").Should().Be("foo\\_bar");
        SqlLikeHelper.EscapeLikeValue("back\\slash").Should().Be("back\\\\slash");
    }

    [Test]
    public void BuildContainsPattern_WrapsEscapedValue()
    {
        SqlLikeHelper.BuildContainsPattern("_", lowercaseQuery: true).Should().Be("%\\_%");
        SqlLikeHelper.BuildContainsPattern("A_B", lowercaseQuery: false).Should().Be("%A\\_B%");
    }
}
