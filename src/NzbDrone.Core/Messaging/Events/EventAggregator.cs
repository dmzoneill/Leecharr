using System;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace NzbDrone.Core.Messaging.Events;

public class EventAggregator : IEventAggregator
{
    private readonly Logger _logger;
    private readonly IServiceProvider _serviceProvider;

    public EventAggregator(IServiceProvider serviceProvider)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _serviceProvider = serviceProvider;
    }

    public void PublishEvent<TEvent>(TEvent @event)
        where TEvent : class, IEvent
    {
        if (@event == null)
        {
            return;
        }

        _logger.Trace("Publishing {0}", @event.GetType().Name);

        var handlerType = typeof(IHandle<>).MakeGenericType(@event.GetType());
        var handlers = _serviceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            try
            {
                ((dynamic)handler).Handle((dynamic)@event);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling {0}", @event.GetType().Name);
            }
        }
    }
}
