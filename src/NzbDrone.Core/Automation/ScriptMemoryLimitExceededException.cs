#nullable enable

using System;

namespace NzbDrone.Core.Automation;

public sealed class ScriptMemoryLimitExceededException : Exception
{
    public ScriptMemoryLimitExceededException()
        : base("Script exceeded memory limit (32MB)")
    {
    }

    public ScriptMemoryLimitExceededException(string message)
        : base(message)
    {
    }
}
