// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class UserServiceTest
{
    private InMemoryUserRepository userRepository;
    private Logger logger;
    private UserService userService;

    [SetUp]
    public void SetUp()
    {
        this.userRepository = new InMemoryUserRepository();
        this.logger = LogManager.GetCurrentClassLogger();
        this.userService = new UserService(this.userRepository, this.logger);
    }

    [Test]
    public void HashPassword_ShouldGenerateSaltAndHash()
    {
        var password = "SecurePassword123!";
        var hash = this.userService.HashPassword(password, out var salt);

        Assert.That(hash, Is.Not.Null.And.Not.Empty);
        Assert.That(salt, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void VerifyPassword_WithCorrectPassword_ShouldReturnTrue()
    {
        var password = "CorrectHorseBatteryStaple";
        var hash = this.userService.HashPassword(password, out var salt);

        var result = this.userService.VerifyPassword(password, hash, salt, 100000);

        Assert.That(result, Is.True);
    }

    [Test]
    public void VerifyPassword_WithIncorrectPassword_ShouldReturnFalse()
    {
        var password = "CorrectPassword";
        var hash = this.userService.HashPassword(password, out var salt);

        var result = this.userService.VerifyPassword("WrongPassword", hash, salt, 100000);

        Assert.That(result, Is.False);
    }

    [Test]
    public void CreateUser_FirstUser_ShouldBeAdmin()
    {
        var user = this.userService.CreateUser("admin", "AdminPassword123!", "admin@example.com", "Admin User");

        Assert.That(user.Username, Is.EqualTo("admin"));
        Assert.That(user.Roles, Does.Contain("Admin"));
    }

    [Test]
    public void Authenticate_WithValidCredentials_ShouldReturnUser()
    {
        var user = this.userService.CreateUser("jdoe", "MySecretPassword", "jdoe@example.com", "John Doe");

        var authenticated = this.userService.Authenticate("jdoe", "MySecretPassword");

        Assert.That(authenticated, Is.Not.Null);
        Assert.That(authenticated.Username, Is.EqualTo("jdoe"));
        Assert.That(authenticated.LastLogin, Is.Not.Null);
    }

    [Test]
    public void Authenticate_WithInvalidPassword_ShouldReturnNull()
    {
        this.userService.CreateUser("jdoe", "RealPassword", "jdoe@example.com", "John Doe");

        var authenticated = this.userService.Authenticate("jdoe", "WrongPassword");

        Assert.That(authenticated, Is.Null);
    }

    private class InMemoryUserRepository : IUserRepository
    {
        private readonly List<User> users = new List<User>();
        private int nextId = 1;

        public User Get(int id)
        {
            return this.users.FirstOrDefault(u => u.Id == id);
        }

        public IEnumerable<User> All()
        {
            return this.users.ToList();
        }

        public User Insert(User model)
        {
            model.Id = this.nextId++;
            this.users.Add(model);
            return model;
        }

        public User Update(User model)
        {
            var idx = this.users.FindIndex(u => u.Id == model.Id);
            if (idx >= 0)
            {
                this.users[idx] = model;
            }

            return model;
        }

        public void Delete(int id)
        {
            this.users.RemoveAll(u => u.Id == id);
        }

        public void Delete(User model)
        {
            this.Delete(model.Id);
        }

        public void DeleteMany(IEnumerable<int> ids)
        {
            this.users.RemoveAll(u => ids.Contains(u.Id));
        }

        public void InsertMany(IList<User> models)
        {
            foreach (var m in models)
            {
                this.Insert(m);
            }
        }

        public void UpdateMany(IList<User> models)
        {
            foreach (var m in models)
            {
                this.Update(m);
            }
        }

        public void Purge()
        {
            this.users.Clear();
        }

        public int Count()
        {
            return this.users.Count;
        }

        public User FindByUsername(string username)
        {
            return this.users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        public User FindByEmail(string email)
        {
            return this.users.FirstOrDefault(u => u.Email != null && u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        }

        public User FindByIdentifier(Guid identifier)
        {
            return this.users.FirstOrDefault(u => u.Identifier == identifier);
        }

        public User FindByExternalId(string providerId, string externalSubjectId)
        {
            return this.users.FirstOrDefault(u => u.ExternalProviderId == providerId && u.ExternalSubjectId == externalSubjectId);
        }

        public int GetUserCount()
        {
            return this.users.Count;
        }
    }
}
