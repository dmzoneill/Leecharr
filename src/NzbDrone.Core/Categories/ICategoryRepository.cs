// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Categories;

public interface ICategoryRepository : IBasicRepository<Category>
{
    Category GetByName(string name);

    Category GetDefault();
}
