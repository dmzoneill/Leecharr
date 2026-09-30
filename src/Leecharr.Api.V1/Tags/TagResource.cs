// Copyright (c) PlaceholderCompany. All rights reserved.

using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Tags;

public class TagResource : RestResource
{
    public string Label { get; set; }

    public string Color { get; set; }

    public int? UploadLimitKbps { get; set; }

    public int? DownloadLimitKbps { get; set; }

    public double? MinSeedRatio { get; set; }

    public long? MinSeedTimeSeconds { get; set; }
}
