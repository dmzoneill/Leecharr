// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Bandwidth;

public class EffectiveSpeedLimits
{
    public int MaxDownloadSpeedKbps { get; set; }

    public int MaxUploadSpeedKbps { get; set; }

    public bool IsThrottled { get; set; }

    public bool IsDownloadPaused { get; set; }

    public bool IsUploadPaused { get; set; }

    public bool IsPaused
    {
        get => this.IsDownloadPaused || this.IsUploadPaused;
        set
        {
            this.IsDownloadPaused = value;
            this.IsUploadPaused = value;
        }
    }

    public bool HasActiveSchedule { get; set; }
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
    private readonly IEventAggregator eventAggregator;
    private readonly IQueueManagerService queueManagerService;
    private readonly ITorrentRepository torrentRepository;
    private readonly System.Threading.Timer timer;
    private readonly Logger logger;
    private readonly HashSet<int> schedulerPausedTorrentIds = new();
    private bool wasPausedByScheduler;

    public bool WasPausedByScheduler => this.wasPausedByScheduler;

    public IReadOnlyCollection<int> SchedulerPausedTorrentIds => this.schedulerPausedTorrentIds.ToList();

    public SpeedSchedulerService(
        ISpeedScheduleRepository repository,
        IConfigService configService,
        IDownloadEngine downloadEngine = null,
        IEventAggregator eventAggregator = null,
        IQueueManagerService queueManagerService = null,
        ITorrentRepository torrentRepository = null)
    {
        this.repository = repository;
        this.configService = configService;
        this.downloadEngine = downloadEngine;
        this.eventAggregator = eventAggregator;
        this.queueManagerService = queueManagerService;
        this.torrentRepository = torrentRepository;
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
                if (limits.IsDownloadPaused && limits.IsUploadPaused)
                {
                    if (!this.wasPausedByScheduler)
                    {
                        this.wasPausedByScheduler = true;
                        this.schedulerPausedTorrentIds.Clear();

                        var tasks = this.downloadEngine.GetAllTasks();
                        if (tasks != null)
                        {
                            foreach (var task in tasks)
                            {
                                if (task.Status is not (TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Queued or TorrentStatus.Completed or TorrentStatus.QueuedForChecking))
                                {
                                    this.schedulerPausedTorrentIds.Add(task.TorrentId);
                                }
                            }
                        }

                        if (this.torrentRepository != null)
                        {
                            try
                            {
                                var activeDbTorrents = this.torrentRepository.All()
                                    .Where(t => t.Status is not (TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Queued or TorrentStatus.Completed or TorrentStatus.QueuedForChecking))
                                    .Select(t => t.Id);

                                foreach (var id in activeDbTorrents)
                                {
                                    this.schedulerPausedTorrentIds.Add(id);
                                }
                            }
                            catch (Exception ex)
                            {
                                this.logger.Debug(ex, "Could not query torrent repository for active torrents");
                            }
                        }
                    }

                    await this.downloadEngine.PauseAllAsync();
                }
                else
                {
                    if (this.wasPausedByScheduler)
                    {
                        this.wasPausedByScheduler = false;
                        var toResume = this.schedulerPausedTorrentIds.ToList();
                        this.schedulerPausedTorrentIds.Clear();

                        foreach (var torrentId in toResume)
                        {
                            if (this.torrentRepository != null)
                            {
                                var torrent = this.torrentRepository.Get(torrentId);
                                if (torrent == null)
                                {
                                    this.logger.Info("Skipping scheduler resume for torrent {0}: torrent was deleted", torrentId);
                                    continue;
                                }

                                if (torrent.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Completed)
                                {
                                    this.logger.Info("Skipping scheduler resume for torrent {0}: database status is {1}", torrentId, torrent.Status);
                                    continue;
                                }
                            }
                            else
                            {
                                var taskCheck = this.downloadEngine.GetTask(torrentId);
                                if (taskCheck != null && taskCheck.Status is TorrentStatus.Paused)
                                {
                                    this.logger.Info("Skipping scheduler resume for torrent {0}: task status is Paused", torrentId);
                                    continue;
                                }
                            }

                            var task = this.downloadEngine.GetTask(torrentId);
                            if (task != null && task.Status is TorrentStatus.Stopped or TorrentStatus.Completed)
                            {
                                this.logger.Info("Skipping scheduler resume for torrent {0}: task status is {1}", torrentId, task.Status);
                                continue;
                            }

                            try
                            {
                                await this.downloadEngine.ResumeTorrentAsync(torrentId);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Warn(ex, "Failed to resume torrent {0} after scheduler pause", torrentId);
                            }
                        }

                        if (this.queueManagerService != null)
                        {
                            try
                            {
                                await this.queueManagerService.ProcessQueueAsync();
                            }
                            catch (Exception ex)
                            {
                                this.logger.Warn(ex, "Failed to process queue after scheduler resume");
                            }
                        }
                    }

                    var downloadLimit = limits.IsDownloadPaused ? 1 : limits.MaxDownloadSpeedKbps;
                    var uploadLimit = limits.IsUploadPaused ? 1 : limits.MaxUploadSpeedKbps;
                    await this.downloadEngine.SetRateLimitsAsync(downloadLimit, uploadLimit);
                }

                if (this.eventAggregator != null)
                {
                    var metrics = this.downloadEngine.GetEngineMetrics();
                    if (metrics != null)
                    {
                        var maxDlBytes = limits.MaxDownloadSpeedKbps > 0 ? (long)limits.MaxDownloadSpeedKbps * 1024L : 0;
                        var maxUlBytes = limits.MaxUploadSpeedKbps > 0 ? (long)limits.MaxUploadSpeedKbps * 1024L : 0;
                        if ((maxDlBytes > 0 && metrics.TotalDownloadSpeed >= maxDlBytes) || (maxUlBytes > 0 && metrics.TotalUploadSpeed >= maxUlBytes))
                        {
                            this.eventAggregator.PublishEvent(new SpeedThresholdExceededEvent(metrics.TotalDownloadSpeed, metrics.TotalUploadSpeed, metrics.ActiveTorrents));
                        }
                    }
                }
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
        var now = this.GetEffectiveDateTime(currentTime);

        if (this.configService.AlternativeSpeedEnabled)
        {
            var isDownloadPaused = this.configService.AltDownloadSpeedKbps < 0;
            var isUploadPaused = this.configService.AltUploadSpeedKbps < 0;
            var isThrottled = this.configService.AltDownloadSpeedKbps > 0 || this.configService.AltUploadSpeedKbps > 0;
            return new EffectiveSpeedLimits
            {
                MaxDownloadSpeedKbps = this.configService.AltDownloadSpeedKbps < 0 ? 0 : this.configService.AltDownloadSpeedKbps,
                MaxUploadSpeedKbps = this.configService.AltUploadSpeedKbps < 0 ? 0 : this.configService.AltUploadSpeedKbps,
                IsThrottled = isThrottled,
                IsDownloadPaused = isDownloadPaused,
                IsUploadPaused = isUploadPaused,
                HasActiveSchedule = true,
            };
        }

        var todayFlag = 1 << (int)now.DayOfWeek;
        var prevDayFlag = 1 << (((int)now.DayOfWeek + 6) % 7);
        var currentTimeOnly = TimeOnly.FromDateTime(now);

        var activeSchedules = this.repository.GetEnabled()
            .Where(s =>
            {
                if (!TimeOnly.TryParse(s.StartTime, CultureInfo.InvariantCulture, out var startTime) ||
                    !TimeOnly.TryParse(s.EndTime, CultureInfo.InvariantCulture, out var endTime))
                {
                    return false;
                }

                if (s.EndTime != null && s.EndTime.Count(c => c == ':') == 1)
                {
                    // minute-precision end time includes the entire minute (e.g. 23:59:59.9999999)
                    endTime = (endTime.Hour == 23 && endTime.Minute == 59)
                        ? TimeOnly.MaxValue
                        : new TimeOnly(endTime.Hour, endTime.Minute, 59, 999, 999);
                }
                else if (endTime.Hour == 23 && endTime.Minute == 59 && endTime.Second == 59)
                {
                    // "23:59:59" end time includes the final milliseconds of the day
                    endTime = TimeOnly.MaxValue;
                }
                else if (s.EndTime != null && s.EndTime.Count(c => c == ':') == 2)
                {
                    // second-precision end time includes the entire second (e.g. 23:59:01.9999999)
                    endTime = new TimeOnly(endTime.Hour, endTime.Minute, endTime.Second, 999, 999);
                }

                if (startTime <= endTime)
                {
                    return (s.Days & todayFlag) != 0 &&
                        currentTimeOnly >= startTime &&
                        currentTimeOnly <= endTime;
                }
                else
                {
                    // Overnight schedule (e.g. 22:00 to 06:00)
                    if (currentTimeOnly >= startTime)
                    {
                        return (s.Days & todayFlag) != 0;
                    }

                    if (currentTimeOnly <= endTime)
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
            var isDownloadPaused = match.MaxDownloadSpeed < 0;
            var isUploadPaused = match.MaxUploadSpeed < 0;
            var isThrottled = match.MaxDownloadSpeed > 0 || match.MaxUploadSpeed > 0;
            return new EffectiveSpeedLimits
            {
                MaxDownloadSpeedKbps = match.MaxDownloadSpeed < 0 ? 0 : match.MaxDownloadSpeed,
                MaxUploadSpeedKbps = match.MaxUploadSpeed < 0 ? 0 : match.MaxUploadSpeed,
                IsThrottled = isThrottled,
                IsDownloadPaused = isDownloadPaused,
                IsUploadPaused = isUploadPaused,
                HasActiveSchedule = true,
            };
        }

        if (this.IsConfigScheduleActive(now))
        {
            var isDownloadPaused = this.configService.AltDownloadSpeedKbps < 0;
            var isUploadPaused = this.configService.AltUploadSpeedKbps < 0;
            var isThrottled = this.configService.AltDownloadSpeedKbps > 0 || this.configService.AltUploadSpeedKbps > 0;
            return new EffectiveSpeedLimits
            {
                MaxDownloadSpeedKbps = this.configService.AltDownloadSpeedKbps < 0 ? 0 : this.configService.AltDownloadSpeedKbps,
                MaxUploadSpeedKbps = this.configService.AltUploadSpeedKbps < 0 ? 0 : this.configService.AltUploadSpeedKbps,
                IsThrottled = isThrottled,
                IsDownloadPaused = isDownloadPaused,
                IsUploadPaused = isUploadPaused,
                HasActiveSchedule = true,
            };
        }

        return new EffectiveSpeedLimits
        {
            MaxDownloadSpeedKbps = this.configService.MaxDownloadSpeedKbps,
            MaxUploadSpeedKbps = this.configService.MaxUploadSpeedKbps,
            IsThrottled = false,
            IsDownloadPaused = false,
            IsUploadPaused = false,
            HasActiveSchedule = false,
        };
    }

    public int ResolveEffectiveDownloadLimit(int torrentLimit, int categoryLimit, DateTime? currentTime = null)
    {
        if (torrentLimit > 0)
        {
            return torrentLimit;
        }

        if (categoryLimit > 0)
        {
            return categoryLimit;
        }

        return 0;
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

        return 0;
    }

    public void Dispose()
    {
        this.timer?.Dispose();
    }

    private bool IsConfigDayActive(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Sunday => this.configService.SchedulerSunday,
            DayOfWeek.Monday => this.configService.SchedulerMonday,
            DayOfWeek.Tuesday => this.configService.SchedulerTuesday,
            DayOfWeek.Wednesday => this.configService.SchedulerWednesday,
            DayOfWeek.Thursday => this.configService.SchedulerThursday,
            DayOfWeek.Friday => this.configService.SchedulerFriday,
            DayOfWeek.Saturday => this.configService.SchedulerSaturday,
            _ => false,
        };
    }

    private bool IsConfigScheduleActive(DateTime now)
    {
        if (!this.configService.SchedulerEnabled)
        {
            return false;
        }

        var startHour = Math.Clamp(this.configService.SchedulerStartHour, 0, 23);
        var startMinute = Math.Clamp(this.configService.SchedulerStartMinute, 0, 59);
        var endHour = Math.Clamp(this.configService.SchedulerEndHour, 0, 23);
        var endMinute = Math.Clamp(this.configService.SchedulerEndMinute, 0, 59);

        var startTime = new TimeOnly(startHour, startMinute);
        var endTime = (endHour == 23 && endMinute == 59)
            ? TimeOnly.MaxValue
            : new TimeOnly(endHour, endMinute, 59, 999, 999);
        var currentTimeOnly = TimeOnly.FromDateTime(now);
        var today = now.DayOfWeek;
        var prevDay = (DayOfWeek)(((int)now.DayOfWeek + 6) % 7);

        if (startTime <= endTime)
        {
            return this.IsConfigDayActive(today) &&
                currentTimeOnly >= startTime &&
                currentTimeOnly <= endTime;
        }
        else
        {
            // Overnight schedule (e.g. 22:00 to 06:00)
            if (currentTimeOnly >= startTime)
            {
                return this.IsConfigDayActive(today);
            }

            if (currentTimeOnly <= endTime)
            {
                return this.IsConfigDayActive(prevDay);
            }

            return false;
        }
    }

    private DateTime GetEffectiveDateTime(DateTime? currentTime)
    {
        if (currentTime.HasValue)
        {
            var dt = currentTime.Value;
            if (dt.Kind == DateTimeKind.Utc)
            {
                if (this.TryGetConfiguredTimeZone(out var tzFromUtc))
                {
                    return TimeZoneInfo.ConvertTimeFromUtc(dt, tzFromUtc);
                }

                return TimeZoneInfo.ConvertTimeFromUtc(dt, TimeZoneInfo.Local);
            }

            return dt;
        }

        if (this.TryGetConfiguredTimeZone(out var tz))
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }

        return DateTime.Now;
    }

    private bool TryGetConfiguredTimeZone(out TimeZoneInfo timeZone)
    {
        timeZone = null;
        var tzId = this.configService?.TimeZone;
        if (string.IsNullOrWhiteSpace(tzId))
        {
            return false;
        }

        try
        {
            return TimeZoneInfo.TryFindSystemTimeZoneById(tzId, out timeZone);
        }
        catch
        {
            return false;
        }
    }
}
