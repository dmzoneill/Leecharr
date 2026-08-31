// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NLog;

namespace NzbDrone.SignalR;

public class SignalRMessageBroadcaster : IBroadcastSignalRMessage
{
    private readonly IHubContext<MessageHub> hubContext;
    private readonly Logger logger;

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext)
    {
        this.hubContext = hubContext;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public bool IsConnected => MessageHub.IsConnected;

    public void BroadcastMessage(SignalRMessage message)
    {
        this.logger.Trace("Broadcasting SignalR message: {0}", message.Name);
        this.hubContext.Clients.All.SendAsync("receiveMessage", message)
            .ContinueWith(t => this.logger.Warn(t.Exception, "SignalR broadcast failed"), TaskContinuationOptions.OnlyOnFaulted);
    }
}
