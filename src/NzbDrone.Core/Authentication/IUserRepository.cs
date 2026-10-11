// Copyright (c) FeedItOut. All rights reserved.

using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public interface IUserRepository : IBasicRepository<User>
{
    User FindByUsername(string username);

    User FindByEmail(string email);

    User FindByEmailForProvider(string email, string providerId);

    User FindByIdentifier(Guid identifier);

    User FindByExternalId(string providerId, string externalSubjectId);

    int GetUserCount();
}
