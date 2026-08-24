using System.Collections.Generic;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class ClaimsRoleMappingServiceTest
{
    private Logger _logger;
    private ClaimsRoleMappingService _service;

    [SetUp]
    public void SetUp()
    {
        _logger = LogManager.GetCurrentClassLogger();
        _service = new ClaimsRoleMappingService(_logger);
    }

    [Test]
    public void ResolveRoles_WhenFirstUser_ShouldReturnAdmin()
    {
        var roles = _service.ResolveRoles(null, new List<string>(), true);

        Assert.That(roles, Does.Contain("Admin"));
    }

    [Test]
    public void ResolveRoles_WithRegexRules_ShouldMapProperly()
    {
        var provider = new IdentityProviderDefinition
        {
            Name = "Authentik",
            RoleMappingRules = "{\"Admin\":\"^(admin|infrastructure|devops)$\",\"Operator\":\"^(media-manager|operators)$\"}"
        };

        var adminRoles = _service.ResolveRoles(provider, new List<string> { "devops", "other-group" }, false);
        Assert.That(adminRoles, Does.Contain("Admin"));

        var operatorRoles = _service.ResolveRoles(provider, new List<string> { "media-manager" }, false);
        Assert.That(operatorRoles, Does.Contain("Operator"));

        var fallbackRoles = _service.ResolveRoles(provider, new List<string> { "random-group" }, false);
        Assert.That(fallbackRoles, Does.Contain("User"));
    }
}
