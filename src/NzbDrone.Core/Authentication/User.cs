using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class User : ModelBase
{
    public Guid Identifier { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; }
    public string Salt { get; set; }
    public int Iterations { get; set; } = 100000;
    public string Email { get; set; }
    public string DisplayName { get; set; }
    public string Roles { get; set; } = "[\"Admin\"]";
    public string AvatarUrl { get; set; }
    public string ExternalProviderId { get; set; }
    public string ExternalSubjectId { get; set; }
    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
