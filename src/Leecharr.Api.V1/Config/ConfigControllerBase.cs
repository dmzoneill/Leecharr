// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Reflection;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public abstract class ConfigController<TResource> : Controller
    where TResource : RestResource, new()
{
    protected readonly IConfigService configService;

    protected ResourceValidator<TResource> SharedValidator { get; set; }

    protected ConfigController(IConfigService configService)
    {
        this.configService = configService;
        this.SharedValidator = new ResourceValidator<TResource>();
    }

    [HttpGet]
    [Produces("application/json")]
    public TResource GetConfig()
    {
        var resource = this.ToResource(this.configService);
        resource.Id = 1;

        return resource;
    }

    [HttpGet("{id:int}")]
    [Produces("application/json")]
    public TResource GetConfigById(int id)
    {
        return this.GetConfig();
    }

    [HttpPut]
    [HttpPut("{id:int}")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public virtual ActionResult<TResource> SaveConfig([FromBody] TResource resource)
    {
        try
        {
            if (this.SharedValidator != null)
            {
                var result = this.SharedValidator.Validate(resource);
                if (!result.IsValid)
                {
                    return this.BadRequest(result.Errors);
                }
            }

            var dictionary = resource.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(prop => prop.Name != "Id" && prop.Name != "ResourceName")
                .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

            this.configService.SaveConfigDictionary(dictionary);

            return this.Accepted(resource);
        }
        catch (Exception ex)
        {
            Console.WriteLine("SAVECONFIG EXCEPTION: " + ex);
            throw;
        }
    }

    protected abstract TResource ToResource(IConfigService model);
}
