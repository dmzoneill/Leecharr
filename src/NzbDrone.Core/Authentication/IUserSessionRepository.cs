// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public interface IUserSessionRepository : IBasicRepository<UserSession>
{
    UserSession FindBySessionToken(string token);

    UserSession FindByRefreshToken(string refreshToken);

    IEnumerable<UserSession> FindByUserId(int userId);

    void DeleteExpiredSessions();

    void DeleteByUserId(int userId);
}
