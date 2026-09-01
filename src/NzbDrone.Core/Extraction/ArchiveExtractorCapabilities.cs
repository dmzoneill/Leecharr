namespace NzbDrone.Core.Extraction;

public class ArchiveExtractorCapabilities
{
    public bool SupportsRar5 { get; set; }
    public bool Supports7z { get; set; }
    public bool SupportsZip { get; set; }
    public bool SupportsTarGz { get; set; }
    public bool SupportsMultiPart { get; set; }
    public bool SupportsPasswordProtected { get; set; }
    public bool SupportsSolidArchives { get; set; }
    public bool SupportsRecoveryVolumes { get; set; }
}
