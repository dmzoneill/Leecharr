// Copyright (c) PlaceholderCompany. All rights reserved.

using System.IO;
using System.Text;

namespace NzbDrone.Core.Subtitles;

public interface ISubtitleEncodingDetector
{
    Encoding DetectEncoding(byte[] bytes);
    string DecodeToUtf8(byte[] bytes);
    string DecodeToUtf8(Stream stream);
}
