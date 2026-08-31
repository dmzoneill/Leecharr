// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class JitUserProvisioningServiceTest
{
    private InMemoryUserRepository userRepository;
    private InMemoryIdentityProviderRepository idpRepository;
    private StubClaimsRoleMappingService roleMapper;
    private Logger logger;
    private JitUserProvisioningService jitService;

    [SetUp]
    public void SetUp()
    {
        this.userRepository = new InMemoryUserRepository();
        this.idpRepository = new InMemoryIdentityProviderRepository();
        this.roleMapper = new StubClaimsRoleMappingService();
        this.logger = LogManager.GetCurrentClassLogger();

        this.jitService = new JitUserProvisioningService(
            this.userRepository,
            this.idpRepository,
            this.roleMapper,
            this.logger);
    }

    [Test]
    public void ProvisionOrUpdateUser_NewUser_ShouldCreateUser()
    {
        var profile = new ExternalUserProfile(
            "authentik",
            "sub-12345",
            "amercer",
            "amercer@example.com",
            "Alex Mercer",
            new List<string> { "leecharr-admins" },
            "https://example.com/avatar.jpg");

        var result = this.jitService.ProvisionOrUpdateUser(profile);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Username, Is.EqualTo("amercer"));
        Assert.That(result.Email, Is.EqualTo("amercer@example.com"));
        Assert.That(result.ExternalProviderId, Is.EqualTo("authentik"));
        Assert.That(result.ExternalSubjectId, Is.EqualTo("sub-12345"));
        Assert.That(result.Roles, Does.Contain("Admin"));
    }

    [Test]
    public void ProvisionOrUpdateUser_ExistingExternalUser_ShouldUpdateLastLogin()
    {
        var existingUser = new User
        {
            Id = 42,
            Username = "jsmith",
            ExternalProviderId = "keycloak",
            ExternalSubjectId = "kc-sub-999",
            Roles = "[\"User\"]",
        };
        this.userRepository.Insert(existingUser);

        var profile = new ExternalUserProfile(
            "keycloak",
            "kc-sub-999",
            "jsmith",
            "jsmith@example.com",
            "John Smith",
            new List<string> { "users" });

        var result = this.jitService.ProvisionOrUpdateUser(profile);

        Assert.That(result.Id, Is.EqualTo(42));
        Assert.That(result.LastLogin, Is.Not.Null);
    }

    private class StubClaimsRoleMappingService : IClaimsRoleMappingService
    {
        public List<string> ResolveRoles(IdentityProviderDefinition provider, IReadOnlyList<string> rawGroups, bool isFirstUser)
        {
            if (isFirstUser || (rawGroups != null && rawGroups.Contains("leecharr-admins")))
            {
                return new List<string> { "Admin" };
            }

            return new List<string> { "User" };
        }
    }

    private class InMemoryIdentityProviderRepository : IIdentityProviderRepository
    {
        private readonly List<IdentityProviderDefinition> providers = new List<IdentityProviderDefinition>();

        public IdentityProviderDefinition Get(int id)
        {
            return this.providers.FirstOrDefault(p => p.Id == id);
        }

        public IEnumerable<IdentityProviderDefinition> All()
        {
            return this.providers.ToList();
        }

        public IdentityProviderDefinition Insert(IdentityProviderDefinition model)
        {
            this.providers.Add(model);
            return model;
        }

        public IdentityProviderDefinition Update(IdentityProviderDefinition model)
        {
            return model;
        }

        public void Delete(int id)
        {
            this.providers.RemoveAll(p => p.Id == id);
        }

        public void Delete(IdentityProviderDefinition model)
        {
            this.Delete(model.Id);
        }

        public void DeleteMany(IEnumerable<int> ids)
        {
            this.providers.RemoveAll(p => ids.Contains(p.Id));
        }

        public void InsertMany(IList<IdentityProviderDefinition> models)
        {
            this.providers.AddRange(models);
        }

        public void UpdateMany(IList<IdentityProviderDefinition> models)
        {
        }

        public void Purge()
        {
            this.providers.Clear();
        }

        public int Count()
        {
            return this.providers.Count;
        }

        public IEnumerable<IdentityProviderDefinition> GetEnabled()
        {
            return this.providers.Where(p => p.IsEnabled);
        }

        public IdentityProviderDefinition FindByProviderId(string providerId)
        {
            return this.providers.FirstOrDefault(p => p.ProviderId == providerId);
        }
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
            if (model.Id == 0)
            {
                model.Id = this.nextId++;
            }

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
