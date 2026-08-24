using System.Collections.Generic;
using System.IO;

namespace NzbDrone.Core.MediaInspection;

public class MediaContainerInfo
{
    public string ContainerFormat { get; set; }
    public string VideoCodec { get; set; }
    public string Resolution { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string HdrFormat { get; set; } = "SDR";
    public string AudioCodec { get; set; }
    public string AudioChannels { get; set; }
    public int AudioSampleRate { get; set; }
    public int AudioBitDepth { get; set; }
    public List<string> SubtitleTracks { get; set; } = new();
    public double DurationSeconds { get; set; }
}

public interface IMediaContainerInspector
{
    MediaContainerInfo Inspect(Stream stream, string fileName = "");
    MediaContainerInfo InspectFile(string filePath);
}

public class MediaContainerInspector : IMediaContainerInspector
{
    private readonly IMediaInspectorProvider _provider;

    public MediaContainerInspector()
    {
        _provider = new TagLibInspectorProvider();
    }

    public MediaContainerInspector(IMediaInspectorProvider provider)
    {
        _provider = provider;
    }

    public MediaContainerInfo Inspect(Stream stream, string fileName = "")
    {
        return _provider.Inspect(stream, fileName);
    }

    public MediaContainerInfo InspectFile(string filePath)
    {
        return _provider.InspectFile(filePath);
    }
}
