// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Linq;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tags;

public interface ITagRepository : IBasicRepository<Tag>
{
    Tag GetByLabel(string label);
}

public class TagRepository : BasicRepository<Tag>, ITagRepository
{
    public TagRepository(IDatabase database)
        : base(database)
    {
    }

    public Tag GetByLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        return this.All().FirstOrDefault(t => t.Label == label);
    }
}
