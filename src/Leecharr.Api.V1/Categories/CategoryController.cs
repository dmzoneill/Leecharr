// Copyright (c) PlaceholderCompany. All rights reserved.
using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Categories;
using NzbDrone.SignalR;

namespace Leecharr.Api.V1.Categories;

[V1ApiController("categories")]
[Route("api/v1/category")]
[Authorize(Policy = "RequireOperator")]
public class CategoryController : RestControllerWithSignalR<CategoryResource, Category>
{
    private readonly ICategoryService categoryService;

    public CategoryController(
        ICategoryService categoryService,
        IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        this.categoryService = categoryService;
    }

    [HttpGet]
    public ActionResult<List<CategoryResource>> GetAll()
    {
        var categories = this.categoryService.GetAll();
        var resources = categories.Select(CategoryResourceMapper.ToResource).ToList();
        return this.Ok(resources);
    }

    [HttpGet("{id:int}")]
    public ActionResult<CategoryResource> GetById(int id)
    {
        var category = this.categoryService.Get(id);
        if (category == null)
        {
            return this.NotFound();
        }

        return this.Ok(CategoryResourceMapper.ToResource(category));
    }

    [HttpPost]
    public ActionResult<CategoryResource> Add([FromBody] CategoryResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Name))
        {
            return this.BadRequest("Category name is required.");
        }

        var trimmedName = resource.Name.Trim();
        var existing = this.categoryService.GetByName(trimmedName);
        if (existing != null)
        {
            return this.BadRequest($"A category with name '{trimmedName}' already exists.");
        }

        resource.Name = trimmedName;
        var model = CategoryResourceMapper.ToModel(resource);
        var inserted = this.categoryService.Add(model);
        return this.Ok(CategoryResourceMapper.ToResource(inserted));
    }

    [HttpPut("{id:int}")]
    public ActionResult<CategoryResource> Update(int id, [FromBody] CategoryResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Name))
        {
            return this.BadRequest("Category name is required.");
        }

        var current = this.categoryService.Get(id);
        if (current == null)
        {
            return this.NotFound();
        }

        var trimmedName = resource.Name.Trim();
        var existingWithName = this.categoryService.GetByName(trimmedName);
        if (existingWithName != null && existingWithName.Id != id)
        {
            return this.BadRequest($"A category with name '{trimmedName}' already exists.");
        }

        resource.Name = trimmedName;
        var model = CategoryResourceMapper.ToModel(resource);
        model.Id = id;
        var updated = this.categoryService.Update(model);
        return this.Ok(CategoryResourceMapper.ToResource(updated));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        this.categoryService.Delete(id);
        return this.NoContent();
    }

    protected override CategoryResource GetResourceById(Category model)
    {
        return CategoryResourceMapper.ToResource(model);
    }
}
