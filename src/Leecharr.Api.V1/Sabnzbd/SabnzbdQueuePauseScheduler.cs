// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Sabnzbd;

internal static class SabnzbdQueuePauseScheduler
{
    private static readonly object Gate = new();
    private static CancellationTokenSource resumeCts;

    public static void ScheduleResume(ITorrentService torrentService, TimeSpan delay)
    {
        if (torrentService == null)
        {
            throw new ArgumentNullException(nameof(torrentService));
        }

        lock (Gate)
        {
            resumeCts?.Cancel();
            resumeCts?.Dispose();
            resumeCts = new CancellationTokenSource();
            var token = resumeCts.Token;
            _ = ResumeAfterDelayAsync(torrentService, delay, token);
        }
    }

    private static async Task ResumeAfterDelayAsync(ITorrentService torrentService, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            foreach (var torrent in torrentService.GetAll())
            {
                await torrentService.ResumeAsync(torrent.Id).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
