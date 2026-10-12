// Copyright (c) FeedItOut. All rights reserved.

using System.Threading;

namespace NzbDrone.Core.Ai;

/// <summary>
/// Per-async-flow fallback flag for chat labeling. Avoids cross-request races on shared AI provider singletons.
/// </summary>
internal static class AiChatFallbackContext
{
    private static readonly AsyncLocal<bool?> LastChatUsedFallback = new();
    private static bool lastCommittedFallback;

    public static bool LastChatUsedFallbackValue => LastChatUsedFallback.Value ?? lastCommittedFallback;

    public static void SetLastChatUsedFallback(bool value)
    {
        LastChatUsedFallback.Value = value;
        lastCommittedFallback = value;
    }
}
