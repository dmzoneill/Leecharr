using System;

namespace NzbDrone.Core.Exceptions;

public class InvalidTorrentFileException : Exception
{
    public InvalidTorrentFileException(string message)
        : base(message)
    {
    }

    public InvalidTorrentFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
