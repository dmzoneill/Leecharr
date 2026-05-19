using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Torrents;

public class TorrentFileResource : RestResource
{
    public int TorrentId { get; set; }
    public string Path { get; set; }
    public long Size { get; set; }
    public int PieceOffset { get; set; }
    public int PieceCount { get; set; }
    public int Priority { get; set; }
    public double Progress { get; set; }
}
