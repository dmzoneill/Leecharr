// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public interface IUserSessionRepository : IBasicRepository<UserSession>
{
    UserSession FindBySessionToken(string token);

    UserSession FindByRefreshToken(string refreshToken);

    IEnumerable<UserSession> FindByUserId(int userId);

    void DeleteExpiredSessions();

    Task<int> PruneExpiredSessionsAsync(CancellationToken cancellationToken = default);

    void DeleteByUserId(int userId);

    void RevokeSession(string token);
}
