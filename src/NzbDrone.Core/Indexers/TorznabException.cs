// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.Indexers;

public class TorznabException : Exception
{
    public TorznabException()
    {
    }

    public TorznabException(string message)
        : base(message)
    {
    }

    public TorznabException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TorznabException(int? code, string description, Exception innerException = null)
        : base(FormatMessage(code, description), innerException)
    {
        this.Code = code;
        this.ErrorCode = code?.ToString();
        this.Description = description;
    }

    public TorznabException(string errorCode, string description, Exception innerException = null)
        : base(FormatMessage(errorCode, description), innerException)
    {
        this.ErrorCode = errorCode;
        if (int.TryParse(errorCode, out var parsedCode))
        {
            this.Code = parsedCode;
        }

        this.Description = description;
    }

    public int? Code { get; }

    public string ErrorCode { get; }

    public string Description { get; }

    private static string FormatMessage(int? code, string description)
    {
        return FormatMessage(code?.ToString(), description);
    }

    private static string FormatMessage(string code, string description)
    {
        if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(description))
        {
            return $"Torznab error ({code}): {description}";
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            return $"Torznab error ({code})";
        }

        return !string.IsNullOrWhiteSpace(description) ? description : "Torznab request failed";
    }
}
