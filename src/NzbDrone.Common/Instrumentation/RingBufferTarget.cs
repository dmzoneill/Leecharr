// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using NLog;
using NLog.Targets;

namespace NzbDrone.Common.Instrumentation;

[Target("RingBuffer")]
public class RingBufferTarget : TargetWithLayout
{
    private readonly object @lock = new();
    private readonly LogEntryRecord[] buffer;
    private int position;
    private int count;

    public int Capacity { get; }

    public RingBufferTarget(int capacity = 2048)
    {
        this.Capacity = capacity;
        this.buffer = new LogEntryRecord[capacity];
    }

    protected override void Write(LogEventInfo logEvent)
    {
        var entry = new LogEntryRecord
        {
            Time = logEvent.TimeStamp.ToUniversalTime(),
            Level = logEvent.Level.Name,
            Logger = logEvent.LoggerName,
            Message = logEvent.FormattedMessage,
            Exception = logEvent.Exception?.ToString(),
        };

        lock (this.@lock)
        {
            this.buffer[this.position] = entry;
            this.position = (this.position + 1) % this.Capacity;

            if (this.count < this.Capacity)
            {
                this.count++;
            }
        }
    }

    public List<LogEntryRecord> GetEntries(int count, LogLevel minimumLevel)
    {
        lock (this.@lock)
        {
            var result = new List<LogEntryRecord>();

            int start;
            if (this.count < this.Capacity)
            {
                start = 0;
            }
            else
            {
                start = this.position;
            }

            for (var i = 0; i < this.count; i++)
            {
                var index = (start + i) % this.Capacity;
                var entry = this.buffer[index];

                if (entry == null)
                {
                    continue;
                }

                if (minimumLevel != null && LogLevel.FromString(entry.Level) < minimumLevel)
                {
                    continue;
                }

                result.Add(entry);
            }

            if (result.Count > count)
            {
                result = result.Skip(result.Count - count).ToList();
            }

            return result;
        }
    }

    public static RingBufferTarget Instance { get; set; }
}
