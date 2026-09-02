// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace NzbDrone.Core.Messaging.Events;

public class EventAggregator : IEventAggregator
{
    private readonly Logger logger;
    private readonly IServiceProvider serviceProvider;

    public EventAggregator(IServiceProvider serviceProvider)
    {
        this.logger = LogManager.GetCurrentClassLogger();
        this.serviceProvider = serviceProvider;
    }

    public void PublishEvent<TEvent>(TEvent @event)
        where TEvent : class, IEvent
    {
        if (@event == null)
        {
            return;
        }

        this.logger.Trace("Publishing {0}", @event.GetType().Name);

        var handlerType = typeof(IHandle<>).MakeGenericType(@event.GetType());
        var handlers = this.serviceProvider.GetServices(handlerType);

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
