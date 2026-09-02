// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;
using NzbDrone.Core.Authentication;

namespace Leecharr.Http.Authentication;

public interface IDynamicAuthSchemeManager
{
    Task RegisterOrUpdateOidcProviderAsync(IdentityProviderDefinition provider);

    Task RemoveProviderSchemeAsync(string providerId);

    Task InitializeConfiguredProvidersAsync();
}
