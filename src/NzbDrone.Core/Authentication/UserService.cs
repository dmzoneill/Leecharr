// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.Authentication;

public class UserService : IUserService
{
    private const int SaltByteSize = 16;
    private const int HashByteSize = 32;
    private const int DefaultIterations = 100000;

    private readonly IUserRepository userRepository;
    private readonly Logger logger;

    public UserService(IUserRepository userRepository, Logger logger)
    {
        this.userRepository = userRepository;
        this.logger = logger;
    }

    public User Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var user = this.userRepository.FindByUsername(username);
        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || string.IsNullOrEmpty(user.Salt))
        {
            return null;
        }

        if (!this.VerifyPassword(password, user.PasswordHash, user.Salt, user.Iterations))
        {
            this.logger.Warn("Failed login attempt for username: {0}", username);
            return null;
        }

        user.LastLogin = DateTime.UtcNow;
        this.userRepository.Update(user);
        return user;
    }

    public User CreateUser(string username, string password, string email = null, string displayName = null, List<string> roles = null)
    {
        var existing = this.userRepository.FindByUsername(username);
        if (existing != null)
        {
            throw new InvalidOperationException($"User with username '{username}' already exists.");
        }

        var passwordHash = this.HashPassword(password, out var salt);
        var effectiveRoles = roles ?? (this.HasAnyUsers() ? new List<string> { "User" } : new List<string> { "Admin" });

        var user = new User
        {
            Identifier = Guid.NewGuid(),
            Username = username.Trim(),
            PasswordHash = passwordHash,
            Salt = salt,
            Iterations = DefaultIterations,
            Email = email?.Trim(),
            DisplayName = displayName?.Trim() ?? username.Trim(),
            Roles = JsonSerializer.Serialize(effectiveRoles),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        return this.userRepository.Insert(user);
    }

    public User GetById(int id)
    {
        return this.userRepository.Get(id);
    }

    public User GetByIdentifier(Guid identifier)
    {
        return this.userRepository.FindByIdentifier(identifier);
    }

    public User GetByUsername(string username)
    {
        return this.userRepository.FindByUsername(username);
    }

    public List<User> GetAll()
    {
        return this.userRepository.All().ToList();
    }

    public User Update(User user)
    {
        user.UpdatedAt = DateTime.UtcNow;
        this.userRepository.Update(user);
        return user;
    }

    public void UpdatePassword(int userId, string newPassword)
    {
        var user = this.userRepository.Get(userId);
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {userId} not found.");
        }

        user.PasswordHash = this.HashPassword(newPassword, out var salt);
        user.Salt = salt;
        user.Iterations = DefaultIterations;
        user.UpdatedAt = DateTime.UtcNow;

        this.userRepository.Update(user);
    }

    public void Delete(int id)
    {
        this.userRepository.Delete(id);
    }

    public string HashPassword(string password, out string salt)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltByteSize);
        salt = Convert.ToBase64String(saltBytes);

        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, DefaultIterations, HashAlgorithmName.SHA256, HashByteSize);
        return Convert.ToBase64String(hashBytes);
    }

    public bool VerifyPassword(string password, string hash, string salt, int iterations)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expectedHashBytes = Convert.FromBase64String(hash);

            var actualHashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, iterations, HashAlgorithmName.SHA256, expectedHashBytes.Length);

            return CryptographicOperations.FixedTimeEquals(actualHashBytes, expectedHashBytes);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error validating password hash");
            return false;
        }
    }

    public bool HasAnyUsers()
    {
        return this.userRepository.GetUserCount() > 0;
    }
}
