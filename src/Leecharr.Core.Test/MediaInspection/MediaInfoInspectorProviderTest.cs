// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaInspection;

namespace Leecharr.Core.Test.MediaInspection;

[TestFixture]
public class MediaInfoInspectorProviderTest
{
    private string tempDirectory = null!;
    private string dummyMediaFile = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDirectory = Path.Combine(Path.GetTempPath(), "mediainfo_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDirectory);

        this.dummyMediaFile = Path.Combine(this.tempDirectory, "sample_video.mkv");
        File.WriteAllBytes(this.dummyMediaFile, new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }); // Matroska EBML header
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDirectory))
        {
            try
            {
                Directory.Delete(this.tempDirectory, recursive: true);
            }
            catch
            {
                // Suppress cleanup failure
            }
        }
    }

    [Test]
    public async Task InspectMediaAsync_WhenStderrExceedsPipeBuffer_DrainsConcurrentlyWithoutDeadlock()
    {
        // Script emits 128 KB to stderr (exceeding standard 64 KB Linux pipe buffer) and valid MediaInfo JSON to stdout
        var mockScript = Path.Combine(this.tempDirectory, "mock_mediainfo_large_stderr.sh");
        var scriptContent = @"#!/bin/sh
dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\000' 'E' >&2
cat << 'EOF'
{
  ""media"": {
    ""track"": [
      {
        ""@type"": ""General"",
        ""Format"": ""Matroska"",
        ""Duration"": ""120.500"",
        ""OverallBitRate"": ""10000000""
      },
      {
        ""@type"": ""Video"",
        ""Format"": ""HEVC"",
        ""Width"": ""3840"",
        ""Height"": ""2160"",
        ""FrameRate"": ""24.000""
      },
      {
        ""@type"": ""Audio"",
        ""Format"": ""E-AC-3"",
        ""Channels"": ""6"",
        ""SamplingRate"": ""48000""
      }
    ]
  }
}
EOF
exit 0
";
        File.WriteAllText(mockScript, scriptContent.Replace("\r\n", "\n"));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(mockScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var provider = new MediaInfoInspectorProvider(mockScript, TimeSpan.FromSeconds(10));

        var result = await provider.InspectMediaAsync(this.dummyMediaFile);

        result.Should().NotBeNull();
        result!.ContainerFormat.Should().Be("Matroska");
        result.VideoCodec.Should().Be("HEVC");
        result.Width.Should().Be(3840);
        result.Height.Should().Be(2160);
        result.Resolution.Should().Be("4K UHD (2160p)");
        result.AudioCodec.Should().Be("E-AC-3");
        result.AudioChannels.Should().Be("5.1");
    }

    [Test]
    public void InspectFile_WhenStderrExceedsPipeBuffer_SynchronouslyCompletesWithoutDeadlock()
    {
        var mockScript = Path.Combine(this.tempDirectory, "mock_mediainfo_sync.sh");
        var scriptContent = @"#!/bin/sh
dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\000' 'X' >&2
cat << 'EOF'
{
  ""media"": {
    ""track"": [
      {
        ""@type"": ""General"",
        ""Format"": ""Matroska""
      },
      {
        ""@type"": ""Video"",
        ""Format"": ""AVC"",
        ""Width"": ""1920"",
        ""Height"": ""1080""
      }
    ]
  }
}
EOF
exit 0
";
        File.WriteAllText(mockScript, scriptContent.Replace("\r\n", "\n"));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(mockScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var provider = new MediaInfoInspectorProvider(mockScript, TimeSpan.FromSeconds(10));

        var result = provider.InspectFile(this.dummyMediaFile);

        result.Should().NotBeNull();
        result!.ContainerFormat.Should().Be("Matroska");
        result.VideoCodec.Should().Be("AVC");
        result.Resolution.Should().Be("1080p");
    }

    [Test]
    public async Task InspectMediaAsync_WhenProcessHangs_TimesOutAndTerminatesProcess()
    {
        // Script hangs indefinitely
        var mockScript = Path.Combine(this.tempDirectory, "mock_mediainfo_hang.sh");
        var pidFile = Path.Combine(this.tempDirectory, "child_pid.txt");
        var scriptContent = $@"#!/bin/sh
echo $$ > ""{pidFile}""
sleep 300
";
        File.WriteAllText(mockScript, scriptContent.Replace("\r\n", "\n"));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(mockScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // Configure with very short timeout of 250ms
        var provider = new MediaInfoInspectorProvider(mockScript, TimeSpan.FromMilliseconds(250));

        var startTime = DateTime.UtcNow;
        var result = await provider.InspectMediaAsync(this.dummyMediaFile);
        var elapsed = DateTime.UtcNow - startTime;

        // Ensure timeout kicked in promptly rather than waiting 300 seconds
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

        // Provider gracefully returns fallback instead of throwing
        result.Should().NotBeNull();

        // Verify the child process was terminated
        if (File.Exists(pidFile))
        {
            var pidText = File.ReadAllText(pidFile).Trim();
            if (int.TryParse(pidText, out var pid))
            {
                // Give OS a moment to reap
                await Task.Delay(200);
                var isRunning = false;
                try
                {
                    var p = System.Diagnostics.Process.GetProcessById(pid);
                    isRunning = !p.HasExited;
                }
                catch
                {
                    isRunning = false;
                }

                isRunning.Should().BeFalse("hanging process should be terminated when timeout expires");
            }
        }
    }

    [Test]
    public void ParseMediaInfoJson_ParsesComplexContainerStreams()
    {
        var json = @"
{
  ""media"": {
    ""track"": [
      {
        ""@type"": ""General"",
        ""Format"": ""Matroska"",
        ""Duration"": ""7200.0"",
        ""OverallBitRate"": ""25000000""
      },
      {
        ""@type"": ""Video"",
        ""Format"": ""HEVC"",
        ""Width"": ""3840"",
        ""Height"": ""2160"",
        ""BitDepth"": ""10"",
        ""HDR_Format"": ""Dolby Vision / HDR10"",
        ""FrameRate"": ""23.976""
      },
      {
        ""@type"": ""Audio"",
        ""Format"": ""TrueHD"",
        ""Format_Commercial"": ""Dolby TrueHD with Dolby Atmos"",
        ""Channels"": ""8"",
        ""SamplingRate"": ""48000"",
        ""BitDepth"": ""24""
      },
      {
        ""@type"": ""Text"",
        ""Language"": ""en"",
        ""Title"": ""Full English SDH"",
        ""Format"": ""SubRip""
      }
    ]
  }
}";
        var info = MediaInfoInspectorProvider.ParseMediaInfoJson(json, "Movie.2160p.UHD.mkv");

        info.Should().NotBeNull();
        info!.ContainerFormat.Should().Be("Matroska");
        info.VideoCodec.Should().Be("HEVC");
        info.Width.Should().Be(3840);
        info.Height.Should().Be(2160);
        info.Resolution.Should().Be("4K UHD (2160p)");
        info.HdrFormat.Should().Contain("Dolby Vision");
        info.AudioCodec.Should().Be("TrueHD");
        info.AudioChannels.Should().Be("7.1");
        info.AudioSampleRate.Should().Be(48000);
        info.AudioBitDepth.Should().Be(24);
        info.SubtitleTracks.Should().ContainSingle().Which.Should().Contain("Full English SDH");
    }

    [Test]
    public void ParseMediaInfoJson_WhenNumericFieldsAreJsonNumbers_ParsesCorrectly()
    {
        var json = @"
{
  ""media"": {
    ""track"": [
      {
        ""@type"": ""General"",
        ""Format"": ""Matroska"",
        ""Duration"": 7200.0,
        ""OverallBitRate"": 25000000
      },
      {
        ""@type"": ""Video"",
        ""Format"": ""HEVC"",
        ""Width"": 3840,
        ""Height"": 2160,
        ""BitDepth"": 10,
        ""HDR_Format"": ""Dolby Vision / HDR10"",
        ""FrameRate"": 23.976
      },
      {
        ""@type"": ""Audio"",
        ""Format"": ""TrueHD"",
        ""Format_Commercial"": ""Dolby TrueHD with Dolby Atmos"",
        ""Channels"": 8,
        ""SamplingRate"": 48000,
        ""BitDepth"": 24
      },
      {
        ""@type"": ""Text"",
        ""Language"": ""en"",
        ""Title"": ""Full English SDH"",
        ""Format"": ""SubRip""
      }
    ]
  }
}";
        var info = MediaInfoInspectorProvider.ParseMediaInfoJson(json, "Movie.2160p.UHD.mkv");

        info.Should().NotBeNull();
        info!.ContainerFormat.Should().Be("Matroska");
        info.VideoCodec.Should().Be("HEVC");
        info.Width.Should().Be(3840);
        info.Height.Should().Be(2160);
        info.Resolution.Should().Be("4K UHD (2160p)");
        info.HdrFormat.Should().Contain("Dolby Vision");
        info.AudioCodec.Should().Be("TrueHD");
        info.AudioChannels.Should().Be("7.1");
        info.AudioSampleRate.Should().Be(48000);
        info.AudioBitDepth.Should().Be(24);
        info.DurationSeconds.Should().Be(7200.0);
        info.SubtitleTracks.Should().ContainSingle().Which.Should().Contain("Full English SDH");
    }

    [TestCase(720, 400)]
    [TestCase(720, 360)]
    [TestCase(640, 360)]
    [TestCase(640, 480)]
    public void ParseMediaInfoJson_WhenSdCroppedOrWidescreen_Derives480pResolution(int width, int height)
    {
        var json = $@"
{{
  ""media"": {{
    ""track"": [
      {{
        ""@type"": ""General"",
        ""Format"": ""Matroska"",
        ""Duration"": 3600.0
      }},
      {{
        ""@type"": ""Video"",
        ""Format"": ""AVC"",
        ""Width"": {width},
        ""Height"": {height}
      }}
    ]
  }}
}}";
        var info = MediaInfoInspectorProvider.ParseMediaInfoJson(json, "sample.mkv");
        info.Should().NotBeNull();
        info!.Resolution.Should().Be("480p");
    }
}
