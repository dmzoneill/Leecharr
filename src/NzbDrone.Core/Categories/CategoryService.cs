// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Categories;

public class CategoryUpdatedEvent : IEvent
{
    public Category Category { get; set; }
}

public class CategoryDeletedEvent : IEvent
{
    public int CategoryId { get; set; }

    public string CategoryName { get; set; }
}

public interface ICategoryService
{
    IEnumerable<Category> GetAll();

    Category Get(int id);

    Category GetByName(string name);

    Category Add(Category category);

    Category Update(Category category);

    void Delete(int id);

    string GetSavePathForCategory(string categoryName, string defaultPath = "");
}

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository repository;
    private readonly IEventAggregator eventAggregator;
    private readonly ITorrentRepository torrentRepository;
    private readonly Logger logger;

    public CategoryService(
        ICategoryRepository repository,
        IEventAggregator eventAggregator,
        ITorrentRepository torrentRepository = null)
    {
        this.repository = repository;
        this.eventAggregator = eventAggregator;
        this.torrentRepository = torrentRepository;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public IEnumerable<Category> GetAll()
    {
        return this.repository.All().OrderBy(c => c.Name);
    }

    public Category Get(int id)
    {
        return this.repository.Get(id);
    }

    public Category GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return this.repository.GetDefault();
        }

        return this.repository.GetByName(name);
    }

    public Category Add(Category category)
    {
        if (category == null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        this.logger.Info("Adding category: {0}", category.Name);
        if (category.IsDefault)
        {
            this.ClearExistingDefaults(0);
        }

        var inserted = this.repository.Insert(category);
        this.eventAggregator.PublishEvent(new CategoryUpdatedEvent { Category = inserted });
        return inserted;
    }

    public Category Update(Category category)
    {
        if (category == null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        this.logger.Info("Updating category: {0}", category.Name);
        if (category.IsDefault)
        {
            this.ClearExistingDefaults(category.Id);
        }

        var updated = this.repository.Update(category);
        this.eventAggregator.PublishEvent(new CategoryUpdatedEvent { Category = updated });
        return updated;
    }

    private void ClearExistingDefaults(int currentCategoryId)
    {
        var existingDefaults = this.repository.All().Where(c => c.IsDefault && c.Id != currentCategoryId);
        foreach (var existing in existingDefaults)
        {
            existing.IsDefault = false;
            this.repository.Update(existing);
        }
    }

    public void Delete(int id)
    {
        var cat = this.repository.Get(id);
        if (cat == null)
        {
            return;
        }

        this.logger.Info("Deleting category id: {0} ({1})", id, cat.Name);

        if (this.torrentRepository != null && !string.IsNullOrWhiteSpace(cat.Name))
        {
            var torrents = this.torrentRepository.GetByCategory(cat.Name).ToList();
            foreach (var torrent in torrents)
            {
                torrent.Category = string.Empty;
                this.torrentRepository.Update(torrent);
            }
        }

        this.repository.Delete(id);

        this.eventAggregator.PublishEvent(new CategoryDeletedEvent
        {
            CategoryId = id,
            CategoryName = cat.Name,
        });
    }

    public string GetSavePathForCategory(string categoryName, string defaultPath = "")
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var cat = this.repository.GetByName(categoryName);
            if (cat != null && !string.IsNullOrWhiteSpace(cat.SavePath))
            {
                return cat.SavePath;
            }
        }

        var defaultCat = this.repository.GetDefault();
        if (defaultCat != null && !string.IsNullOrWhiteSpace(defaultCat.SavePath))
        {
            return defaultCat.SavePath;
        }

        return defaultPath;
    }
}
