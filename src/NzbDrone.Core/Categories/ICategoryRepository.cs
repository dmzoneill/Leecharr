// Copyright (c) FeedItOut. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Categories;

public interface ICategoryRepository : IBasicRepository<Category>
{
    Category GetByName(string name);

    Category GetDefault();

    Category InsertAsDefault(Category category);

    Category SetAsDefault(Category category);
}
