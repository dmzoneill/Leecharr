// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class UserSession : ModelBase
{
    public int UserId { get; set; }

    public string SessionToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; }

    public DateTime Expiry { get; set; }

    public DateTime ExpiresAt
    {
        get => this.Expiry;
        set => this.Expiry = value;
    }

    public bool IsRevoked { get; set; }

    public string IpAddress { get; set; }

    public string UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}
