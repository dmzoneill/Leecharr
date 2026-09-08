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
    private readonly Channel<SignalRMessage> telemetryChannel;
    private readonly Channel<SignalRMessage> guaranteedChannel;
    private readonly CancellationTokenSource cancellationTokenSource;
    private readonly Task telemetryProcessingTask;
    private readonly Task guaranteedProcessingTask;
    private bool disposed;

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext)
        : this(hubContext, 1000)
    {
    }

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext, int telemetryCapacity)
    {
        this.hubContext = hubContext;
        this.logger = LogManager.GetCurrentClassLogger();
        this.cancellationTokenSource = new CancellationTokenSource();

        var telemetryOptions = new BoundedChannelOptions(telemetryCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
        };

        var guaranteedOptions = new BoundedChannelOptions(50_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
        };

        this.telemetryChannel = System.Threading.Channels.Channel.CreateBounded<SignalRMessage>(telemetryOptions);
        this.guaranteedChannel = System.Threading.Channels.Channel.CreateBounded<SignalRMessage>(guaranteedOptions);

        this.telemetryProcessingTask = Task.Run(() => this.ProcessChannelAsync(this.telemetryChannel, "telemetry"));
        this.guaranteedProcessingTask = Task.Run(() => this.ProcessChannelAsync(this.guaranteedChannel, "guaranteed"));
    }

    public virtual bool IsConnected => MessageHub.IsConnected;

    public Channel<SignalRMessage> BoundedChannel => this.telemetryChannel;

    public Channel<SignalRMessage> TelemetryChannel => this.telemetryChannel;

    public Channel<SignalRMessage> GuaranteedChannel => this.guaranteedChannel;

    public void BroadcastMessage(SignalRMessage message)
    {
        if (message == null || this.disposed || !this.IsConnected)
        {
            return;
        }

        this.logger.Trace("Broadcasting SignalR message: {0}", message.Name);
        if (IsTelemetryMessage(message))
        {
            this.telemetryChannel.Writer.TryWrite(message);
        }
        else
        {
            this.guaranteedChannel.Writer.TryWrite(message);
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        this.telemetryChannel.Writer.TryComplete();
        this.guaranteedChannel.Writer.TryComplete();

        try
        {
            this.cancellationTokenSource.Cancel();
            Task.WaitAll(new[] { this.telemetryProcessingTask, this.guaranteedProcessingTask }, TimeSpan.FromSeconds(3));
        }
        catch
        {
        }
        finally
        {
            this.cancellationTokenSource.Dispose();
        }
    }

    private static bool IsTelemetryMessage(SignalRMessage message)
    {
        if (string.IsNullOrEmpty(message?.Name))
        {
            return false;
        }

        return message.Name.Equals("speedPulse", StringComparison.OrdinalIgnoreCase) ||
               message.Name.Equals("pieceMapUpdated", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ProcessChannelAsync(Channel<SignalRMessage> channel, string channelName)
    {
        var reader = channel.Reader;
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
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "SignalR broadcast failed for message {0} on {1} channel", message.Name, channelName);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Unhandled exception in SignalRMessageBroadcaster {0} channel processor", channelName);
        }
    }
}
