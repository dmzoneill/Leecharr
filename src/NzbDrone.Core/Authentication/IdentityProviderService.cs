// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Authentication;

public class IdentityProviderService : IIdentityProviderService
{
    private readonly IIdentityProviderRepository repository;
    private readonly Logger logger;

    public IdentityProviderService(
        IIdentityProviderRepository repository,
        Logger logger)
    {
        this.repository = repository;
        this.logger = logger;
    }

    public List<IdentityProviderDefinition> GetAll()
    {
        return this.repository.All().ToList();
    }

    public List<IdentityProviderDefinition> GetEnabled()
    {
        return this.repository.GetEnabled().ToList();
    }

    public IdentityProviderDefinition GetById(int id)
    {
        return this.repository.Get(id);
    }

    public IdentityProviderDefinition GetByProviderId(string providerId)
    {
        return this.repository.FindByProviderId(providerId);
    }

    public IdentityProviderDefinition Add(IdentityProviderDefinition provider)
    {
        provider.CreatedAt = DateTime.UtcNow;
        provider.UpdatedAt = DateTime.UtcNow;
        return this.repository.Insert(provider);
    }

    public IdentityProviderDefinition Update(IdentityProviderDefinition provider)
    {
        provider.UpdatedAt = DateTime.UtcNow;
        this.repository.Update(provider);
        return provider;
    }

    public void Delete(int id)
    {
        this.repository.Delete(id);
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
                _ => provider.IssuerUrl,
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
            this.logger.Warn(ex, "Failed to test connection to identity provider {0}", provider.Name);
            return false;
        }
    }
}
