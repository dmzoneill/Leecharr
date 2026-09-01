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
    private InMemoryUserRepository _userRepository;
    private InMemoryIdentityProviderRepository _idpRepository;
    private StubClaimsRoleMappingService _roleMapper;
    private Logger _logger;
    private JitUserProvisioningService _jitService;

    [SetUp]
    public void SetUp()
    {
        _userRepository = new InMemoryUserRepository();
        _idpRepository = new InMemoryIdentityProviderRepository();
        _roleMapper = new StubClaimsRoleMappingService();
        _logger = LogManager.GetCurrentClassLogger();

        _jitService = new JitUserProvisioningService(
            _userRepository,
            _idpRepository,
            _roleMapper,
            _logger);
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

        var result = _jitService.ProvisionOrUpdateUser(profile);

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
            Roles = "[\"User\"]"
        };
        _userRepository.Insert(existingUser);

        var profile = new ExternalUserProfile(
            "keycloak",
            "kc-sub-999",
            "jsmith",
            "jsmith@example.com",
            "John Smith",
            new List<string> { "users" });

        var result = _jitService.ProvisionOrUpdateUser(profile);

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
        private readonly List<IdentityProviderDefinition> _providers = new List<IdentityProviderDefinition>();

        public IdentityProviderDefinition Get(int id)
        {
            return _providers.FirstOrDefault(p => p.Id == id);
        }

        public IEnumerable<IdentityProviderDefinition> All()
        {
            return _providers.ToList();
        }

        public IdentityProviderDefinition Insert(IdentityProviderDefinition model)
        {
            _providers.Add(model);
            return model;
        }

        public IdentityProviderDefinition Update(IdentityProviderDefinition model)
        {
            return model;
        }

        public void Delete(int id)
        {
            _providers.RemoveAll(p => p.Id == id);
        }

        public void Delete(IdentityProviderDefinition model)
        {
            Delete(model.Id);
        }

        public void DeleteMany(IEnumerable<int> ids)
        {
            _providers.RemoveAll(p => ids.Contains(p.Id));
        }

        public void InsertMany(IList<IdentityProviderDefinition> models)
        {
            _providers.AddRange(models);
        }

        public void UpdateMany(IList<IdentityProviderDefinition> models)
        {
        }

        public void Purge()
        {
            _providers.Clear();
        }

        public int Count()
        {
            return _providers.Count;
        }

        public IEnumerable<IdentityProviderDefinition> GetEnabled()
        {
            return _providers.Where(p => p.IsEnabled);
        }

        public IdentityProviderDefinition FindByProviderId(string providerId)
        {
            return _providers.FirstOrDefault(p => p.ProviderId == providerId);
        }
    }

    private class InMemoryUserRepository : IUserRepository
    {
        private readonly List<User> _users = new List<User>();
        private int _nextId = 1;

        public User Get(int id)
        {
            return _users.FirstOrDefault(u => u.Id == id);
        }

        public IEnumerable<User> All()
        {
            return _users.ToList();
        }

        public User Insert(User model)
        {
            if (model.Id == 0)
            {
                model.Id = _nextId++;
            }

            _users.Add(model);
            return model;
        }

        public User Update(User model)
        {
            var idx = _users.FindIndex(u => u.Id == model.Id);
            if (idx >= 0)
            {
                _users[idx] = model;
            }

            return model;
        }

        public void Delete(int id)
        {
            _users.RemoveAll(u => u.Id == id);
        }

        public void Delete(User model)
        {
            Delete(model.Id);
        }

        public void DeleteMany(IEnumerable<int> ids)
        {
            _users.RemoveAll(u => ids.Contains(u.Id));
        }

        public void InsertMany(IList<User> models)
        {
            foreach (var m in models)
            {
                Insert(m);
            }
        }

        public void UpdateMany(IList<User> models)
        {
            foreach (var m in models)
            {
                Update(m);
            }
        }

        public void Purge()
        {
            _users.Clear();
        }

        public int Count()
        {
            return _users.Count;
        }

        public User FindByUsername(string username)
        {
            return _users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        public User FindByEmail(string email)
        {
            return _users.FirstOrDefault(u => u.Email != null && u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        }

        public User FindByIdentifier(Guid identifier)
        {
            return _users.FirstOrDefault(u => u.Identifier == identifier);
        }

        public User FindByExternalId(string providerId, string externalSubjectId)
        {
            return _users.FirstOrDefault(u => u.ExternalProviderId == providerId && u.ExternalSubjectId == externalSubjectId);
        }

        public int GetUserCount()
        {
            return _users.Count;
        }
    }
}
