using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Extraction;

public class DynamicArchiveExtractorProxy : IArchiveExtractorService, IArchiveExtractorManager, IDisposable
{
    private readonly IEnumerable<IArchiveExtractorProvider> _availableProviders;
    private readonly IConfigService _configService;
    private readonly IDiskProvider _diskProvider;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private IArchiveExtractorProvider _activeProvider;
    private bool _disposed;

    public IArchiveExtractorProvider ActiveProvider => Volatile.Read(ref _activeProvider);
    public string ActiveProviderId => Volatile.Read(ref _activeProvider)?.ProviderId ?? "SharpCompress";

    public DynamicArchiveExtractorProxy(
        IEnumerable<IArchiveExtractorProvider> availableProviders,
        IConfigService configService,
        IDiskProvider diskProvider,
        IEventAggregator eventAggregator)
    {
        _availableProviders = availableProviders ?? Array.Empty<IArchiveExtractorProvider>();
        _configService = configService;
        _diskProvider = diskProvider;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();

        var desiredProviderId = _configService.ActiveArchiveExtractor;
        _activeProvider = _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredProviderId, StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("SharpCompress", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault();

        if (_activeProvider == null)
        {
            throw new InvalidOperationException("No archive extractor providers are registered in the system container.");
        }

        _logger.Info("DynamicArchiveExtractorProxy initialized with active provider: {0} ({1})", _activeProvider.DisplayName, _activeProvider.ProviderId);
    }

    public IEnumerable<IArchiveExtractorProvider> GetProviders()
    {
        return _availableProviders;
    }

    public IArchiveExtractorProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ExtractorHealthCheckResult> ProbeProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return new ExtractorHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Extractor provider '{providerId}' is not recognized or registered.",
                Warnings = new List<string> { "Provider identifier not found in extractor registry." }
            };
        }

        return await provider.ProbeHealthAsync(cancellationToken);
    }

    public async Task<ExtractorSwitchResult> SwitchProviderAsync(string targetProviderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetProviderId))
        {
            return new ExtractorSwitchResult
            {
                Success = false,
                Error = "Target provider ID must not be empty."
            };
        }

        var targetProvider = GetProvider(targetProviderId);
        if (targetProvider == null)
        {
            return new ExtractorSwitchResult
            {
                Success = false,
                Error = $"Target extractor provider '{targetProviderId}' is not registered."
            };
        }

        var current = Volatile.Read(ref _activeProvider);
        if (string.Equals(current.ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return new ExtractorSwitchResult
            {
                Success = true,
                PreviousProvider = current.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Extractor provider '{targetProvider.DisplayName}' is already active."
            };
        }

        await _switchLock.WaitAsync(cancellationToken);
        try
        {
            var health = await targetProvider.ProbeHealthAsync(cancellationToken);
            if (!health.IsHealthy)
            {
                return new ExtractorSwitchResult
                {
                    Success = false,
                    PreviousProvider = Volatile.Read(ref _activeProvider).ProviderId,
                    ActiveProvider = Volatile.Read(ref _activeProvider).ProviderId,
                    Error = $"Cannot switch to extractor provider '{targetProvider.DisplayName}': health check failed ({health.StatusMessage})."
                };
            }

            var previousProvider = Volatile.Read(ref _activeProvider);
            _logger.Info("Switching archive extractor: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);

            Volatile.Write(ref _activeProvider, targetProvider);

            _configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveArchiveExtractor", targetProvider.ProviderId }
            });

            _eventAggregator.PublishEvent(new ArchiveExtractorSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));

            _logger.Info("Archive extractor hot-swap completed: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);

            return new ExtractorSwitchResult
            {
                Success = true,
                PreviousProvider = previousProvider.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Successfully switched archive extractor to {targetProvider.DisplayName}."
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Fatal error during extractor hot-swap to {0}", targetProviderId);
            return new ExtractorSwitchResult
            {
                Success = false,
                PreviousProvider = Volatile.Read(ref _activeProvider)?.ProviderId,
                ActiveProvider = Volatile.Read(ref _activeProvider)?.ProviderId,
                Error = $"Extractor switch failed: {ex.Message}"
            };
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public bool IsArchiveFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var active = Volatile.Read(ref _activeProvider);
        if (active != null && active.CanExtract(filePath))
        {
            return true;
        }

        return _availableProviders.Any(p => p.CanExtract(filePath));
    }

    public async Task<bool> ExtractArchiveAsync(string archiveFilePath, string destinationDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !_diskProvider.FileExists(archiveFilePath))
        {
            _logger.Warn("Archive file does not exist: {0}", archiveFilePath);
            return false;
        }

        var targetDir = destinationDirectory;
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            targetDir = Path.GetDirectoryName(archiveFilePath) ?? "/tmp";
        }

        _diskProvider.EnsureFolder(targetDir);

        var active = Volatile.Read(ref _activeProvider);
        var success = await active.ExtractAsync(archiveFilePath, targetDir);

        if (!success && !active.ProviderId.Equals("SharpCompress", StringComparison.OrdinalIgnoreCase))
        {
            var fallback = GetProvider("SharpCompress");
            if (fallback != null)
            {
                _logger.Warn("Active extractor '{0}' failed for '{1}'. Attempting fallback to SharpCompress...", active.ProviderId, archiveFilePath);
                try
                {
                    success = await fallback.ExtractAsync(archiveFilePath, targetDir);
                    if (success)
                    {
                        _logger.Info("SharpCompress fallback extraction succeeded for '{0}'.", archiveFilePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "SharpCompress fallback extraction failed for '{0}'.", archiveFilePath);
                }
            }
        }

        return success;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _switchLock.Dispose();
        }
    }
}
