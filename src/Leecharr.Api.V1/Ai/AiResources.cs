// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace Leecharr.Api.V1.Ai;

public class AiParseReleaseRequest
{
    public string ReleaseName { get; set; }
}

public class AiNaturalSearchRequest
{
    public string Query { get; set; }
}

public class AiMalwareCheckRequest
{
    public int? TorrentId { get; set; }

    public string TorrentName { get; set; }

    public List<string> FileNames { get; set; } = new();
}

public class AiChatRequest
{
    public string Message { get; set; }

    public string Context { get; set; }
}

public class AiChatResponse
{
    public string Reply { get; set; }

    public string Provider { get; set; }

    public bool Success { get; set; } = true;

    public string Error { get; set; }
}
