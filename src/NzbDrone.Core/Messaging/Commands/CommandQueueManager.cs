// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandQueueManager : IManageCommandQueue, IDisposable
{
    private readonly ICommandRepository repository;
    private readonly Logger logger;
    private readonly Timer cleanupTimer;

    public CommandQueueManager(ICommandRepository repository)
    {
        this.repository = repository;
        this.logger = LogManager.GetCurrentClassLogger();
        this.cleanupTimer = new Timer(
            _ =>
            {
                try
                {
                    this.CleanupOldCommands();
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error during command history cleanup");
                }
            },
            null,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(24));
    }

    public CommandModel Push<TCommand>(TCommand command, CommandTrigger trigger = CommandTrigger.Unspecified)
        where TCommand : Command
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        command.QueuedAt = DateTime.UtcNow;
        command.Trigger = trigger;

        this.logger.Trace("Publishing {0}", command.Name);

        var model = new CommandModel
        {
            Name = command.Name,
            Body = command.ToJson(),
            Status = CommandStatus.Queued,
            QueuedAt = command.QueuedAt,
            Trigger = (int)trigger,
        };

        this.repository.Insert(model);

        return model;
    }

    public CommandModel PushRaw(string name, string body, CommandTrigger trigger = CommandTrigger.Manual)
    {
        this.logger.Trace("Publishing raw command {0}", name);

        var model = new CommandModel
        {
            Name = name,
            Body = body,
            Status = CommandStatus.Queued,
            QueuedAt = DateTime.UtcNow,
            Trigger = (int)trigger,
        };

        this.repository.Insert(model);
        return model;
    }

    public IEnumerable<CommandModel> GetAll()
    {
        return this.repository.All()
            .OrderByDescending(c => c.QueuedAt)
            .Take(50);
    }

    public IEnumerable<CommandModel> GetStarted()
    {
        return this.repository.GetByStatus(CommandStatus.Running);
    }

    public IEnumerable<CommandModel> GetQueued()
    {
        return this.repository.GetByStatus(CommandStatus.Queued);
    }

    private void CleanupOldCommands()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        this.repository.DeleteOldTerminalCommands(cutoff);
        this.logger.Debug("Cleaned up terminal command records older than {0:yyyy-MM-dd}", cutoff);
    }

    public void Dispose()
    {
        this.cleanupTimer?.Dispose();
    }
}
