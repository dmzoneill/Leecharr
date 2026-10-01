// Copyright (c) FeedItOut. All rights reserved.

namespace NzbDrone.Core.Ai;

public interface IFallbackAwareAiProvider
{
    bool LastChatUsedFallback { get; }
}
