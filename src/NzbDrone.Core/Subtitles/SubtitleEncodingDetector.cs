// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Text;
using NLog;

namespace NzbDrone.Core.Subtitles;

public class SubtitleEncodingDetector : ISubtitleEncodingDetector
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    static SubtitleEncodingDetector()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch (Exception ex)
        {
            Logger.Trace(ex, "CodePagesEncodingProvider already registered or unavailable");
        }
    }

    public Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return Encoding.UTF8;
        }

        // 1. Check BOMs
        if (bytes.Length >= 4)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            {
                return Encoding.UTF32;
            }

            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            {
                return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            }
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode; // UTF-16 LE
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode; // UTF-16 BE
            }
        }

        // 2. Check for UTF-16 without BOM
        if (IsUtf16LeWithoutBom(bytes))
        {
            return Encoding.Unicode;
        }

        if (IsUtf16BeWithoutBom(bytes))
        {
            return Encoding.BigEndianUnicode;
        }

        // 3. Strict UTF-8 validation
        if (IsValidUtf8(bytes))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        }

        // 4. Fallback: Windows-1252 / ISO-8859-1 heuristics
        try
        {
            return Encoding.GetEncoding(1252);
        }
        catch (Exception ex)
        {
            Logger.Trace(ex, "Code page 1252 unavailable; falling back to Latin1");
            return Encoding.Latin1; // ISO-8859-1
        }
    }

    public string DecodeToUtf8(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }

        var encoding = DetectEncoding(bytes);
        var text = encoding.GetString(bytes);

        // Strip any leading BOM character
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text.Substring(1);
        }

        return text;
    }

    public string DecodeToUtf8(Stream stream)
    {
        if (stream == null)
        {
            return string.Empty;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return DecodeToUtf8(ms.ToArray());
    }

    private static bool IsUtf16LeWithoutBom(byte[] bytes)
    {
        if (bytes.Length < 4)
        {
            return false;
        }

        var sampleLength = Math.Min(bytes.Length, 512);
        sampleLength -= sampleLength % 2;

        var zeroOddBytes = 0;
        var asciiEvenBytes = 0;
        var pairs = sampleLength / 2;

        for (var i = 0; i < sampleLength; i += 2)
        {
            if (bytes[i + 1] == 0x00)
            {
                zeroOddBytes++;
            }

            if ((bytes[i] >= 0x20 && bytes[i] <= 0x7E) || bytes[i] == 0x0D || bytes[i] == 0x0A || bytes[i] == 0x09)
            {
                asciiEvenBytes++;
            }
        }

        return zeroOddBytes > pairs * 0.7 && asciiEvenBytes > pairs * 0.7;
    }

    private static bool IsUtf16BeWithoutBom(byte[] bytes)
    {
        if (bytes.Length < 4)
        {
            return false;
        }

        var sampleLength = Math.Min(bytes.Length, 512);
        sampleLength -= sampleLength % 2;

        var zeroEvenBytes = 0;
        var asciiOddBytes = 0;
        var pairs = sampleLength / 2;

        for (var i = 0; i < sampleLength; i += 2)
        {
            if (bytes[i] == 0x00)
            {
                zeroEvenBytes++;
            }

            if ((bytes[i + 1] >= 0x20 && bytes[i + 1] <= 0x7E) || bytes[i + 1] == 0x0D || bytes[i + 1] == 0x0A || bytes[i + 1] == 0x09)
            {
                asciiOddBytes++;
            }
        }

        return zeroEvenBytes > pairs * 0.7 && asciiOddBytes > pairs * 0.7;
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        var i = 0;
        var length = bytes.Length;

        while (i < length)
        {
            var b = bytes[i++];

            if (b <= 0x7F)
            {
                continue;
            }

            if (b >= 0xC2 && b <= 0xDF)
            {
                if (i >= length || (bytes[i++] & 0xC0) != 0x80)
                {
                    return false;
                }
            }
            else if (b >= 0xE0 && b <= 0xEF)
            {
                if (i + 1 >= length)
                {
                    return false;
                }

                var c1 = bytes[i++];
                var c2 = bytes[i++];

                if ((c1 & 0xC0) != 0x80 || (c2 & 0xC0) != 0x80)
                {
                    return false;
                }

                if (b == 0xE0 && c1 < 0xA0)
                {
                    return false;
                }

                if (b == 0xED && c1 >= 0xA0)
                {
                    return false;
                }
            }
            else if (b >= 0xF0 && b <= 0xF4)
            {
                if (i + 2 >= length)
                {
                    return false;
                }

                var c1 = bytes[i++];
                var c2 = bytes[i++];
                var c3 = bytes[i++];

                if ((c1 & 0xC0) != 0x80 || (c2 & 0xC0) != 0x80 || (c3 & 0xC0) != 0x80)
                {
                    return false;
                }

                if (b == 0xF0 && c1 < 0x90)
                {
                    return false;
                }

                if (b == 0xF4 && c1 > 0x8F)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        return true;
    }
}
