// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace NzbDrone.Core.Messaging.Events;

public class EventAggregator : IEventAggregator
{
    private readonly Logger logger;
    private readonly IServiceProvider serviceProvider;
    private readonly NzbDrone.Core.Developer.IDeveloperEventStore developerEventStore;

    public EventAggregator(
        IServiceProvider serviceProvider,
        NzbDrone.Core.Developer.IDeveloperEventStore developerEventStore = null)
    {
        this.logger = LogManager.GetCurrentClassLogger();
        this.serviceProvider = serviceProvider;
        this.developerEventStore = developerEventStore;
    }

    public void PublishEvent<TEvent>(TEvent @event)
        where TEvent : class, IEvent
    {
        if (@event == null)
        {
            return;
        }

        this.logger.Trace("Publishing {0}", @event.GetType().Name);

        try
        {
            this.developerEventStore?.RecordEvent(@event);
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Failed to record developer event in developerEventStore");
        }

        var handlerType = typeof(IHandle<>).MakeGenericType(@event.GetType());
        var handlers = this.serviceProvider.GetServices(handlerType).DistinctBy(h => h.GetType());

        foreach (var handler in handlers)
        {
            try
            {
                ((dynamic)handler).Handle((dynamic)@event);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error handling {0}", @event.GetType().Name);
            }
        }
    }
}
