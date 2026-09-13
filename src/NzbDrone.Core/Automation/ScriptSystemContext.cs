#nullable enable
using System;
using System.Text.Json;
using NLog;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Automation;

#pragma warning disable SA1300 // Element should begin with upper-case letter (DSL wrapper)
public class ScriptSystemContext
{
    private readonly IManageCommandQueue? _commandQueue;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public ScriptSystemContext(IManageCommandQueue? commandQueue = null)
    {
        _commandQueue = commandQueue;
    }

    public object? runCommand(string commandName, object? payload = null)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            throw new ArgumentException("Command name cannot be empty.", nameof(commandName));
        }

        if (_commandQueue == null)
        {
            _logger.Warn("Command queue is not available to execute command {0}", commandName);
            return null;
        }

        var jsonBody = payload != null ? JsonSerializer.Serialize(payload) : "{}";
        var result = _commandQueue.PushRaw(commandName.Trim(), jsonBody, CommandTrigger.Manual);
        _logger.Info("Queued system command '{0}' (Id={1}) from automation script", commandName, result?.Id);

        return new
        {
            id = result?.Id,
            name = result?.Name,
            status = result?.Status.ToString(),
        };
    }

    public object? executeTask(string taskName)
    {
        return runCommand(taskName);
    }
}
#pragma warning restore SA1300
