// Copyright (c) FeedItOut. All rights reserved.

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

        // 2. Check for UTF-16 without BOM (before UTF-8: some UTF-16 byte patterns validate as UTF-8)
        if (TryDetectUtf16WithoutBom(bytes, out var utf16Encoding))
        {
            return utf16Encoding;
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
        var plausiblePairs = 0;
        var pairs = sampleLength / 2;

        for (var i = 0; i < sampleLength; i += 2)
        {
            if (bytes[i + 1] == 0x00)
            {
                zeroOddBytes++;
            }

            if (IsPlausibleUtf16LeCodeUnit(bytes[i], bytes[i + 1]))
            {
                plausiblePairs++;
            }
        }

        if (plausiblePairs <= pairs * 0.7)
        {
            return false;
        }

        // ASCII / Latin-1 BMP in UTF-16 LE: high byte is usually zero.
        if (zeroOddBytes > pairs * 0.7)
        {
            return true;
        }

        // Cyrillic, CJK, and other BMP scripts: few zero bytes but still UTF-16 LE pairs.
        return zeroOddBytes < pairs * 0.15;
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
        var plausiblePairs = 0;
        var pairs = sampleLength / 2;

        for (var i = 0; i < sampleLength; i += 2)
        {
            if (bytes[i] == 0x00)
            {
                zeroEvenBytes++;
            }

            if (IsPlausibleUtf16BeCodeUnit(bytes[i], bytes[i + 1]))
            {
                plausiblePairs++;
            }
        }

        if (plausiblePairs <= pairs * 0.7)
        {
            return false;
        }

        if (zeroEvenBytes > pairs * 0.7)
        {
            return true;
        }

        return zeroEvenBytes < pairs * 0.15;
    }

    private static bool IsPlausibleUtf16LeCodeUnit(byte lowByte, byte highByte)
    {
        if (highByte == 0x00)
        {
            return lowByte == 0x09 || lowByte == 0x0A || lowByte == 0x0D || lowByte >= 0x20;
        }

        var codeUnit = (char)(lowByte | (highByte << 8));
        return IsPlausibleBmpCodeUnit(codeUnit);
    }

    private static bool IsPlausibleUtf16BeCodeUnit(byte highByte, byte lowByte)
    {
        if (highByte == 0x00)
        {
            return lowByte == 0x09 || lowByte == 0x0A || lowByte == 0x0D || lowByte >= 0x20;
        }

        var codeUnit = (char)((highByte << 8) | lowByte);
        return IsPlausibleBmpCodeUnit(codeUnit);
    }

    private static bool IsPlausibleBmpCodeUnit(char codeUnit)
    {
        if (char.IsSurrogate(codeUnit))
        {
            return false;
        }

        if (codeUnit == '\t' || codeUnit == '\r' || codeUnit == '\n')
        {
            return true;
        }

        if (codeUnit < 0x20)
        {
            return false;
        }

        return true;
    }

    private static bool TryDetectUtf16WithoutBom(byte[] bytes, out Encoding encoding)
    {
        encoding = Encoding.Unicode;
        var sampleLength = GetEvenSampleLength(bytes);
        if (sampleLength < 4)
        {
            return false;
        }

        var bestScore = 0d;
        var bestEncoding = Encoding.Unicode;
        var hasCandidate = false;

        if (IsUtf16LeWithoutBom(bytes))
        {
            var score = SubtitleTextScore(Encoding.Unicode.GetString(bytes, 0, sampleLength));
            if (score > bestScore)
            {
                bestScore = score;
                bestEncoding = Encoding.Unicode;
                hasCandidate = true;
            }
        }

        if (IsUtf16BeWithoutBom(bytes))
        {
            var score = SubtitleTextScore(Encoding.BigEndianUnicode.GetString(bytes, 0, sampleLength));
            if (score > bestScore)
            {
                bestScore = score;
                bestEncoding = Encoding.BigEndianUnicode;
                hasCandidate = true;
            }
        }

        if (!hasCandidate)
        {
            return false;
        }

        if (IsValidUtf8(bytes))
        {
            var utf8Score = SubtitleTextScore(Encoding.UTF8.GetString(bytes, 0, sampleLength));
            if (utf8Score >= bestScore)
            {
                return false;
            }
        }

        encoding = bestEncoding;
        return true;
    }

    private static int GetSampleLength(byte[] bytes)
    {
        return Math.Min(bytes.Length, 512);
    }

    private static int GetEvenSampleLength(byte[] bytes)
    {
        var sampleLength = GetSampleLength(bytes);
        sampleLength -= sampleLength % 2;
        return sampleLength;
    }

    private static double SubtitleTextScore(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var score = 0;
        foreach (var ch in text)
        {
            if (ch == '\uFFFD')
            {
                score -= 2;
            }
            else if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            {
                score += 2;
            }
            else if (ch is ':' or ',' or '-' or '>' or '.' or '[' or ']')
            {
                score += 2;
            }
            else if (ch < 0x20 && ch != '\r' && ch != '\n' && ch != '\t')
            {
                score -= 1;
            }
            else
            {
                score += 1;
            }
        }

        return score / (double)text.Length;
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
