using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Indexers;
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

    public List<int> AffectedTorrentIds { get; set; } = new();
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
    private readonly IRssRuleRepository rssRuleRepository;
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger;

    public CategoryService(
        ICategoryRepository repository,
        IEventAggregator eventAggregator,
        ITorrentRepository torrentRepository = null,
        IDiskProvider diskProvider = null,
        IRssRuleRepository rssRuleRepository = null)
    {
        this.repository = repository;
        this.eventAggregator = eventAggregator;
        this.torrentRepository = torrentRepository;
        this.diskProvider = diskProvider;
        this.rssRuleRepository = rssRuleRepository;
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

        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name is required.", nameof(category));
        }

        if (category.DefaultUploadLimit < 0 || category.DefaultDownloadLimit < 0 ||
            category.TargetRatio < 0 || category.TargetSeedTimeMinutes < 0)
        {
            throw new ArgumentException("Category limits and seed targets must be non-negative.", nameof(category));
        }

        category.Name = category.Name.Trim();
        if (category.SavePath != null)
        {
            category.SavePath = category.SavePath.Trim();
        }

        this.ValidateSavePath(category.SavePath);

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

        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name is required.", nameof(category));
        }

        if (category.DefaultUploadLimit < 0 || category.DefaultDownloadLimit < 0 ||
            category.TargetRatio < 0 || category.TargetSeedTimeMinutes < 0)
        {
            throw new ArgumentException("Category limits and seed targets must be non-negative.", nameof(category));
        }

        category.Name = category.Name.Trim();
        if (category.SavePath != null)
        {
            category.SavePath = category.SavePath.Trim();
        }

        this.ValidateSavePath(category.SavePath);

        this.logger.Info("Updating category: {0}", category.Name);
        var existing = this.repository.Get(category.Id);

        if (category.IsDefault)
        {
            this.ClearExistingDefaults(category.Id);
        }

        var updated = this.repository.Update(category);

        if (this.torrentRepository != null && existing != null &&
            !string.IsNullOrWhiteSpace(existing.Name) &&
            !string.Equals(existing.Name, updated.Name, StringComparison.OrdinalIgnoreCase))
        {
            var torrents = this.torrentRepository.GetByCategory(existing.Name);
            if (torrents != null)
            {
                foreach (var torrent in torrents)
                {
                    torrent.Category = updated.Name;
                    this.torrentRepository.Update(torrent);
                }
            }
        }

        this.eventAggregator.PublishEvent(new CategoryUpdatedEvent { Category = updated });
        return updated;
    }

    private void ValidateSavePath(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        if (savePath.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("Save path contains invalid characters.", nameof(savePath));
        }

        var invalidChars = Path.GetInvalidPathChars();
        if (savePath.IndexOfAny(invalidChars) >= 0)
        {
            throw new ArgumentException("Save path contains invalid characters.", nameof(savePath));
        }

        if (this.diskProvider != null)
        {
            try
            {
                if (!this.diskProvider.FolderExists(savePath))
                {
                    this.diskProvider.CreateFolder(savePath);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Could not create save directory '{savePath}': {ex.Message}", ex);
            }

            if (!this.diskProvider.FolderWritable(savePath))
            {
                throw new InvalidOperationException($"Save path '{savePath}' is not writable.");
            }
        }
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

        var affectedTorrentIds = new List<int>();
        if (this.torrentRepository != null && !string.IsNullOrWhiteSpace(cat.Name))
        {
            var torrents = this.torrentRepository.GetByCategory(cat.Name);
            if (torrents != null)
            {
                foreach (var torrent in torrents)
                {
                    affectedTorrentIds.Add(torrent.Id);
                    torrent.Category = string.Empty;
                    this.torrentRepository.Update(torrent);
                }
            }
        }

        if (this.rssRuleRepository != null)
        {
            var rules = this.rssRuleRepository.All().Where(r => r.CategoryId == id).ToList();
            foreach (var rule in rules)
            {
                rule.CategoryId = 0;
                this.rssRuleRepository.Update(rule);
            }
        }

        this.repository.Delete(id);
        this.eventAggregator.PublishEvent(new CategoryDeletedEvent
        {
            CategoryId = id,
            CategoryName = cat.Name,
            AffectedTorrentIds = affectedTorrentIds,
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
