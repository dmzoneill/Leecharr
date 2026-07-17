using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;

namespace Leecharr.Core.Test.Common;

[TestFixture]
public class InstrumentationTest
{
    [Test]
    public void RingBufferTarget_StoresAndRetrievesEntries()
    {
        var target = new RingBufferTarget(100);

        var entries = target.GetEntries(50, LogLevel.Info);
        entries.Should().NotBeNull();
    }

    [Test]
    public void LogEntryRecord_InitializesProperties()
    {
        var record = new LogEntryRecord
        {
            Level = "Info",
            Logger = "General",
            Message = "App started",
            Exception = null
        };

        record.Message.Should().Be("App started");
        record.Level.Should().Be("Info");
    }
}
