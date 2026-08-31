// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Authentication;

public enum AuthenticationType
{
    None = 0,
    Basic = 1,
    Forms = 2,
    External = 3,
    Sso = 4,
}
