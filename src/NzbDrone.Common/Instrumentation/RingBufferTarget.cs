using System.Collections.Generic;
using System.Linq;
using NLog;
using NLog.Targets;

namespace NzbDrone.Common.Instrumentation;

[Target("RingBuffer")]
public class RingBufferTarget : TargetWithLayout
{
    private readonly object _lock = new();
    private readonly LogEntryRecord[] _buffer;
    private int _position;
    private int _count;

    public int Capacity { get; }

    public RingBufferTarget(int capacity = 2048)
    {
        Capacity = capacity;
        _buffer = new LogEntryRecord[capacity];
    }

    protected override void Write(LogEventInfo logEvent)
    {
        var entry = new LogEntryRecord
        {
            Time = logEvent.TimeStamp.ToUniversalTime(),
            Level = logEvent.Level.Name,
            Logger = logEvent.LoggerName,
            Message = logEvent.FormattedMessage,
            Exception = logEvent.Exception?.ToString()
        };

        lock (_lock)
        {
            _buffer[_position] = entry;
            _position = (_position + 1) % Capacity;

            if (_count < Capacity)
            {
                _count++;
            }
        }
    }

    public List<LogEntryRecord> GetEntries(int count, LogLevel minimumLevel)
    {
        lock (_lock)
        {
            var result = new List<LogEntryRecord>();

            int start;
            if (_count < Capacity)
            {
                start = 0;
            }
            else
            {
                start = _position;
            }

            for (var i = 0; i < _count; i++)
            {
                var index = (start + i) % Capacity;
                var entry = _buffer[index];

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
