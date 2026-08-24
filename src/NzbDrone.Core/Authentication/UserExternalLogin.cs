using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class UserExternalLogin : ModelBase
{
    public int UserId { get; set; }
    public string LoginProvider { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string ProviderDisplayName { get; set; }
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
