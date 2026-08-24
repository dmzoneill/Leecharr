namespace NzbDrone.Core.MediaInspection;

public class MediaInspectorCapabilities
{
    public bool SupportsDolbyVision { get; set; }
    public bool SupportsHdr10Plus { get; set; }
    public bool SupportsEac3Atmos { get; set; }
    public bool SupportsTrueHd { get; set; }
    public bool SupportsDtsX { get; set; }
    public bool SupportsSubtitleTracks { get; set; }
    public bool SupportsAudioStreamTracks { get; set; }
    public bool SupportsVideoStreamTracks { get; set; }
    public bool SupportsChapters { get; set; }
    public bool SupportsVideoThumbnails { get; set; }
    public bool SupportsPureManagedStreams { get; set; }
}
