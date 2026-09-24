// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Subtitles;

public class SubtitleTrackInfo
{
    public int TrackId { get; set; }
    public int? FileId { get; set; }
    public string Title { get; set; }
    public string Language { get; set; }
    public string TwoLetterCode { get; set; }
    public string Format { get; set; }
    public string Path { get; set; }
    public bool IsExternal { get; set; } = true;
    public bool IsForced { get; set; }
    public bool IsHearingImpaired { get; set; }
    public bool IsDefault { get; set; }
}
