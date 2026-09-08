// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public interface IUserExternalLoginRepository : IBasicRepository<UserExternalLogin>
{
    UserExternalLogin FindByProvider(string loginProvider, string providerKey);

    IEnumerable<UserExternalLogin> FindByUserId(int userId);

    void DeleteByUserId(int userId);
}
