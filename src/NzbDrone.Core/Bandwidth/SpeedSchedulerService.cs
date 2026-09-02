using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.BitTorrent;
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
    Task ApplyCurrentLimitsAsync();
}

public class SpeedSchedulerService : ISpeedSchedulerService, IDisposable
{
    private readonly ISpeedScheduleRepository _repository;
    private readonly IConfigService _configService;
    private readonly IDownloadEngine _downloadEngine;
    private readonly System.Threading.Timer _timer;
    private readonly Logger _logger;

    public SpeedSchedulerService(
        ISpeedScheduleRepository repository,
        IConfigService configService,
        IDownloadEngine downloadEngine = null)
    {
        _repository = repository;
        _configService = configService;
        _downloadEngine = downloadEngine;
        _logger = LogManager.GetCurrentClassLogger();

        if (_downloadEngine != null)
        {
            _timer = new System.Threading.Timer(
                _ => { _ = ApplyCurrentLimitsAsync(); },
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(60));
        }
    }

    public async Task ApplyCurrentLimitsAsync()
    {
        if (_downloadEngine != null)
        {
            try
            {
                var limits = GetCurrentLimits();
                await _downloadEngine.SetRateLimitsAsync(limits.MaxDownloadSpeedKbps, limits.MaxUploadSpeedKbps);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to apply scheduled rate limits to engine");
            }
        }
    }

    public EffectiveSpeedLimits GetCurrentLimits(DateTime? currentTime = null)
    {
        var now = currentTime ?? DateTime.Now;
        var dayFlag = 1 << (int)now.DayOfWeek;
        var timeStr = now.ToString("HH:mm:ss");

        var activeSchedules = _repository.GetEnabled()
            .Where(s => (s.Days & dayFlag) != 0)
            .Where(s =>
            {
                if (string.Compare(s.StartTime, s.EndTime, StringComparison.Ordinal) <= 0)
                {
                    return string.Compare(timeStr, s.StartTime, StringComparison.Ordinal) >= 0 &&
                           string.Compare(timeStr, s.EndTime, StringComparison.Ordinal) <= 0;
                }
                else
                {
                    // Overnight schedule (e.g. 22:00 to 06:00)
                    return string.Compare(timeStr, s.StartTime, StringComparison.Ordinal) >= 0 ||
                           string.Compare(timeStr, s.EndTime, StringComparison.Ordinal) <= 0;
                }
            })
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

        if (_configService.AlternativeSpeedEnabled)
        {
            return new EffectiveSpeedLimits
            {
                MaxDownloadSpeedKbps = _configService.AltDownloadSpeedKbps,
                MaxUploadSpeedKbps = _configService.AltUploadSpeedKbps,
                IsThrottled = true,
                IsPaused = _configService.AltDownloadSpeedKbps == 0 && _configService.AltUploadSpeedKbps == 0
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
        if (schedule.IsThrottled || schedule.IsPaused)
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
        if (schedule.IsThrottled || schedule.IsPaused)
        {
            return schedule.MaxUploadSpeedKbps;
        }

        return _configService.MaxUploadSpeedKbps;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
