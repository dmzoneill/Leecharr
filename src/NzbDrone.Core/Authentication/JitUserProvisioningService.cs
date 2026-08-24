using System;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.Authentication;

public class JitUserProvisioningService : IJitUserProvisioningService
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityProviderRepository _identityProviderRepository;
    private readonly IClaimsRoleMappingService _roleMapper;
    private readonly Logger _logger;

    public JitUserProvisioningService(
        IUserRepository userRepository,
        IIdentityProviderRepository identityProviderRepository,
        IClaimsRoleMappingService roleMapper,
        Logger logger)
    {
        _userRepository = userRepository;
        _identityProviderRepository = identityProviderRepository;
        _roleMapper = roleMapper;
        _logger = logger;
    }

    public User ProvisionOrUpdateUser(ExternalUserProfile profile)
    {
        var isFirstUser = _userRepository.GetUserCount() == 0;
        var provider = _identityProviderRepository.FindByProviderId(profile.ProviderId);

        // 1. Check if user already exists by external ID
        var existingUser = _userRepository.FindByExternalId(profile.ProviderId, profile.SubjectId);
        if (existingUser != null)
        {
            existingUser.LastLogin = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(profile.Email))
            {
                existingUser.Email = profile.Email;
            }

            if (!string.IsNullOrEmpty(profile.DisplayName))
            {
                existingUser.DisplayName = profile.DisplayName;
            }

            if (!string.IsNullOrEmpty(profile.AvatarUrl))
            {
                existingUser.AvatarUrl = profile.AvatarUrl;
            }

            // Recalculate roles if groups provided
            if (profile.RawGroups != null && profile.RawGroups.Count > 0)
            {
                var roles = _roleMapper.ResolveRoles(provider, profile.RawGroups, false);
                existingUser.Roles = JsonSerializer.Serialize(roles);
            }

            existingUser.UpdatedAt = DateTime.UtcNow;
            _userRepository.Update(existingUser);
            return existingUser;
        }

        // 2. Check if user matches existing username or email
        User matchedUser = null;
        if (!string.IsNullOrEmpty(profile.Email))
        {
            matchedUser = _userRepository.FindByEmail(profile.Email);
        }

        if (matchedUser == null && !string.IsNullOrEmpty(profile.Username))
        {
            matchedUser = _userRepository.FindByUsername(profile.Username);
        }

        if (matchedUser != null)
        {
            matchedUser.ExternalProviderId = profile.ProviderId;
            matchedUser.ExternalSubjectId = profile.SubjectId;
            matchedUser.LastLogin = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(profile.AvatarUrl))
            {
                matchedUser.AvatarUrl = profile.AvatarUrl;
            }

            if (profile.RawGroups != null && profile.RawGroups.Count > 0)
            {
                var roles = _roleMapper.ResolveRoles(provider, profile.RawGroups, false);
                matchedUser.Roles = JsonSerializer.Serialize(roles);
            }

            matchedUser.UpdatedAt = DateTime.UtcNow;
            _userRepository.Update(matchedUser);
            _logger.Info("Linked external {0} login to existing user {1}", profile.ProviderId, matchedUser.Username);
            return matchedUser;
        }

        // 3. JIT Provision new user
        var assignedRoles = _roleMapper.ResolveRoles(provider, profile.RawGroups, isFirstUser);

        var newUser = new User
        {
            Identifier = Guid.NewGuid(),
            Username = profile.Username.Trim(),
            Email = profile.Email?.Trim(),
            DisplayName = profile.DisplayName?.Trim() ?? profile.Username.Trim(),
            AvatarUrl = profile.AvatarUrl,
            ExternalProviderId = profile.ProviderId,
            ExternalSubjectId = profile.SubjectId,
            Roles = JsonSerializer.Serialize(assignedRoles),
            LastLogin = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = _userRepository.Insert(newUser);
        _logger.Info("JIT provisioned new user {0} via {1}", created.Username, profile.ProviderId);
        return created;
    }
}
