// Copyright (c) PlaceholderCompany. All rights reserved.

using Leecharr.Http.REST;
using NzbDrone.Core.Categories;

namespace Leecharr.Api.V1.Categories;

public class CategoryResource : RestResource
{
    public string Name { get; set; }

    public string SavePath { get; set; }

    public int DefaultUploadLimit { get; set; }

    public int DefaultDownloadLimit { get; set; }

    public double TargetRatio { get; set; }

    public int TargetSeedTimeMinutes { get; set; }

    public bool AutoStop { get; set; }

    public bool IsDefault { get; set; }
}

public static class CategoryResourceMapper
{
    public static CategoryResource ToResource(Category model)
    {
        if (model == null)
        {
            return null;
        }

        return new CategoryResource
        {
            Id = model.Id,
            Name = model.Name,
            SavePath = model.SavePath,
            DefaultUploadLimit = model.DefaultUploadLimit,
            DefaultDownloadLimit = model.DefaultDownloadLimit,
            TargetRatio = model.TargetRatio,
            TargetSeedTimeMinutes = model.TargetSeedTimeMinutes,
            AutoStop = model.AutoStop,
            IsDefault = model.IsDefault,
        };
    }

    public static Category ToModel(CategoryResource resource)
    {
        if (resource == null)
        {
            return null;
        }

        return new Category
        {
            Id = resource.Id,
            Name = resource.Name,
            SavePath = resource.SavePath,
            DefaultUploadLimit = resource.DefaultUploadLimit,
            DefaultDownloadLimit = resource.DefaultDownloadLimit,
            TargetRatio = resource.TargetRatio,
            TargetSeedTimeMinutes = resource.TargetSeedTimeMinutes,
            AutoStop = resource.AutoStop,
            IsDefault = resource.IsDefault,
        };
    }
}
