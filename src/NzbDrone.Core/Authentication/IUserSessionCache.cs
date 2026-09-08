// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Authentication;

public interface IUserSessionCache
{
    void InvalidateCache(string token);

    void ClearCache();
}
