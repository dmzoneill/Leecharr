using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Host;

public class AppLifetime : IHostedService
{
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    public AppLifetime(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Leecharr application started");
        _eventAggregator.PublishEvent(new ApplicationStartedEvent());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Leecharr application stopping");
        _eventAggregator.PublishEvent(new ApplicationShutdownRequested());
        return Task.CompletedTask;
    }
}
