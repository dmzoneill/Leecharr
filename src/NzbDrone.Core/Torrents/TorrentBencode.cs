// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Text;

namespace NzbDrone.Core.Torrents;

public static class TorrentBencode
{
    public static bool TryExtractInfoDictionaryBytes(ReadOnlySpan<byte> torrentBytes, out ReadOnlySpan<byte> infoDictionaryBytes)
    {
        infoDictionaryBytes = default;
        if (torrentBytes.IsEmpty || torrentBytes[0] != (byte)'d')
        {
            return false;
        }

        var index = 1;
        while (index < torrentBytes.Length && torrentBytes[index] != (byte)'e')
        {
            if (!TryReadBencodeString(torrentBytes, ref index, out var keyBytes))
            {
                return false;
            }

            var valueStart = index;
            if (!TrySkipBencodeValue(torrentBytes, ref index))
            {
                return false;
            }

            if (keyBytes.SequenceEqual("info"u8))
            {
                infoDictionaryBytes = torrentBytes.Slice(valueStart, index - valueStart);
                return infoDictionaryBytes.Length > 0 && infoDictionaryBytes[0] == (byte)'d';
            }
        }

        return false;
    }

    private static bool TryReadBencodeString(ReadOnlySpan<byte> data, ref int index, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (index >= data.Length || data[index] < (byte)'0' || data[index] > (byte)'9')
        {
            return false;
        }

        var lenStart = index;
        while (index < data.Length && data[index] >= (byte)'0' && data[index] <= (byte)'9')
        {
            index++;
        }

        if (index >= data.Length || data[index] != (byte)':')
        {
            return false;
        }

        var lenSpan = data.Slice(lenStart, index - lenStart);
        if (!long.TryParse(Encoding.ASCII.GetString(lenSpan), out var strLen) || strLen < 0 || strLen > int.MaxValue)
        {
            return false;
        }

        index++;
        if (index + strLen > data.Length)
        {
            return false;
        }

        value = data.Slice(index, (int)strLen);
        index += (int)strLen;
        return true;
    }

    private static bool TrySkipBencodeValue(ReadOnlySpan<byte> data, ref int index)
    {
        if (index >= data.Length)
        {
            return false;
        }

        var b = data[index];
        if (b == (byte)'d' || b == (byte)'l')
        {
            var containerType = b;
            index++;
            while (index < data.Length && data[index] != (byte)'e')
            {
                if (containerType == (byte)'d')
                {
                    if (!TryReadBencodeString(data, ref index, out _))
                    {
                        return false;
                    }
                }

                if (!TrySkipBencodeValue(data, ref index))
                {
                    return false;
                }
            }

            if (index >= data.Length || data[index] != (byte)'e')
            {
                return false;
            }

            index++;
            return true;
        }

        if (b == (byte)'i')
        {
            index++;
            while (index < data.Length && data[index] != (byte)'e')
            {
                index++;
            }

            if (index >= data.Length)
            {
                return false;
            }

            index++;
            return true;
        }

        if (b >= (byte)'0' && b <= (byte)'9')
        {
            return TryReadBencodeString(data, ref index, out _);
        }

        return false;
    }
}
