// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Instrumentation;

namespace NzbDrone.Core.HealthCheck.Checks;

/// <summary>
/// Health check that warns or errors when file descriptor usage nears the operating system soft limit.
/// </summary>
public class FileDescriptorExhaustionCheck : IHealthCheck
{
    private readonly IFileDescriptorProvider fileDescriptorProvider;

    public FileDescriptorExhaustionCheck(IFileDescriptorProvider fileDescriptorProvider = null)
    {
        this.fileDescriptorProvider = fileDescriptorProvider ?? new FileDescriptorProvider();
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var usage = this.fileDescriptorProvider.GetFileDescriptorUsagePercentage();
        if (!usage.HasValue)
        {
            return Task.FromResult(HealthCheckResult.Ok("FileDescriptors"));
        }

        var count = this.fileDescriptorProvider.GetOpenFileDescriptorCount();
        var max = this.fileDescriptorProvider.GetMaxFileDescriptors();

        if (usage.Value >= 90.0)
        {
            return Task.FromResult(HealthCheckResult.Error(
                "FileDescriptors",
                $"File descriptor usage is at {usage.Value:0.#}% ({count}/{max}). Risk of socket and file I/O failure (EMFILE). Increase nofile ulimit (e.g. 'ulimits: nofile: 65536' in podman-compose / compose.yaml or /etc/security/limits.conf)."));
        }

        if (usage.Value >= 80.0)
        {
            return Task.FromResult(HealthCheckResult.Warning(
                "FileDescriptors",
                $"File descriptor usage is at {usage.Value:0.#}% ({count}/{max}). Consider increasing nofile ulimit before descriptor exhaustion occurs."));
        }

        return Task.FromResult(HealthCheckResult.Ok("FileDescriptors"));
    }
}
