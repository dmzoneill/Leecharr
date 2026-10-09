// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Bandwidth;

public interface IBandwidthQuotaService
{
    void CheckBandwidthQuotaThresholds();
}

public class BandwidthQuotaService : IBandwidthQuotaService
{
    private readonly IConfigService configService;
    private readonly ITorrentService torrentService;
    private readonly IDownloadEngine downloadEngine;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private bool approachingEventActive;
    private long lastSessionTransferBytes;

    public BandwidthQuotaService(
        IConfigService configService,
        ITorrentService torrentService = null,
        IDownloadEngine downloadEngine = null,
        IEventAggregator eventAggregator = null)
    {
        this.configService = configService;
        this.torrentService = torrentService;
        this.downloadEngine = downloadEngine;
        this.eventAggregator = eventAggregator;
    }

    public void CheckBandwidthQuotaThresholds()
    {
        if (this.eventAggregator == null || this.configService == null)
        {
            return;
        }

        var quotaLimitBytes = this.ResolveQuotaLimitBytes();
        if (quotaLimitBytes <= 0)
        {
            this.approachingEventActive = false;
            return;
        }

        var thresholdPercent = this.configService.BandwidthQuotaThresholdPercent;
        if (thresholdPercent <= 0)
        {
            return;
        }

        var bytesUsed = this.ComputeBytesUsed();
        if (bytesUsed < 0)
        {
            bytesUsed = 0;
        }

        var percentageUsed = quotaLimitBytes > 0
            ? (double)bytesUsed / quotaLimitBytes * 100.0
            : 0.0;

        if (percentageUsed >= thresholdPercent)
        {
            if (!this.approachingEventActive)
            {
                this.approachingEventActive = true;
                this.logger.Info(
                    "Bandwidth quota threshold reached: {0:F1}% used ({1} / {2} bytes)",
                    percentageUsed,
                    bytesUsed,
                    quotaLimitBytes);
                this.eventAggregator.PublishEvent(
                    new BandwidthQuotaApproachingEvent(bytesUsed, quotaLimitBytes, percentageUsed));
            }
        }
        else
        {
            var resetBelow = Math.Max(0, thresholdPercent - 5);
            if (percentageUsed < resetBelow)
            {
                this.approachingEventActive = false;
            }
        }
    }

    private long ResolveQuotaLimitBytes()
    {
        var bytes = this.configService.MonthlyBandwidthQuotaBytes;
        if (bytes > 0)
        {
            return bytes;
        }

        var quotaGb = this.configService.MonthlyBandwidthQuotaGb;
        return quotaGb > 0 ? quotaGb * 1024L * 1024L * 1024L : 0L;
    }

    private long ComputeBytesUsed()
    {
        var torrentBytes = this.GetTorrentTransferBytes();
        if (torrentBytes > 0)
        {
            return torrentBytes;
        }

        return this.GetMonthlySessionTransferBytes();
    }

    private long GetTorrentTransferBytes()
    {
        if (this.torrentService == null)
        {
            return 0;
        }

        try
        {
            return this.torrentService.GetAll().Sum(t => t.Downloaded + t.Uploaded);
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to aggregate torrent transfer totals for bandwidth quota check");
            return 0;
        }
    }

    private long GetMonthlySessionTransferBytes()
    {
        var sessionBytes = this.GetCurrentSessionTransferBytes();
        var period = this.GetCurrentUsagePeriod();
        var storedPeriod = this.configService.GetValue("MonthlyBandwidthUsagePeriod", string.Empty);
        var usageBytes = this.configService.GetValueLong("MonthlyBandwidthUsageBytes", 0L);

        if (!string.Equals(period, storedPeriod, StringComparison.Ordinal))
        {
            usageBytes = 0;
            this.PersistMonthlyUsage(period, usageBytes);
            this.lastSessionTransferBytes = sessionBytes;
            return usageBytes + sessionBytes;
        }

        if (this.lastSessionTransferBytes > 0 && sessionBytes >= this.lastSessionTransferBytes)
        {
            usageBytes += sessionBytes - this.lastSessionTransferBytes;
        }
        else if (this.lastSessionTransferBytes == 0 && sessionBytes > usageBytes)
        {
            usageBytes = sessionBytes;
        }

        this.lastSessionTransferBytes = sessionBytes;
        this.PersistMonthlyUsage(period, usageBytes);
        return usageBytes;
    }

    private long GetCurrentSessionTransferBytes()
    {
        if (this.downloadEngine == null)
        {
            return 0;
        }

        try
        {
            var metrics = this.downloadEngine.GetEngineMetrics();
            if (metrics == null)
            {
                return 0;
            }

            return metrics.TotalDataDownloaded + metrics.TotalDataUploaded
                + metrics.TotalProtocolDownloaded + metrics.TotalProtocolUploaded;
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to read engine metrics for bandwidth quota check");
            return 0;
        }
    }

    private string GetCurrentUsagePeriod()
    {
        var now = DateTime.UtcNow;
        var tzId = this.configService.TimeZone;
        if (!string.IsNullOrWhiteSpace(tzId) && TimeZoneInfo.TryFindSystemTimeZoneById(tzId, out var tz))
        {
            now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }

        return now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    private void PersistMonthlyUsage(string period, long usageBytes)
    {
        try
        {
            this.configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                ["MonthlyBandwidthUsagePeriod"] = period,
                ["MonthlyBandwidthUsageBytes"] = Math.Max(0L, usageBytes),
            });
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to persist monthly bandwidth usage snapshot");
        }
    }
}
