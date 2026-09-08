// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Leecharr.Api.V1.QBittorrent;

public class QBitAddTorrentsRequest
{
    public string Urls { get; set; }

    public List<IFormFile> Torrents { get; set; }

    public string Category { get; set; }

    public string Savepath { get; set; }

    public string DownloadPath { get; set; }

    public string Download_path { get; set; }

    public string Cookie { get; set; }

    public string Cookies { get; set; }

    public string Paused { get; set; }

    public string Stopped { get; set; }

    public string Tags { get; set; }

    public string SequentialDownload { get; set; }

    public string FirstLastPiecePrio { get; set; }

    public double? RatioLimit { get; set; }

    public int? SeedingTimeLimit { get; set; }

    public string ContentLayout { get; set; }

    public string EffectiveSavePath =>
        !string.IsNullOrWhiteSpace(this.Savepath)
            ? this.Savepath
            : (!string.IsNullOrWhiteSpace(this.DownloadPath) ? this.DownloadPath : this.Download_path);

    public string EffectiveCookie =>
        !string.IsNullOrWhiteSpace(this.Cookie) ? this.Cookie : this.Cookies;

    public bool IsPaused =>
        string.Equals(this.Paused, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(this.Stopped, "true", StringComparison.OrdinalIgnoreCase);

    public bool IsSequential =>
        string.Equals(this.SequentialDownload, "true", StringComparison.OrdinalIgnoreCase);

    public bool IsFirstLastPiecePrio =>
        string.Equals(this.FirstLastPiecePrio, "true", StringComparison.OrdinalIgnoreCase);
}
