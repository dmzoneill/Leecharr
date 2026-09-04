// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaInspection;

namespace Leecharr.Core.Test.MediaInspection;

[TestFixture]
public class FFprobeInspectorProviderTest
{
    private string tempDirectory = null!;
    private string dummyMediaFile = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDirectory = Path.Combine(Path.GetTempPath(), "ffprobe_test_" + Guid.NewGuid().ToString("N"));
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
        // Script emits 128 KB to stderr (exceeding standard 64 KB Linux pipe buffer) and valid ffprobe JSON to stdout
        var mockScript = Path.Combine(this.tempDirectory, "mock_ffprobe_large_stderr.sh");
        var scriptContent = @"#!/bin/sh
dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\000' 'E' >&2
cat << 'EOF'
{
  ""format"": {
    ""format_name"": ""matroska,webm"",
    ""duration"": ""120.500""
  },
  ""streams"": [
    {
      ""codec_type"": ""video"",
      ""codec_name"": ""hevc"",
      ""width"": 3840,
      ""height"": 2160
    },
    {
      ""codec_type"": ""audio"",
      ""codec_name"": ""eac3"",
      ""channels"": 6
    }
  ]
}
EOF
exit 0
";
        File.WriteAllText(mockScript, scriptContent.Replace("\r\n", "\n"));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(mockScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var provider = new FFprobeInspectorProvider(mockScript, TimeSpan.FromSeconds(10));

        var result = await provider.InspectMediaAsync(this.dummyMediaFile);

        result.Should().NotBeNull();
        result!.ContainerFormat.Should().Be("Matroska (MKV)");
        result.VideoCodec.Should().Be("HEVC / H.265");
        result.Width.Should().Be(3840);
        result.Height.Should().Be(2160);
        result.Resolution.Should().Be("4K UHD (2160p)");
        result.AudioCodec.Should().Be("E-AC3 / DD+");
        result.AudioChannels.Should().Be("5.1");
    }

    [Test]
    public void InspectFile_WhenStderrExceedsPipeBuffer_SynchronouslyCompletesWithoutDeadlock()
    {
        var mockScript = Path.Combine(this.tempDirectory, "mock_ffprobe_sync.sh");
        var scriptContent = @"#!/bin/sh
dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\000' 'X' >&2
cat << 'EOF'
{
  ""format"": {
    ""format_name"": ""matroska,webm""
  },
  ""streams"": [
    {
      ""codec_type"": ""video"",
      ""codec_name"": ""h264"",
      ""width"": 1920,
      ""height"": 1080
    }
  ]
}
EOF
exit 0
";
        File.WriteAllText(mockScript, scriptContent.Replace("\r\n", "\n"));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(mockScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var provider = new FFprobeInspectorProvider(mockScript, TimeSpan.FromSeconds(10));

        var result = provider.InspectFile(this.dummyMediaFile);

        result.Should().NotBeNull();
        result!.ContainerFormat.Should().Be("Matroska (MKV)");
        result.VideoCodec.Should().Be("AVC / H.264");
        result.Resolution.Should().Be("1080p");
    }

    [Test]
    public async Task InspectMediaAsync_WhenProcessHangs_TimesOutAndTerminatesProcess()
    {
        // Script hangs indefinitely
        var mockScript = Path.Combine(this.tempDirectory, "mock_ffprobe_hang.sh");
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
        var provider = new FFprobeInspectorProvider(mockScript, TimeSpan.FromMilliseconds(250));

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
                    var p = Process.GetProcessById(pid);
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
    public async Task InspectMediaAsync_WhenCancellationTokenTriggered_TerminatesProcessPromptly()
    {
        var mockScript = Path.Combine(this.tempDirectory, "mock_ffprobe_cancel.sh");
        var pidFile = Path.Combine(this.tempDirectory, "cancel_pid.txt");
        var scriptContent = $@"#!/bin/sh
echo $$ > ""{pidFile}""
sleep 300
";
        File.WriteAllText(mockScript, scriptContent.Replace("\r\n", "\n"));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(mockScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var provider = new FFprobeInspectorProvider(mockScript, TimeSpan.FromSeconds(60));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(250));

        var startTime = DateTime.UtcNow;
        var result = await provider.InspectMediaAsync(this.dummyMediaFile, cts.Token);
        var elapsed = DateTime.UtcNow - startTime;

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        if (File.Exists(pidFile))
        {
            var pidText = File.ReadAllText(pidFile).Trim();
            if (int.TryParse(pidText, out var pid))
            {
                await Task.Delay(200);
                var isRunning = false;
                try
                {
                    var p = Process.GetProcessById(pid);
                    isRunning = !p.HasExited;
                }
                catch
                {
                    isRunning = false;
                }

                isRunning.Should().BeFalse("process should be terminated when cancellation token triggers");
            }
        }
    }

    [Test]
    public void ParseFFprobeJson_ParsesComplexContainerStreams()
    {
        var json = @"
{
  ""format"": {
    ""format_name"": ""matroska,webm"",
    ""duration"": ""7200.000000""
  },
  ""streams"": [
    {
      ""codec_type"": ""video"",
      ""codec_name"": ""hevc"",
      ""width"": 3840,
      ""height"": 2160,
      ""color_transfer"": ""smpte2084"",
      ""side_data_list"": [
        {
          ""side_data_type"": ""DOVI configuration record""
        }
      ]
    },
    {
      ""codec_type"": ""audio"",
      ""codec_name"": ""truehd"",
      ""channels"": 8
    }
  ]
}";
        var info = FFprobeInspectorProvider.ParseFFprobeJson(json, "Movie.2160p.UHD.mkv");

        info.Should().NotBeNull();
        info!.ContainerFormat.Should().Be("Matroska (MKV)");
        info.VideoCodec.Should().Be("HEVC / H.265");
        info.Width.Should().Be(3840);
        info.Height.Should().Be(2160);
        info.Resolution.Should().Be("4K UHD (2160p)");
        info.HdrFormat.Should().Be("Dolby Vision");
        info.AudioCodec.Should().Be("Dolby TrueHD");
        info.AudioChannels.Should().Be("7.1");
        info.DurationSeconds.Should().Be(7200.0);
    }
}
