using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NLog;

namespace NzbDrone.SignalR;

public class SignalRMessageBroadcaster : IBroadcastSignalRMessage
{
    private readonly IHubContext<MessageHub> _hubContext;
    private readonly Logger _logger;

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext)
    {
        _hubContext = hubContext;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool IsConnected => MessageHub.IsConnected;

    public void BroadcastMessage(SignalRMessage message)
    {
        _logger.Trace("Broadcasting SignalR message: {0}", message.Name);
        _hubContext.Clients.All.SendAsync("receiveMessage", message)
            .ContinueWith(t => _logger.Warn(t.Exception, "SignalR broadcast failed"), TaskContinuationOptions.OnlyOnFaulted);
    }
}
