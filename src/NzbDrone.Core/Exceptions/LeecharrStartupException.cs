using System;

namespace NzbDrone.Core.Exceptions;

public class LeecharrStartupException : Exception
{
    public LeecharrStartupException(string message)
        : base(message)
    {
    }

    public LeecharrStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
