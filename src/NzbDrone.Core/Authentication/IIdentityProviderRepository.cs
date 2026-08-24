using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public interface IIdentityProviderRepository : IBasicRepository<IdentityProviderDefinition>
{
    IEnumerable<IdentityProviderDefinition> GetEnabled();
    IdentityProviderDefinition FindByProviderId(string providerId);
}
