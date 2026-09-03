// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Http;

public interface ISafeHttpClientService
{
    Task<byte[]> DownloadBytesAsync(string url, long maxSizeBytes = 10 * 1024 * 1024, CancellationToken cancellationToken = default);

    Task<byte[]> DownloadBytesAsync(Uri uri, long maxSizeBytes = 10 * 1024 * 1024, CancellationToken cancellationToken = default);

    void ValidateUrl(string url);

    void ValidateUri(Uri uri);

    bool IsBlockedIp(IPAddress ip);
}
