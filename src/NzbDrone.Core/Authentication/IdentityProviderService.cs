using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Authentication;

public class IdentityProviderService : IIdentityProviderService
{
    private readonly IIdentityProviderRepository _repository;
    private readonly Logger _logger;

    public IdentityProviderService(
        IIdentityProviderRepository repository,
        Logger logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public List<IdentityProviderDefinition> GetAll()
    {
        return _repository.All().ToList();
    }

    public List<IdentityProviderDefinition> GetEnabled()
    {
        return _repository.GetEnabled().ToList();
    }

    public IdentityProviderDefinition GetById(int id)
    {
        return _repository.Get(id);
    }

    public IdentityProviderDefinition GetByProviderId(string providerId)
    {
        return _repository.FindByProviderId(providerId);
    }

    public IdentityProviderDefinition Add(IdentityProviderDefinition provider)
    {
        provider.CreatedAt = DateTime.UtcNow;
        provider.UpdatedAt = DateTime.UtcNow;
        return _repository.Insert(provider);
    }

    public IdentityProviderDefinition Update(IdentityProviderDefinition provider)
    {
        provider.UpdatedAt = DateTime.UtcNow;
        _repository.Update(provider);
        return provider;
    }

    public void Delete(int id)
    {
        _repository.Delete(id);
    }

    public async Task<bool> TestConnectionAsync(IdentityProviderDefinition provider)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var targetUrl = provider.ProviderType switch
            {
                IdentityProviderType.Oidc => !string.IsNullOrEmpty(provider.IssuerUrl)
                    ? (provider.IssuerUrl.EndsWith("/") ? provider.IssuerUrl + ".well-known/openid-configuration" : provider.IssuerUrl + "/.well-known/openid-configuration")
                    : null,
                IdentityProviderType.Saml => provider.MetadataUrl,
                _ => provider.IssuerUrl
            };

            if (string.IsNullOrEmpty(targetUrl))
            {
                return true;
            }

            var response = await client.GetAsync(targetUrl);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to test connection to identity provider {0}", provider.Name);
            return false;
        }
    }
}
