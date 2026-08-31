// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Authentication;

public interface IClaimsRoleMappingService
{
    List<string> ResolveRoles(IdentityProviderDefinition provider, IReadOnlyList<string> rawGroups, bool isFirstUser);
}
