// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Notifications;

public interface IEpisodicParser
{
    (int? SeasonNumber, int? EpisodeNumber, string EpisodeTitle) ExtractEpisodicInfo(string name);

    (string ContainerFormat, string Resolution, string VideoCodec, string HdrFormat, string AudioCodec, string AudioChannels, string AudioLanguage, List<string> SubtitleLanguages) ExtractStreamSpecs(string mediaInfoJson);

    string EscapeMarkdown(string text);

    string FormatEta(long seconds);
}
