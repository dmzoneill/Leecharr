using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;

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
}

public class SpeedSchedulerService : ISpeedSchedulerService
{
    private readonly ISpeedScheduleRepository _repository;
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public SpeedSchedulerService(
        ISpeedScheduleRepository repository,
        IConfigService configService)
    {
        _repository = repository;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public EffectiveSpeedLimits GetCurrentLimits(DateTime? currentTime = null)
    {
        var now = currentTime ?? DateTime.Now;
        var dayFlag = 1 << (int)now.DayOfWeek;
        var timeStr = now.ToString("HH:mm:ss");

        var activeSchedules = _repository.GetEnabled()
            .Where(s => (s.Days & dayFlag) != 0)
            .Where(s => string.Compare(timeStr, s.StartTime, StringComparison.Ordinal) >= 0 &&
                        string.Compare(timeStr, s.EndTime, StringComparison.Ordinal) <= 0)
            .OrderByDescending(s => s.Priority)
            .ToList();

        if (activeSchedules.Count > 0)
        {
            var match = activeSchedules.First();
            return new EffectiveSpeedLimits
            {
                MaxDownloadSpeedKbps = match.MaxDownloadSpeed,
                MaxUploadSpeedKbps = match.MaxUploadSpeed,
                IsThrottled = true,
                IsPaused = match.MaxDownloadSpeed == 0 && match.MaxUploadSpeed == 0
            };
        }

        return new EffectiveSpeedLimits
        {
            MaxDownloadSpeedKbps = _configService.MaxDownloadSpeedKbps,
            MaxUploadSpeedKbps = _configService.MaxUploadSpeedKbps,
            IsThrottled = false,
            IsPaused = false
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

        var schedule = GetCurrentLimits(currentTime);
        if (schedule.MaxDownloadSpeedKbps > 0)
        {
            return schedule.MaxDownloadSpeedKbps;
        }

        return _configService.MaxDownloadSpeedKbps;
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

        var schedule = GetCurrentLimits(currentTime);
        if (schedule.MaxUploadSpeedKbps > 0)
        {
            return schedule.MaxUploadSpeedKbps;
        }

        return _configService.MaxUploadSpeedKbps;
    }
}
