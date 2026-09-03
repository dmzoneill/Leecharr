// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NLog;

namespace NzbDrone.SignalR;

public class SignalRMessageBroadcaster : IBroadcastSignalRMessage, IDisposable
{
    private readonly IHubContext<MessageHub> hubContext;
    private readonly Logger logger;
    private readonly Channel<SignalRMessage> messageChannel;
    private readonly CancellationTokenSource cancellationTokenSource;
    private readonly Task processingTask;

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext)
        : this(hubContext, 1000)
    {
    }

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext, int capacity)
    {
        this.hubContext = hubContext;
        this.logger = LogManager.GetCurrentClassLogger();
        this.cancellationTokenSource = new CancellationTokenSource();

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
        };

        this.messageChannel = System.Threading.Channels.Channel.CreateBounded<SignalRMessage>(options);
        this.processingTask = Task.Run(this.ProcessChannelAsync);
    }

    public bool IsConnected => MessageHub.IsConnected;

    public Channel<SignalRMessage> BoundedChannel => this.messageChannel;

    public void BroadcastMessage(SignalRMessage message)
    {
        if (message == null)
        {
            return;
        }

        this.logger.Trace("Broadcasting SignalR message: {0}", message.Name);
        this.messageChannel.Writer.TryWrite(message);
    }

    public void Dispose()
    {
        this.messageChannel.Writer.TryComplete();
        this.cancellationTokenSource.Cancel();
        this.cancellationTokenSource.Dispose();
    }

    private async Task ProcessChannelAsync()
    {
        var reader = this.messageChannel.Reader;
        var token = this.cancellationTokenSource.Token;

        try
        {
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var message))
                {
                    try
                    {
                        if (this.hubContext != null)
                        {
                            await this.hubContext.Clients.All.SendAsync("receiveMessage", message, token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "SignalR broadcast failed for message {0}", message.Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Unhandled exception in SignalRMessageBroadcaster channel processor");
        }
    }
}
