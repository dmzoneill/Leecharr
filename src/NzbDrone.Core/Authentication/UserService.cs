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

    private readonly IUserRepository _userRepository;
    private readonly Logger _logger;

    public UserService(IUserRepository userRepository, Logger logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public User Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var user = _userRepository.FindByUsername(username);
        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || string.IsNullOrEmpty(user.Salt))
        {
            return null;
        }

        if (!VerifyPassword(password, user.PasswordHash, user.Salt, user.Iterations))
        {
            _logger.Warn("Failed login attempt for username: {0}", username);
            return null;
        }

        user.LastLogin = DateTime.UtcNow;
        _userRepository.Update(user);
        return user;
    }

    public User CreateUser(string username, string password, string email = null, string displayName = null, List<string> roles = null)
    {
        var existing = _userRepository.FindByUsername(username);
        if (existing != null)
        {
            throw new InvalidOperationException($"User with username '{username}' already exists.");
        }

        var passwordHash = HashPassword(password, out var salt);
        var effectiveRoles = roles ?? (HasAnyUsers() ? new List<string> { "User" } : new List<string> { "Admin" });

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
            UpdatedAt = DateTime.UtcNow
        };

        return _userRepository.Insert(user);
    }

    public User GetById(int id)
    {
        return _userRepository.Get(id);
    }

    public User GetByIdentifier(Guid identifier)
    {
        return _userRepository.FindByIdentifier(identifier);
    }

    public User GetByUsername(string username)
    {
        return _userRepository.FindByUsername(username);
    }

    public List<User> GetAll()
    {
        return _userRepository.All().ToList();
    }

    public User Update(User user)
    {
        user.UpdatedAt = DateTime.UtcNow;
        _userRepository.Update(user);
        return user;
    }

    public void UpdatePassword(int userId, string newPassword)
    {
        var user = _userRepository.Get(userId);
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {userId} not found.");
        }

        user.PasswordHash = HashPassword(newPassword, out var salt);
        user.Salt = salt;
        user.Iterations = DefaultIterations;
        user.UpdatedAt = DateTime.UtcNow;

        _userRepository.Update(user);
    }

    public void Delete(int id)
    {
        _userRepository.Delete(id);
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
            _logger.Error(ex, "Error validating password hash");
            return false;
        }
    }

    public bool HasAnyUsers()
    {
        return _userRepository.GetUserCount() > 0;
    }
}
