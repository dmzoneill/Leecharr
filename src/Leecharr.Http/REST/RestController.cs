using Microsoft.AspNetCore.Mvc;

namespace Leecharr.Http.REST;

public abstract class RestController<TResource> : Controller
    where TResource : RestResource, new()
{
    protected ResourceValidator<TResource> PostValidator { get; set; }
    protected ResourceValidator<TResource> PutValidator { get; set; }
    protected ResourceValidator<TResource> SharedValidator { get; set; }

    protected ActionResult ValidateAndRespond(TResource resource, ResourceValidator<TResource> validator)
    {
        if (validator != null)
        {
            var result = validator.Validate(resource);
            if (!result.IsValid)
            {
                return BadRequest(result.Errors);
            }
        }

        return Ok(resource);
    }
}
