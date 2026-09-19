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
    private static readonly string[] ForbiddenUnixSystemPrefixes =
    [
        "/etc",
        "/root",
        "/bin",
        "/sbin",
        "/usr",
        "/boot",
        "/sys",
        "/proc",
        "/dev",
        "/var"
    ];

    private static readonly string[] ForbiddenWindowsSystemPrefixes =
    [
        @"C:\Windows",
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\ProgramData"
    ];

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

    public static string NormalizeCategoryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim().Replace('\\', '/');
        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('/', parts);
    }

    public Category GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return this.repository.GetDefault();
        }

        var normalized = NormalizeCategoryName(name);
        return this.repository.GetByName(normalized) ?? this.repository.GetByName(name.Trim());
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

        category.Name = NormalizeCategoryName(category.Name);
        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name is required.", nameof(category));
        }

        if (category.SavePath != null)
        {
            category.SavePath = category.SavePath.Trim();
        }

        category.SavePath = this.ValidateSavePath(category.SavePath);

        var existing = this.repository.GetByName(category.Name);
        if (existing != null)
        {
            this.logger.Info("Category already exists: {0}, returning existing category", category.Name);
            if (!string.IsNullOrWhiteSpace(category.SavePath))
            {
                existing.SavePath = category.SavePath;
                this.repository.Update(existing);
                this.eventAggregator.PublishEvent(new CategoryUpdatedEvent { Category = existing });
            }

            return existing;
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

        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name is required.", nameof(category));
        }

        if (category.DefaultUploadLimit < 0 || category.DefaultDownloadLimit < 0 ||
            category.TargetRatio < 0 || category.TargetSeedTimeMinutes < 0)
        {
            throw new ArgumentException("Category limits and seed targets must be non-negative.", nameof(category));
        }

        category.Name = NormalizeCategoryName(category.Name);
        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name is required.", nameof(category));
        }

        if (category.SavePath != null)
        {
            category.SavePath = category.SavePath.Trim();
        }

        category.SavePath = this.ValidateSavePath(category.SavePath);

        this.logger.Info("Updating category: {0}", category.Name);
        var existing = this.repository.Get(category.Id);

        if (category.IsDefault)
        {
            this.ClearExistingDefaults(category.Id);
        }

        var updated = this.repository.Update(category);

        if (existing != null && !string.IsNullOrWhiteSpace(existing.Name))
        {
            var oldName = existing.Name;
            var newName = updated.Name;

            if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            {
                if (this.torrentRepository != null)
                {
                    var torrents = this.torrentRepository.GetByCategory(oldName);
                    if (torrents != null)
                    {
                        foreach (var torrent in torrents)
                        {
                            torrent.Category = newName;
                            this.torrentRepository.Update(torrent);
                        }
                    }
                }

                var oldPrefix = oldName + "/";
                var subcategories = this.repository.All()
                    .Where(c => c.Id != updated.Id && c.Name.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var subCat in subcategories)
                {
                    var oldSubName = subCat.Name;
                    var newSubName = newName + oldSubName[oldName.Length..];
                    subCat.Name = newSubName;
                    this.repository.Update(subCat);

                    if (this.torrentRepository != null)
                    {
                        var subTorrents = this.torrentRepository.GetByCategory(oldSubName);
                        if (subTorrents != null)
                        {
                            foreach (var torrent in subTorrents)
                            {
                                torrent.Category = newSubName;
                                this.torrentRepository.Update(torrent);
                            }
                        }
                    }

                    this.eventAggregator.PublishEvent(new CategoryUpdatedEvent { Category = subCat });
                }
            }
        }

        this.eventAggregator.PublishEvent(new CategoryUpdatedEvent { Category = updated });
        return updated;
    }

    private string ValidateSavePath(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return savePath?.Trim();
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

        if (!Path.IsPathRooted(savePath))
        {
            throw new ArgumentException("Save path must be an absolute rooted path.", nameof(savePath));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(savePath);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid save path: {savePath}", nameof(savePath), ex);
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(fullPath) ||
            fullPath == "/" ||
            fullPath == "\\" ||
            string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Save path cannot be the root filesystem directory.", nameof(savePath));
        }

        if (IsForbiddenSystemDirectory(fullPath))
        {
            throw new ArgumentException($"Save path cannot be a system directory: {fullPath}", nameof(savePath));
        }

        if (this.diskProvider != null)
        {
            if (this.diskProvider.FolderExists(fullPath))
            {
                if (!this.diskProvider.FolderWritable(fullPath))
                {
                    throw new InvalidOperationException($"Save path '{fullPath}' is not writable.");
                }
            }
            else
            {
                try
                {
                    this.diskProvider.CreateFolder(fullPath);
                }
                catch (Exception ex)
                {
                    this.logger.Warn("Could not create save directory '{0}': {1}", fullPath, ex.Message);
                }

                if (this.diskProvider.FolderExists(fullPath) && !this.diskProvider.FolderWritable(fullPath))
                {
                    throw new InvalidOperationException($"Save path '{fullPath}' is not writable.");
                }
            }
        }

        return fullPath;
    }

    public static bool IsForbiddenSystemDirectory(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var normalized = fullPath.Replace('\\', '/').TrimEnd('/');

        foreach (var prefix in ForbiddenUnixSystemPrefixes)
        {
            var normPrefix = prefix.Replace('\\', '/').TrimEnd('/');
            if (string.Equals(normalized, normPrefix, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(normPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var prefix in ForbiddenWindowsSystemPrefixes)
        {
            var normPrefix = prefix.Replace('\\', '/').TrimEnd('/');
            if (string.Equals(normalized, normPrefix, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(normPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var winPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows)?.Replace('\\', '/').TrimEnd('/');
            if (!string.IsNullOrEmpty(winPath) &&
                (string.Equals(normalized, winPath, StringComparison.OrdinalIgnoreCase) ||
                 normalized.StartsWith(winPath + "/", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)?.Replace('\\', '/').TrimEnd('/');
            if (!string.IsNullOrEmpty(progFiles) &&
                (string.Equals(normalized, progFiles, StringComparison.OrdinalIgnoreCase) ||
                 normalized.StartsWith(progFiles + "/", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsRootOrSystemDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var normalized = path.Replace('\\', '/').Trim();
        if (normalized == "/" ||
            string.Equals(normalized, "C:", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "C:/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            fullPath = normalized;
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(fullPath) ||
            fullPath == "/" ||
            fullPath == "\\" ||
            string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsForbiddenSystemDirectory(normalized) || IsForbiddenSystemDirectory(fullPath);
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

        if (cat.IsDefault)
        {
            this.logger.Warn("Cannot delete default category id: {0} ({1})", id, cat.Name);
            return;
        }

        this.logger.Info("Deleting category id: {0} ({1})", id, cat.Name);

        if (!string.IsNullOrWhiteSpace(cat.Name))
        {
            var prefix = cat.Name + "/";
            var directSubcategories = this.repository.All()
                .Where(c => c.Id != id &&
                            c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                            !c.Name[prefix.Length..].Contains('/'))
                .ToList();

            foreach (var subCat in directSubcategories)
            {
                this.Delete(subCat.Id);
            }
        }

        var defaultCategoryName = this.repository.GetDefault()?.Name ?? string.Empty;
        var affectedTorrentIds = new List<int>();
        if (this.torrentRepository != null && !string.IsNullOrWhiteSpace(cat.Name))
        {
            var torrents = this.torrentRepository.GetByCategory(cat.Name);
            if (torrents != null)
            {
                foreach (var torrent in torrents)
                {
                    affectedTorrentIds.Add(torrent.Id);
                    torrent.Category = defaultCategoryName;
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
            var normalized = NormalizeCategoryName(categoryName);
            var path = this.GetExplicitOrInheritedSavePath(normalized);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        var defaultCat = this.repository.GetDefault();
        if (defaultCat != null && !string.IsNullOrWhiteSpace(defaultCat.SavePath))
        {
            return defaultCat.SavePath;
        }

        return defaultPath;
    }

    private string GetExplicitOrInheritedSavePath(string normalizedCategory)
    {
        if (string.IsNullOrWhiteSpace(normalizedCategory))
        {
            return null;
        }

        var cat = this.repository.GetByName(normalizedCategory);
        if (cat != null && !string.IsNullOrWhiteSpace(cat.SavePath))
        {
            return cat.SavePath;
        }

        var slashIndex = normalizedCategory.LastIndexOf('/');
        if (slashIndex > 0 && slashIndex < normalizedCategory.Length - 1)
        {
            var parentName = normalizedCategory[..slashIndex];
            var childSegment = normalizedCategory[(slashIndex + 1)..];
            var parentPath = this.GetExplicitOrInheritedSavePath(parentName);
            if (!string.IsNullOrWhiteSpace(parentPath))
            {
                return Path.Combine(parentPath, childSegment);
            }
        }

        return null;
    }
}
