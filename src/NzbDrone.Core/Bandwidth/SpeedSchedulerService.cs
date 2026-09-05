// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Bandwidth;

public class EffectiveSpeedLimits
{
    public int MaxDownloadSpeedKbps { get; set; }

    public int MaxUploadSpeedKbps { get; set; }

    public bool IsThrottled { get; set; }

    public bool IsPaused { get; set; }
}

public interface ISpeedSchedulerService
{
    EffectiveSpeedLimits GetCurrentLimits(DateTime? currentTime = null);

    int ResolveEffectiveDownloadLimit(int torrentLimit, int categoryLimit, DateTime? currentTime = null);

    int ResolveEffectiveUploadLimit(int torrentLimit, int categoryLimit, DateTime? currentTime = null);

    Task ApplyCurrentLimitsAsync();
}

public class SpeedSchedulerService : ISpeedSchedulerService, IHandle<ConfigSavedEvent>, IDisposable
{
    private readonly ISpeedScheduleRepository repository;
    private readonly IConfigService configService;
    private readonly IDownloadEngine downloadEngine;
    private readonly System.Threading.Timer timer;
    private readonly Logger logger;

    public SpeedSchedulerService(
        ISpeedScheduleRepository repository,
        IConfigService configService,
        IDownloadEngine downloadEngine = null)
    {
        this.repository = repository;
        this.configService = configService;
        this.downloadEngine = downloadEngine;
        this.logger = LogManager.GetCurrentClassLogger();

        if (this.downloadEngine != null)
        {
            this.timer = new System.Threading.Timer(
                _ => { _ = this.ApplyCurrentLimitsAsync(); },
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(60));
        }
    }

    public async Task ApplyCurrentLimitsAsync()
    {
        if (this.downloadEngine != null)
        {
            try
            {
                var limits = this.GetCurrentLimits();
                await this.downloadEngine.SetRateLimitsAsync(limits.MaxDownloadSpeedKbps, limits.MaxUploadSpeedKbps);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to apply scheduled rate limits to engine");
            }
        }
    }

    public void Handle(ConfigSavedEvent message)
    {
        _ = this.ApplyCurrentLimitsAsync();
    }

    public EffectiveSpeedLimits GetCurrentLimits(DateTime? currentTime = null)
    {
        var now = currentTime ?? DateTime.Now;
        var todayFlag = 1 << (int)now.DayOfWeek;
        var prevDayFlag = 1 << (((int)now.DayOfWeek + 6) % 7);
        var timeStr = now.ToString("HH:mm:ss");

        var activeSchedules = this.repository.GetEnabled()
            .Where(s =>
            {
                if (string.Compare(s.StartTime, s.EndTime, StringComparison.Ordinal) <= 0)
                {
                    return (s.Days & todayFlag) != 0 &&
                        string.Compare(timeStr, s.StartTime, StringComparison.Ordinal) >= 0 &&
                        string.Compare(timeStr, s.EndTime, StringComparison.Ordinal) <= 0;
                }
                else
                {
                    // Overnight schedule (e.g. 22:00 to 06:00)
                    if (string.Compare(timeStr, s.StartTime, StringComparison.Ordinal) >= 0)
                    {
                        return (s.Days & todayFlag) != 0;
                    }

                    if (string.Compare(timeStr, s.EndTime, StringComparison.Ordinal) <= 0)
                    {
                        return (s.Days & prevDayFlag) != 0;
                    }

                    return false;
                }
            })
            .OrderByDescending(s => s.Priority)
            .ToList();

        if (activeSchedules.Count > 0)
        {
            var match = activeSchedules.First();
            var isPaused = match.MaxDownloadSpeed < 0 || match.MaxUploadSpeed < 0;
            var isThrottled = match.MaxDownloadSpeed > 0 || match.MaxUploadSpeed > 0;
            return new EffectiveSpeedLimits
            {
                MaxDownloadSpeedKbps = match.MaxDownloadSpeed < 0 ? 0 : match.MaxDownloadSpeed > 0 ? match.MaxDownloadSpeed : this.configService.MaxDownloadSpeedKbps,
                MaxUploadSpeedKbps = match.MaxUploadSpeed < 0 ? 0 : match.MaxUploadSpeed > 0 ? match.MaxUploadSpeed : this.configService.MaxUploadSpeedKbps,
                IsThrottled = isThrottled,
                IsPaused = isPaused,
            };
        }

        if (this.configService.AlternativeSpeedEnabled)
        {
            var isPaused = this.configService.AltDownloadSpeedKbps < 0 || this.configService.AltUploadSpeedKbps < 0;
            var isThrottled = this.configService.AltDownloadSpeedKbps > 0 || this.configService.AltUploadSpeedKbps > 0;
            return new EffectiveSpeedLimits
            {
                MaxDownloadSpeedKbps = this.configService.AltDownloadSpeedKbps < 0 ? 0 : this.configService.AltDownloadSpeedKbps > 0 ? this.configService.AltDownloadSpeedKbps : this.configService.MaxDownloadSpeedKbps,
                MaxUploadSpeedKbps = this.configService.AltUploadSpeedKbps < 0 ? 0 : this.configService.AltUploadSpeedKbps > 0 ? this.configService.AltUploadSpeedKbps : this.configService.MaxUploadSpeedKbps,
                IsThrottled = isThrottled,
                IsPaused = isPaused,
            };
        }

        return new EffectiveSpeedLimits
        {
            MaxDownloadSpeedKbps = this.configService.MaxDownloadSpeedKbps,
            MaxUploadSpeedKbps = this.configService.MaxUploadSpeedKbps,
            IsThrottled = false,
            IsPaused = false,
        };
    }

    public int ResolveEffectiveDownloadLimit(int torrentLimit, int categoryLimit, DateTime? currentTime = null)
    {
        // 4-level hierarchy: Torrent Override > Category Limit > Schedule Limit > Global Limit
        if (torrentLimit > 0)
        {
            return torrentLimit;
        }

        if (categoryLimit > 0)
        {
            return categoryLimit;
        }

        var schedule = this.GetCurrentLimits(currentTime);
        if (schedule.MaxDownloadSpeedKbps > 0 || (schedule.IsPaused && schedule.MaxDownloadSpeedKbps == 0))
        {
            return schedule.MaxDownloadSpeedKbps;
        }

        return this.configService.MaxDownloadSpeedKbps;
    }

    public int ResolveEffectiveUploadLimit(int torrentLimit, int categoryLimit, DateTime? currentTime = null)
    {
        if (torrentLimit > 0)
        {
            return torrentLimit;
        }

        if (categoryLimit > 0)
        {
            return categoryLimit;
        }

        var schedule = this.GetCurrentLimits(currentTime);
        if (schedule.MaxUploadSpeedKbps > 0 || (schedule.IsPaused && schedule.MaxUploadSpeedKbps == 0))
        {
            return schedule.MaxUploadSpeedKbps;
        }

        return this.configService.MaxUploadSpeedKbps;
    }

    public void Dispose()
    {
        this.timer?.Dispose();
    }
}
