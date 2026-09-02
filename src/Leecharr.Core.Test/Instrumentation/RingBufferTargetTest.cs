// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;

namespace Leecharr.Core.Test.Instrumentation;

[TestFixture]
public class RingBufferTargetTest
{
    private class TestableRingBufferTarget : RingBufferTarget
    {
        public TestableRingBufferTarget(int capacity = 2048)
            : base(capacity)
        {
        }

        public void WriteLog(LogLevel level, string logger, string message, Exception exception = null)
        {
            var logEvent = new LogEventInfo(level, logger, message)
            {
                Exception = exception,
                TimeStamp = DateTime.UtcNow,
            };

            this.Write(logEvent);
        }
    }

    [Test]
    public void Capacity_IsSetFromConstructor()
    {
        var target = new TestableRingBufferTarget(128);

        target.Capacity.Should().Be(128);
    }

    [Test]
    public void Write_StoresLogEntryWithAccurateProperties()
    {
        var target = new TestableRingBufferTarget(10);
        var ex = new InvalidOperationException("Database connection timeout");

        target.WriteLog(LogLevel.Error, "TorrentService", "Failed to start torrent", ex);

        var entries = target.GetEntries(10, LogLevel.Trace);
        entries.Should().HaveCount(1);

        var entry = entries[0];
        entry.Level.Should().Be("Error");
        entry.Logger.Should().Be("TorrentService");
        entry.Message.Should().Be("Failed to start torrent");
        entry.Exception.Should().Contain("Database connection timeout");
        entry.Time.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Test]
    public void WrapAround_WhenCapacityExceeded_RetainsMostRecentEntriesInChronologicalOrder()
    {
        const int capacity = 5;
        var target = new TestableRingBufferTarget(capacity);

        for (var i = 0; i < 10; i++)
        {
            target.WriteLog(LogLevel.Info, "Service", $"Message #{i}");
        }

        var entries = target.GetEntries(10, LogLevel.Trace);

        entries.Should().HaveCount(capacity);
        entries.Select(e => e.Message).Should().Equal(
            "Message #5",
            "Message #6",
            "Message #7",
            "Message #8",
            "Message #9");
    }

    [Test]
    public void ConcurrentWrites_WithWrapAround_HandlesParallelThreadsSafely()
    {
        const int capacity = 100;
        const int threadCount = 20;
        const int writesPerThread = 50;

        var target = new TestableRingBufferTarget(capacity);

        Parallel.For(0, threadCount, threadIndex =>
        {
            for (var i = 0; i < writesPerThread; i++)
            {
                target.WriteLog(LogLevel.Info, $"Worker-{threadIndex}", $"Thread {threadIndex} Log {i}");
            }
        });

        var entries = target.GetEntries(capacity, LogLevel.Trace);

        entries.Should().HaveCount(capacity);
        entries.Should().NotContainNulls();
        entries.Should().OnlyContain(e => !string.IsNullOrEmpty(e.Message) && !string.IsNullOrEmpty(e.Logger));
    }

    [Test]
    public void LogLevelFiltering_ReturnsOnlyEntriesMeetingOrExceedingThreshold()
    {
        var target = new TestableRingBufferTarget(50);

        target.WriteLog(LogLevel.Trace, "Test", "Trace message");
        target.WriteLog(LogLevel.Debug, "Test", "Debug message");
        target.WriteLog(LogLevel.Info, "Test", "Info message");
        target.WriteLog(LogLevel.Warn, "Test", "Warn message");
        target.WriteLog(LogLevel.Error, "Test", "Error message");
        target.WriteLog(LogLevel.Fatal, "Test", "Fatal message");

        target.GetEntries(50, LogLevel.Trace).Should().HaveCount(6);
        target.GetEntries(50, LogLevel.Debug).Should().HaveCount(5);
        target.GetEntries(50, LogLevel.Info).Should().HaveCount(4);
        target.GetEntries(50, LogLevel.Warn).Should().HaveCount(3);
        target.GetEntries(50, LogLevel.Error).Should().HaveCount(2);
        target.GetEntries(50, LogLevel.Fatal).Should().HaveCount(1);
    }

    [Test]
    public void LogLevelFiltering_WhenNullProvided_ReturnsAllLogLevels()
    {
        var target = new TestableRingBufferTarget(10);

        target.WriteLog(LogLevel.Trace, "Test", "T");
        target.WriteLog(LogLevel.Fatal, "Test", "F");

        var entries = target.GetEntries(10, null!);

        entries.Should().HaveCount(2);
    }

    [Test]
    public void GetEntries_WhenCountIsLessThanAvailable_ReturnsMostRecentEntries()
    {
        var target = new TestableRingBufferTarget(10);

        for (var i = 0; i < 6; i++)
        {
            target.WriteLog(LogLevel.Info, "Test", $"Item {i}");
        }

        var entries = target.GetEntries(3, LogLevel.Info);

        entries.Should().HaveCount(3);
        entries.Select(e => e.Message).Should().Equal("Item 3", "Item 4", "Item 5");
    }

    [Test]
    public void GetEntries_WhenCountIsZero_ReturnsEmptyList()
    {
        var target = new TestableRingBufferTarget(10);
        target.WriteLog(LogLevel.Info, "Test", "Log 1");

        var entries = target.GetEntries(0, LogLevel.Info);

        entries.Should().BeEmpty();
    }

    [Test]
    public void Instance_StaticPropertyCanBeAssignedAndRetrieved()
    {
        var previousInstance = RingBufferTarget.Instance;
        try
        {
            var customTarget = new RingBufferTarget(500);
            RingBufferTarget.Instance = customTarget;

            RingBufferTarget.Instance.Should().BeSameAs(customTarget);
        }
        finally
        {
            RingBufferTarget.Instance = previousInstance;
        }
    }
}
