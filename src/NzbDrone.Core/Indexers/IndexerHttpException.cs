// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Net.Http;

namespace NzbDrone.Core.Indexers;

public class IndexerHttpException : HttpRequestException
{
    public IndexerHttpException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter)
        : base(message, null, statusCode)
    {
        this.RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}
