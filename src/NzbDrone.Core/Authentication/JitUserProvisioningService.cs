// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.Authentication;

public class JitUserProvisioningService : IJitUserProvisioningService
{
    private readonly IUserRepository userRepository;
    private readonly IIdentityProviderRepository identityProviderRepository;
    private readonly IClaimsRoleMappingService roleMapper;
    private readonly Logger logger;

    public JitUserProvisioningService(
        IUserRepository userRepository,
        IIdentityProviderRepository identityProviderRepository,
        IClaimsRoleMappingService roleMapper,
        Logger logger)
    {
        this.userRepository = userRepository;
        this.identityProviderRepository = identityProviderRepository;
        this.roleMapper = roleMapper;
        this.logger = logger;
    }

    public User ProvisionOrUpdateUser(ExternalUserProfile profile)
    {
        var isFirstUser = this.userRepository.GetUserCount() == 0;
        var provider = this.identityProviderRepository.FindByProviderId(profile.ProviderId);

        // 1. Check if user already exists by external ID
        var existingUser = this.userRepository.FindByExternalId(profile.ProviderId, profile.SubjectId);
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

            var sanitizedAvatar = SanitizeAvatarUrl(profile.AvatarUrl);
            if (!string.IsNullOrEmpty(sanitizedAvatar))
            {
                existingUser.AvatarUrl = sanitizedAvatar;
            }

            // Recalculate roles if groups provided
            if (profile.RawGroups != null && profile.RawGroups.Count > 0)
            {
                var roles = this.roleMapper.ResolveRoles(provider, profile.RawGroups, false);
                existingUser.Roles = JsonSerializer.Serialize(roles);
            }

            existingUser.UpdatedAt = DateTime.UtcNow;
            this.userRepository.Update(existingUser);
            return existingUser;
        }

        // 2. Check if user matches existing username or email from the SAME external provider
        User matchedUser = null;
        if (!string.IsNullOrEmpty(profile.Email))
        {
            matchedUser = this.userRepository.FindByEmail(profile.Email);
        }

        if (matchedUser == null && !string.IsNullOrEmpty(profile.Username))
        {
            matchedUser = this.userRepository.FindByUsername(profile.Username);
        }

        if (matchedUser != null &&
            !string.IsNullOrEmpty(matchedUser.ExternalProviderId) &&
            string.Equals(matchedUser.ExternalProviderId, profile.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            matchedUser.ExternalSubjectId = profile.SubjectId;
            matchedUser.LastLogin = DateTime.UtcNow;
            var sanitizedAvatar = SanitizeAvatarUrl(profile.AvatarUrl);
            if (!string.IsNullOrEmpty(sanitizedAvatar))
            {
                matchedUser.AvatarUrl = sanitizedAvatar;
            }

            if (profile.RawGroups != null && profile.RawGroups.Count > 0)
            {
                var roles = this.roleMapper.ResolveRoles(provider, profile.RawGroups, false);
                matchedUser.Roles = JsonSerializer.Serialize(roles);
            }

            matchedUser.UpdatedAt = DateTime.UtcNow;
            this.userRepository.Update(matchedUser);
            this.logger.Info("Linked external {0} login to existing user {1}", profile.ProviderId, matchedUser.Username);
            return matchedUser;
        }

        // 3. JIT Provision new user with disambiguated username if collision exists
        var baseUsername = string.IsNullOrWhiteSpace(profile.Username)
            ? (!string.IsNullOrWhiteSpace(profile.Email) ? profile.Email.Split('@')[0].Trim() : "user")
            : profile.Username.Trim();

        var assignedRoles = this.roleMapper.ResolveRoles(provider, profile.RawGroups, isFirstUser);
        var disambiguatedUsername = this.DisambiguateUsername(baseUsername, profile.ProviderId);

        var newUser = new User
        {
            Identifier = Guid.NewGuid(),
            Username = disambiguatedUsername,
            Email = profile.Email?.Trim(),
            DisplayName = profile.DisplayName?.Trim() ?? profile.Username?.Trim() ?? disambiguatedUsername,
            AvatarUrl = SanitizeAvatarUrl(profile.AvatarUrl),
            ExternalProviderId = profile.ProviderId,
            ExternalSubjectId = profile.SubjectId,
            Roles = JsonSerializer.Serialize(assignedRoles),
            LastLogin = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var created = this.userRepository.Insert(newUser);
        this.logger.Info("JIT provisioned new user {0} via {1}", created.Username, profile.ProviderId);
        return created;
    }

    private static string SanitizeAvatarUrl(string avatarUrl)
    {
        if (!string.IsNullOrWhiteSpace(avatarUrl) &&
            Uri.TryCreate(avatarUrl.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.ToString();
        }

        return null;
    }

    private string DisambiguateUsername(string desiredUsername, string providerId)
    {
        var trimmed = desiredUsername.Trim();
        if (this.userRepository.FindByUsername(trimmed) == null)
        {
            return trimmed;
        }

        var candidate = $"{trimmed}_{providerId}";
        if (this.userRepository.FindByUsername(candidate) == null)
        {
            return candidate;
        }

        var suffix = 2;
        while (this.userRepository.FindByUsername($"{candidate}_{suffix}") != null)
        {
            suffix++;
        }

        return $"{candidate}_{suffix}";
    }
}
