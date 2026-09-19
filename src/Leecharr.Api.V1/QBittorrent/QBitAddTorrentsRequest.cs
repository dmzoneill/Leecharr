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

    public string Rename { get; set; }

    public long? UpLimit { get; set; }

    public long? Uplimit { get; set; }

    public long? DlLimit { get; set; }

    public long? Dllimit { get; set; }

    public long? EffectiveUpLimit => this.UpLimit ?? this.Uplimit;

    public long? EffectiveDlLimit => this.DlLimit ?? this.Dllimit;

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

    public string Root_folder { get; set; }

    public string RootFolder { get; set; }

    public string Skip_checking { get; set; }

    public string SkipChecking { get; set; }

    public string AutoTMM { get; set; }

    public string AutoTmm { get; set; }

    public bool IsFirstLastPiecePrio =>
        string.Equals(this.FirstLastPiecePrio, "true", StringComparison.OrdinalIgnoreCase);

    public bool IsSkipChecking =>
        string.Equals(this.Skip_checking, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(this.Skip_checking, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(this.SkipChecking, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(this.SkipChecking, "1", StringComparison.OrdinalIgnoreCase);

    public bool IsAutoTMM =>
        string.Equals(this.AutoTMM, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(this.AutoTMM, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(this.AutoTmm, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(this.AutoTmm, "1", StringComparison.OrdinalIgnoreCase);

    public bool IsRootFolder =>
        string.Equals(this.Root_folder, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(this.Root_folder, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(this.RootFolder, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(this.RootFolder, "1", StringComparison.OrdinalIgnoreCase);
}
