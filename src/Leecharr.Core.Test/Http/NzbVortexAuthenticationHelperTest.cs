// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using Leecharr.Http.Security;
using NUnit.Framework;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class NzbVortexAuthenticationHelperTest
{
    [Test]
    public void ComputeLoginHash_MatchesNzbVortexDocumentationExample()
    {
        var hash = NzbVortexAuthenticationHelper.ComputeLoginHash(
            "B8DB315C-2C2C-45AA-9088-875D9ED9A90E",
            "5E992A86",
            "1EC2-FCD9");

        hash.Should().Be("AmFTryv+yASQ6japQf4KgD9ChvJl67+e5SytFhCbWRM=");
    }

    [Test]
    public void TryValidateLogin_AcceptsHashWithoutBase64Padding()
    {
        const string apiKey = "test-api-key-123";
        const string cnonce = "clientnonce1";
        var nonce = NzbVortexAuthenticationHelper.IssueNonce();
        var hash = NzbVortexAuthenticationHelper.ComputeLoginHash(nonce, cnonce, apiKey).TrimEnd('=');

        NzbVortexAuthenticationHelper.TryValidateLogin(nonce, cnonce, hash, apiKey).Should().BeTrue();
        NzbVortexAuthenticationHelper.TryValidateLogin(nonce, cnonce, hash, apiKey).Should().BeFalse();
    }
}
