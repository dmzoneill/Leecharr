using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class UserSession : ModelBase
{
    public int UserId { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; }
    public DateTime Expiry { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}
