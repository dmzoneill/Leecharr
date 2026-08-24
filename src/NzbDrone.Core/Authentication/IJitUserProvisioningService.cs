using System.Collections.Generic;

namespace NzbDrone.Core.Authentication;

public class ExternalUserProfile
{
    public string ProviderId { get; set; }
    public string SubjectId { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string DisplayName { get; set; }
    public IReadOnlyList<string> RawGroups { get; set; }
    public string AvatarUrl { get; set; }

    public ExternalUserProfile(
        string providerId,
        string subjectId,
        string username,
        string email = null,
        string displayName = null,
        IReadOnlyList<string> rawGroups = null,
        string avatarUrl = null)
    {
        ProviderId = providerId;
        SubjectId = subjectId;
        Username = username;
        Email = email;
        DisplayName = displayName;
        RawGroups = rawGroups;
        AvatarUrl = avatarUrl;
    }
}

public interface IJitUserProvisioningService
{
    User ProvisionOrUpdateUser(ExternalUserProfile profile);
}
