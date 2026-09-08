// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Http;

public interface ISafeHttpClientService
{
    bool AllowPrivateNetworkRequests { get; set; }

    string AllowedSsrfHostnames { get; set; }

    string AllowedSsrfSubnets { get; set; }

    Task<byte[]> DownloadBytesAsync(string url, long maxSizeBytes = 10 * 1024 * 1024, CancellationToken cancellationToken = default);

    Task<byte[]> DownloadBytesAsync(string url, long maxSizeBytes, TimeSpan? timeout, CancellationToken cancellationToken = default);

    Task<byte[]> DownloadBytesAsync(Uri uri, long maxSizeBytes = 10 * 1024 * 1024, CancellationToken cancellationToken = default);

    Task<byte[]> DownloadBytesAsync(Uri uri, long maxSizeBytes, IDictionary<string, string> customHeaders, CancellationToken cancellationToken = default)
        => this.DownloadBytesAsync(uri, maxSizeBytes, customHeaders, null, cancellationToken);

    Task<byte[]> DownloadBytesAsync(Uri uri, long maxSizeBytes, IDictionary<string, string> customHeaders, TimeSpan? timeout, CancellationToken cancellationToken = default);

    Task<string> DownloadStringAsync(string url, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    Task<string> DownloadStringAsync(Uri uri, IDictionary<string, string> customHeaders = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    void ValidateUrl(string url);

    void ValidateUri(Uri uri);

    bool IsBlockedIp(IPAddress ip);

    bool IsAllowedHost(string host);

    bool IsAllowedIp(IPAddress ip);
}
