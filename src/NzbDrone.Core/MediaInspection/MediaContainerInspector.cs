// Copyright (c) PlaceholderCompany. All rights reserved.

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

    public string HdrFormat { get; set; }

    public string AudioCodec { get; set; }

    public string AudioChannels { get; set; }

    public int AudioSampleRate { get; set; }

    public int AudioBitDepth { get; set; }

    public List<string> SubtitleTracks { get; set; } = new();

    public double DurationSeconds { get; set; }

    public int Track { get; set; }

    public int TrackCount { get; set; }

    public int Disc { get; set; }

    public int DiscCount { get; set; }

    public string Title { get; set; }

    public string Artist { get; set; }

    public string Album { get; set; }

    public byte[] PictureData { get; set; }

    public List<byte[]> Pictures { get; set; } = new();

    public bool HasEmbeddedPicture => (this.PictureData != null && this.PictureData.Length > 0) || this.Pictures.Count > 0;
}

public interface IMediaContainerInspector
{
    MediaContainerInfo Inspect(Stream stream, string fileName = "");

    MediaContainerInfo InspectFile(string filePath);
}

public class MediaContainerInspector : IMediaContainerInspector
{
    private readonly IMediaInspectorProvider provider;

    public MediaContainerInspector()
    {
        this.provider = new TagLibInspectorProvider();
    }

    public MediaContainerInspector(IMediaInspectorProvider provider)
    {
        this.provider = provider;
    }

    public MediaContainerInfo Inspect(Stream stream, string fileName = "")
    {
        return this.provider.Inspect(stream, fileName);
    }

    public MediaContainerInfo InspectFile(string filePath)
    {
        return this.provider.InspectFile(filePath);
    }
}
