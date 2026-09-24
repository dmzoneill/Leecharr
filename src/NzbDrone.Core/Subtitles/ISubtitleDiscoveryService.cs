// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Subtitles;

public interface ISubtitleDiscoveryService
{
    List<SubtitleTrackInfo> DiscoverSubtitles(TorrentFile videoFile, IEnumerable<TorrentFile> allTorrentFiles);
    List<SubtitleTrackInfo> DiscoverSubtitles(string videoFilePath);
    List<SubtitleTrackInfo> DiscoverSubtitles(string videoFilePath, IEnumerable<string> allFilePaths);
    bool IsSubtitleFile(string path);
    bool IsMediaFile(string path);
}
