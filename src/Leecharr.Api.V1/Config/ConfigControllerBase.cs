using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public abstract class ConfigController<TResource> : Controller
    where TResource : RestResource, new()
{
    protected readonly IConfigService _configService;
    protected ResourceValidator<TResource> SharedValidator { get; set; }

    protected ConfigController(IConfigService configService)
    {
        _configService = configService;
        SharedValidator = new ResourceValidator<TResource>();
    }

    [HttpGet]
    [Produces("application/json")]
    public TResource GetConfig()
    {
        var resource = ToResource(_configService);
        resource.Id = 1;

        return resource;
    }

    [HttpGet("{id:int}")]
    [Produces("application/json")]
    public TResource GetConfigById(int id)
    {
        return GetConfig();
    }

    [HttpPut]
    [HttpPut("{id:int}")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public virtual ActionResult<TResource> SaveConfig([FromBody] TResource resource)
    {
        try
        {
            if (SharedValidator != null)
            {
                var result = SharedValidator.Validate(resource);
                if (!result.IsValid)
                {
                    return BadRequest(result.Errors);
                }
            }

            var dictionary = resource.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(prop => prop.Name != "Id" && prop.Name != "ResourceName")
                .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

            _configService.SaveConfigDictionary(dictionary);

            return Accepted(resource);
        }
        catch (Exception ex)
        {
            Console.WriteLine("SAVECONFIG EXCEPTION: " + ex);
            throw;
        }
    }

    protected abstract TResource ToResource(IConfigService model);
}
