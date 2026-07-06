using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Categories;

public class CategoryUpdatedEvent : IEvent
{
    public Category Category { get; set; }
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
    private readonly ICategoryRepository _repository;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    public CategoryService(ICategoryRepository repository, IEventAggregator eventAggregator)
    {
        _repository = repository;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public IEnumerable<Category> GetAll()
    {
        return _repository.All().OrderBy(c => c.Name);
    }

    public Category Get(int id)
    {
        return _repository.Get(id);
    }

    public Category GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return _repository.GetDefault();
        }

        return _repository.GetByName(name);
    }

    public Category Add(Category category)
    {
        if (category == null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        _logger.Info("Adding category: {0}", category.Name);
        var inserted = _repository.Insert(category);
        _eventAggregator.PublishEvent(new CategoryUpdatedEvent { Category = inserted });
        return inserted;
    }

    public Category Update(Category category)
    {
        if (category == null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        _logger.Info("Updating category: {0}", category.Name);
        var updated = _repository.Update(category);
        _eventAggregator.PublishEvent(new CategoryUpdatedEvent { Category = updated });
        return updated;
    }

    public void Delete(int id)
    {
        _logger.Info("Deleting category id: {0}", id);
        _repository.Delete(id);
    }

    public string GetSavePathForCategory(string categoryName, string defaultPath = "")
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var cat = _repository.GetByName(categoryName);
            if (cat != null && !string.IsNullOrWhiteSpace(cat.SavePath))
            {
                return cat.SavePath;
            }
        }

        var defaultCat = _repository.GetDefault();
        if (defaultCat != null && !string.IsNullOrWhiteSpace(defaultCat.SavePath))
        {
            return defaultCat.SavePath;
        }

        return defaultPath;
    }
}
