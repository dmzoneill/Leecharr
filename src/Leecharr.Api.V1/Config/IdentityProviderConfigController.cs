// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation;
using Leecharr.Http;
using Leecharr.Http.Authentication;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;

namespace Leecharr.Api.V1.Config;

public class IdentityProviderResource : RestResource
{
    public string ProviderId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public IdentityProviderType ProviderType { get; set; } = IdentityProviderType.Oidc;

    public bool IsEnabled { get; set; } = true;

    public string ClientId { get; set; }

    public string ClientSecret { get; set; }

    public string IssuerUrl { get; set; }

    public string MetadataUrl { get; set; }

    public string Scopes { get; set; } = "openid profile email";

    public string Certificate { get; set; }

    public string RoleMappingRules { get; set; }

    public string IconUrl { get; set; }

    public string ButtonText { get; set; }
}

[V1ApiController("config/auth/providers")]
public class IdentityProviderConfigController : RestController<IdentityProviderResource>
{
    private readonly IIdentityProviderService providerService;
    private readonly IDynamicAuthSchemeManager dynamicAuthManager;

    public IdentityProviderConfigController(
        IIdentityProviderService providerService,
        IDynamicAuthSchemeManager dynamicAuthManager)
    {
        this.providerService = providerService;
        this.dynamicAuthManager = dynamicAuthManager;

        this.SharedValidator = new ResourceValidator<IdentityProviderResource>();
        this.SharedValidator.RuleFor(c => c.ProviderId).NotEmpty();
        this.SharedValidator.RuleFor(c => c.Name).NotEmpty();
    }

    [HttpGet]
    public ActionResult<List<IdentityProviderResource>> GetAll()
    {
        var providers = this.providerService.GetAll();
        var resources = new List<IdentityProviderResource>();

        foreach (var p in providers)
        {
            resources.Add(ToResource(p));
        }

        return this.Ok(resources);
    }

    [HttpGet("{id:int}")]
    public ActionResult<IdentityProviderResource> GetById(int id)
    {
        var provider = this.providerService.GetById(id);
        if (provider == null)
        {
            return this.NotFound();
        }

        return this.Ok(ToResource(provider));
    }

    [HttpPost]
    public ActionResult<IdentityProviderResource> Create([FromBody] IdentityProviderResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var model = ToModel(resource);
        var created = this.providerService.Add(model);

        if (created.IsEnabled)
        {
            _ = this.dynamicAuthManager.RegisterOrUpdateOidcProviderAsync(created);
        }

        return this.Created($"/api/v1/config/auth/providers/{created.Id}", ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<IdentityProviderResource> Update(int id, [FromBody] IdentityProviderResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var existing = this.providerService.GetById(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;

        // If client secret is masked (e.g. "******"), preserve original
        if (string.IsNullOrEmpty(model.ClientSecretEncrypted) || model.ClientSecretEncrypted.Contains('*'))
        {
            model.ClientSecretEncrypted = existing.ClientSecretEncrypted;
        }

        var updated = this.providerService.Update(model);

        if (updated.IsEnabled)
        {
            _ = this.dynamicAuthManager.RegisterOrUpdateOidcProviderAsync(updated);
        }
        else
        {
            _ = this.dynamicAuthManager.RemoveProviderSchemeAsync(updated.ProviderId);
        }

        return this.Ok(ToResource(updated));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        var existing = this.providerService.GetById(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        this.providerService.Delete(id);
        _ = this.dynamicAuthManager.RemoveProviderSchemeAsync(existing.ProviderId);

        return this.NoContent();
    }

    [HttpPost("test")]
    public async Task<ActionResult> TestConnection([FromBody] IdentityProviderResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var model = ToModel(resource);
        var success = await this.providerService.TestConnectionAsync(model);

        return this.Ok(new { success, message = success ? "Connection successful" : "Failed to reach provider endpoint" });
    }

    private static IdentityProviderResource ToResource(IdentityProviderDefinition model)
    {
        return new IdentityProviderResource
        {
            Id = model.Id,
            ProviderId = model.ProviderId,
            Name = model.Name,
            ProviderType = model.ProviderType,
            IsEnabled = model.IsEnabled,
            ClientId = model.ClientId,
            ClientSecret = string.IsNullOrEmpty(model.ClientSecretEncrypted) ? null : "********",
            IssuerUrl = model.IssuerUrl,
            MetadataUrl = model.MetadataUrl,
            Scopes = model.Scopes,
            Certificate = model.Certificate,
            RoleMappingRules = model.RoleMappingRules,
            IconUrl = model.IconUrl,
            ButtonText = model.ButtonText,
        };
    }

    private static IdentityProviderDefinition ToModel(IdentityProviderResource resource)
    {
        return new IdentityProviderDefinition
        {
            Id = resource.Id,
            ProviderId = resource.ProviderId,
            Name = resource.Name,
            ProviderType = resource.ProviderType,
            IsEnabled = resource.IsEnabled,
            ClientId = resource.ClientId,
            ClientSecretEncrypted = resource.ClientSecret,
            IssuerUrl = resource.IssuerUrl,
            MetadataUrl = resource.MetadataUrl,
            Scopes = resource.Scopes,
            Certificate = resource.Certificate,
            RoleMappingRules = resource.RoleMappingRules,
            IconUrl = resource.IconUrl,
            ButtonText = resource.ButtonText,
        };
    }
}
