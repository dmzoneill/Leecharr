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

    [Test]
    public void ProvisionOrUpdateUser_LocalUserCollision_ShouldNotHijackAndDisambiguateUsername()
    {
        var localAdmin = new User
        {
            Id = 1,
            Username = "admin",
            Email = "admin@example.com",
            Roles = "[\"Admin\"]",
            ExternalProviderId = null,
            ExternalSubjectId = null,
        };
        this.userRepository.Insert(localAdmin);

        var profile = new ExternalUserProfile(
            "authentik",
            "sub-attacker",
            "admin",
            "admin@example.com",
            "Attacker",
            new List<string> { "users" });

        var result = this.jitService.ProvisionOrUpdateUser(profile);

        Assert.That(result.Id, Is.Not.EqualTo(1));
        Assert.That(result.Username, Is.EqualTo("admin_authentik"));
        Assert.That(result.ExternalProviderId, Is.EqualTo("authentik"));
        Assert.That(result.ExternalSubjectId, Is.EqualTo("sub-attacker"));

        var adminAfter = this.userRepository.Get(1);
        Assert.That(adminAfter.ExternalProviderId, Is.Null);
        Assert.That(adminAfter.ExternalSubjectId, Is.Null);
        Assert.That(adminAfter.Username, Is.EqualTo("admin"));
    }

    [Test]
    public void ProvisionOrUpdateUser_CrossProviderCollision_ShouldNotOverwriteAndDisambiguateUsername()
    {
        var githubUser = new User
        {
            Id = 10,
            Username = "alice",
            Email = "alice@example.com",
            ExternalProviderId = "github",
            ExternalSubjectId = "gh-123",
            Roles = "[\"User\"]",
        };
        this.userRepository.Insert(githubUser);

        var googleProfile = new ExternalUserProfile(
            "google",
            "goog-456",
            "alice",
            "alice@example.com",
            "Alice Google",
            new List<string> { "users" });

        var result = this.jitService.ProvisionOrUpdateUser(googleProfile);

        Assert.That(result.Id, Is.Not.EqualTo(10));
        Assert.That(result.Username, Is.EqualTo("alice_google"));
        Assert.That(result.ExternalProviderId, Is.EqualTo("google"));
        Assert.That(result.ExternalSubjectId, Is.EqualTo("goog-456"));

        var githubAfter = this.userRepository.Get(10);
        Assert.That(githubAfter.ExternalProviderId, Is.EqualTo("github"));
        Assert.That(githubAfter.ExternalSubjectId, Is.EqualTo("gh-123"));
        Assert.That(githubAfter.Username, Is.EqualTo("alice"));
    }

    [Test]
    public void ProvisionOrUpdateUser_MultipleCollisions_ShouldAppendNumericSuffix()
    {
        this.userRepository.Insert(new User { Id = 1, Username = "bob", ExternalProviderId = "local" });
        this.userRepository.Insert(new User { Id = 2, Username = "bob_google", ExternalProviderId = "google-other" });

        var profile = new ExternalUserProfile(
            "google",
            "goog-789",
            "bob",
            "bob@example.com",
            "Bob",
            new List<string> { "users" });

        var result = this.jitService.ProvisionOrUpdateUser(profile);

        Assert.That(result.Username, Is.EqualTo("bob_google_2"));
    }

    [Test]
    public void ProvisionOrUpdateUser_SameProviderMatchingEmailOrUsername_ShouldLinkAccount()
    {
        var unlinkedSameProviderUser = new User
        {
            Id = 5,
            Username = "carol",
            Email = "carol@example.com",
            ExternalProviderId = "keycloak",
            ExternalSubjectId = null,
            Roles = "[\"User\"]",
        };
        this.userRepository.Insert(unlinkedSameProviderUser);

        var profile = new ExternalUserProfile(
            "keycloak",
            "kc-sub-new",
            "carol",
            "carol@example.com",
            "Carol K",
            new List<string> { "users" });

        var result = this.jitService.ProvisionOrUpdateUser(profile);

        Assert.That(result.Id, Is.EqualTo(5));
        Assert.That(result.ExternalSubjectId, Is.EqualTo("kc-sub-new"));
        Assert.That(result.Username, Is.EqualTo("carol"));
    }

    [TestCase("https://example.com/avatar.png", "https://example.com/avatar.png")]
    [TestCase("http://cdn.example.org/pic.jpg", "http://cdn.example.org/pic.jpg")]
    [TestCase("javascript:alert(1)", null)]
    [TestCase("data:image/png;base64,iVBORw0KGgoAAAANSUhEUg", null)]
    [TestCase("ftp://example.com/pic.png", null)]
    [TestCase("/relative/path.png", null)]
    [TestCase("not a valid uri", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void ProvisionOrUpdateUser_AvatarUrlSanitization_ShouldOnlyAcceptHttpAndHttps(string inputAvatarUrl, string expectedAvatarUrl)
    {
        var profile = new ExternalUserProfile(
            "authentik",
            $"sub-{Guid.NewGuid()}",
            $"user_{Guid.NewGuid():N}",
            "test@example.com",
            "Test User",
            new List<string> { "users" },
            inputAvatarUrl);

        var result = this.jitService.ProvisionOrUpdateUser(profile);

        Assert.That(result.AvatarUrl, Is.EqualTo(expectedAvatarUrl));
    }

    [Test]
    public void ProvisionOrUpdateUser_ExistingUser_InvalidAvatarUrlInProfile_ShouldNotOverwriteValidAvatar()
    {
        var existingUser = new User
        {
            Id = 88,
            Username = "dave",
            ExternalProviderId = "authentik",
            ExternalSubjectId = "sub-88",
            AvatarUrl = "https://example.com/valid.png",
            Roles = "[\"User\"]",
        };
        this.userRepository.Insert(existingUser);

        var profile = new ExternalUserProfile(
            "authentik",
            "sub-88",
            "dave",
            "dave@example.com",
            "Dave",
            new List<string> { "users" },
            "javascript:alert(1)");

        var result = this.jitService.ProvisionOrUpdateUser(profile);

        Assert.That(result.Id, Is.EqualTo(88));
        Assert.That(result.AvatarUrl, Is.EqualTo("https://example.com/valid.png"));
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
            else if (model.Id >= this.nextId)
            {
                this.nextId = model.Id + 1;
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
