// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Core.Datastore;

namespace NzbDrone.SignalR;

public class SignalRMessageBroadcaster : IBroadcastSignalRMessage, IDisposable
{
    private readonly IHubContext<MessageHub> hubContext;
    private readonly Logger logger;
    private readonly Channel<SignalRMessage> telemetryChannel;
    private readonly Channel<SignalRMessage> guaranteedChannel;
    private readonly Channel<SignalRMessage> guaranteedOverflowChannel;
    private readonly CancellationTokenSource cancellationTokenSource;
    private readonly Task telemetryProcessingTask;
    private readonly Task guaranteedProcessingTask;
    private readonly Task guaranteedOverflowPumpTask;
    private readonly int guaranteedChannelCapacity;
    private readonly TimeSpan sendTimeout;
    private bool disposed;

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext)
        : this(hubContext, 1000, TimeSpan.FromSeconds(3))
    {
    }

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext, int telemetryCapacity)
        : this(hubContext, telemetryCapacity, TimeSpan.FromSeconds(3), 50_000)
    {
    }

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext, int telemetryCapacity, TimeSpan sendTimeout)
        : this(hubContext, telemetryCapacity, sendTimeout, 50_000)
    {
    }

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext, int telemetryCapacity, TimeSpan sendTimeout, int guaranteedCapacity)
    {
        this.hubContext = hubContext;
        this.logger = LogManager.GetCurrentClassLogger();
        this.cancellationTokenSource = new CancellationTokenSource();
        this.sendTimeout = sendTimeout > TimeSpan.Zero ? sendTimeout : TimeSpan.FromSeconds(3);
        this.guaranteedChannelCapacity = guaranteedCapacity > 0 ? guaranteedCapacity : 50_000;

        var telemetryOptions = new BoundedChannelOptions(telemetryCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
        };

        var guaranteedOptions = new BoundedChannelOptions(this.guaranteedChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
        };

        this.telemetryChannel = System.Threading.Channels.Channel.CreateBounded<SignalRMessage>(telemetryOptions);
        this.guaranteedChannel = System.Threading.Channels.Channel.CreateBounded<SignalRMessage>(guaranteedOptions);
        this.guaranteedOverflowChannel = System.Threading.Channels.Channel.CreateUnbounded<SignalRMessage>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });

        this.telemetryProcessingTask = Task.Run(() => this.ProcessChannelAsync(this.telemetryChannel, "telemetry"), this.cancellationTokenSource.Token);
        this.guaranteedProcessingTask = Task.Run(() => this.ProcessChannelAsync(this.guaranteedChannel, "guaranteed"), this.cancellationTokenSource.Token);
        this.guaranteedOverflowPumpTask = Task.Run(() => this.PumpGuaranteedOverflowAsync(), this.cancellationTokenSource.Token);
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
            if (!this.guaranteedChannel.Writer.TryWrite(message))
            {
                this.logger.Warn("Guaranteed SignalR channel is full ({0} capacity). Queueing message '{1}' on ordered overflow pump.", this.guaranteedChannelCapacity, message.Name);
                if (!this.guaranteedOverflowChannel.Writer.TryWrite(message))
                {
                    this.logger.Trace("Guaranteed overflow pump is closed; dropping message '{0}' during shutdown", message.Name);
                }
            }
        }
    }

    public void BroadcastToGroup(string groupName, SignalRMessage message)
    {
        if (string.IsNullOrWhiteSpace(groupName) || message == null || this.disposed || !this.IsConnected)
        {
            return;
        }

        var group = this.hubContext?.Clients?.Group(groupName);
        if (group == null)
        {
            return;
        }

        group.SendAsync("receiveMessage", message, this.cancellationTokenSource?.Token ?? CancellationToken.None)
            ?.ContinueWith(t => this.logger.Warn(t.Exception, "SignalR group broadcast failed"), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

        var events = GetNamedEvents(message);
        foreach (var ev in events)
        {
            group.SendAsync(ev, message.Body, this.cancellationTokenSource?.Token ?? CancellationToken.None)
                ?.ContinueWith(t => this.logger.Warn(t.Exception, "SignalR group named event broadcast failed"), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    public void BroadcastToTorrent(int torrentId, SignalRMessage message)
    {
        this.BroadcastToGroup($"torrent-{torrentId}", message);
    }

    public void BroadcastToChannel(string channel, SignalRMessage message)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        this.BroadcastToGroup($"channel-{channel.ToLowerInvariant()}", message);
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
        this.guaranteedOverflowChannel.Writer.TryComplete();

        try
        {
            this.cancellationTokenSource.Cancel();
            Task.WaitAll(new[] { this.telemetryProcessingTask, this.guaranteedProcessingTask, this.guaranteedOverflowPumpTask }, TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Error while waiting for SignalR message broadcaster processing tasks to terminate");
        }
        finally
        {
            this.cancellationTokenSource.Dispose();
        }
    }

    private async Task PumpGuaranteedOverflowAsync()
    {
        var overflowReader = this.guaranteedOverflowChannel.Reader;
        var token = this.cancellationTokenSource.Token;

        try
        {
            while (await overflowReader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (overflowReader.TryRead(out var message))
                {
                    try
                    {
                        await this.guaranteedChannel.Writer.WriteAsync(message, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex)
                    {
                        this.logger.Trace(ex, "Guaranteed overflow pump cancelled during shutdown for message '{0}'", message.Name);
                        return;
                    }
                    catch (ChannelClosedException ex)
                    {
                        this.logger.Trace(ex, "Guaranteed channel closed while overflow pump was writing message '{0}'", message.Name);
                        return;
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Failed to write guaranteed SignalR message '{0}' from overflow pump", message.Name);
                    }
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            this.logger.Trace(ex, "Guaranteed overflow pump cancelled");
        }
        catch (ObjectDisposedException ex)
        {
            this.logger.Trace(ex, "Guaranteed overflow pump disposed during shutdown");
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Unhandled exception in guaranteed SignalR overflow pump");
        }
    }

    private static bool IsTelemetryMessage(SignalRMessage message)
    {
        if (string.IsNullOrEmpty(message?.Name))
        {
            return false;
        }

        return message.Name.Equals("speedPulse", StringComparison.OrdinalIgnoreCase);
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
                            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                            sendCts.CancelAfter(this.sendTimeout);

                            try
                            {
                                await this.hubContext.Clients.All.SendAsync("receiveMessage", message, sendCts.Token).ConfigureAwait(false);

                                var events = GetNamedEvents(message);
                                foreach (var ev in events)
                                {
                                    try
                                    {
                                        await this.hubContext.Clients.All.SendAsync(ev, message.Body, sendCts.Token).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    {
                                        this.logger.Trace(ex, "Failed to send individual named event '{0}' for message '{1}'", ev, message.Name);
                                    }
                                }
                            }
                            catch (OperationCanceledException) when (sendCts.IsCancellationRequested && !token.IsCancellationRequested)
                            {
                                this.logger.Warn("SignalR broadcast timed out after {0}s for message '{1}' on {2} channel", this.sendTimeout.TotalSeconds, message.Name, channelName);
                            }
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
        catch (OperationCanceledException ex)
        {
            this.logger.Trace(ex, "SignalR broadcaster channel loop cancelled for {0}", channelName);
        }
        catch (ObjectDisposedException ex)
        {
            this.logger.Trace(ex, "SignalR broadcaster channel disposed during shutdown for {0}", channelName);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Unhandled exception in SignalRMessageBroadcaster {0} channel processor", channelName);
        }
    }

    private static List<string> GetNamedEvents(SignalRMessage message)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(message?.Name))
        {
            return list;
        }

        var name = message.Name;
        if (string.Equals(name, "Torrent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Torrents", StringComparison.OrdinalIgnoreCase))
        {
            switch (message.Action)
            {
                case ModelAction.Created:
                    list.Add("TorrentAdded");
                    break;
                case ModelAction.Updated:
                    list.Add("TorrentUpdated");
                    break;
                case ModelAction.Deleted:
                    list.Add("TorrentDeleted");
                    break;
            }
        }
        else if (string.Equals(name, "TorrentAdded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "torrentAdded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "torrent_added", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("TorrentAdded");
        }
        else if (string.Equals(name, "TorrentUpdated", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "torrentUpdated", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "torrent_updated", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("TorrentUpdated");
        }
        else if (string.Equals(name, "TorrentDeleted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "torrentDeleted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "torrent_deleted", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("TorrentDeleted");
        }
        else if (string.Equals(name, "speedPulse", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "speed_update", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "speedUpdate", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Seeding", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "SeedingStatsUpdated", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("speedPulse");
        }
        else if (string.Equals(name, "Health", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "HealthCheckCompleted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "health_warning", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "healthWarning", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("HealthCheckCompleted");
        }
        else if (string.Equals(name, "Task", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "TaskStarted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "TaskCompleted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "task_progress", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "taskProgress", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Command", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "CommandStarted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "CommandCompleted", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(name);
        }
        else if (string.Equals(name, "pieceMapUpdated", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "piece_map_updated", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "PieceCompleted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "PieceBatchCompleted", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("pieceMapUpdated");
        }
        else if (string.Equals(name, "Tracker", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "trackerUpdated", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "TrackerUpdated", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "tracker_updated", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("trackerUpdated");
        }
        else if (string.Equals(name, "trackerAnnounced", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "TrackerAnnounced", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "tracker_announced", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("trackerAnnounced");
        }
        else
        {
            list.Add(name);
        }

        return list;
    }
}
