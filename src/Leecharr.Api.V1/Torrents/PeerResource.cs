// Copyright (c) PlaceholderCompany. All rights reserved.

using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Torrents;

public class PeerResource : RestResource
{
    public string Ip { get; set; }

    public int Port { get; set; }

    public string Client { get; set; }

    public long UploadSpeed { get; set; }

    public long DownloadSpeed { get; set; }

    public long Uploaded { get; set; }

    public long Downloaded { get; set; }

    public double Progress { get; set; }

    public string Flags { get; set; }

    public string CountryCode { get; set; }

    public string CountryName { get; set; }

    public string City { get; set; }
}
