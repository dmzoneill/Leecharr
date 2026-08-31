// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Authentication;

public interface IIdentityProviderService
{
    List<IdentityProviderDefinition> GetAll();

    List<IdentityProviderDefinition> GetEnabled();

    IdentityProviderDefinition GetById(int id);

    IdentityProviderDefinition GetByProviderId(string providerId);

    IdentityProviderDefinition Add(IdentityProviderDefinition provider);

    IdentityProviderDefinition Update(IdentityProviderDefinition provider);

    void Delete(int id);

    Task<bool> TestConnectionAsync(IdentityProviderDefinition provider);
}
