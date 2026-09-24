// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Subtitles;

public interface ISubtitleConversionService
{
    string ConvertToWebVtt(string content, string format = "srt");
    string ConvertToWebVtt(byte[] rawBytes, string format = "srt");
    string ConvertSrtToWebVtt(string srtContent);
}
