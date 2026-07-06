using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Leecharr.Http;
using Leecharr.Http.REST;
using NzbDrone.Core.Categories;
using NzbDrone.SignalR;

namespace Leecharr.Api.V1.Categories;

[V1ApiController("categories")]
public class CategoryController : RestControllerWithSignalR<CategoryResource, Category>
{
    private readonly ICategoryService _categoryService;

    public CategoryController(
        ICategoryService categoryService,
        IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    public ActionResult<List<CategoryResource>> GetAll()
    {
        var categories = _categoryService.GetAll();
        var resources = categories.Select(CategoryResourceMapper.ToResource).ToList();
        return Ok(resources);
    }

    [HttpGet("{id:int}")]
    public ActionResult<CategoryResource> GetById(int id)
    {
        var category = _categoryService.Get(id);
        if (category == null)
        {
            return NotFound();
        }

        return Ok(CategoryResourceMapper.ToResource(category));
    }

    [HttpPost]
    public ActionResult<CategoryResource> Add([FromBody] CategoryResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Name))
        {
            return BadRequest("Category name is required.");
        }

        var model = CategoryResourceMapper.ToModel(resource);
        var inserted = _categoryService.Add(model);
        return Ok(CategoryResourceMapper.ToResource(inserted));
    }

    [HttpPut("{id:int}")]
    public ActionResult<CategoryResource> Update(int id, [FromBody] CategoryResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = CategoryResourceMapper.ToModel(resource);
        model.Id = id;
        var updated = _categoryService.Update(model);
        return Ok(CategoryResourceMapper.ToResource(updated));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _categoryService.Delete(id);
        return NoContent();
    }

    protected override CategoryResource GetResourceById(Category model)
    {
        return CategoryResourceMapper.ToResource(model);
    }
}
