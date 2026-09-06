// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandExecutor : ICommandExecutor
{
    private static readonly ConcurrentDictionary<string, Type> CommandTypeCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceFactory serviceFactory;
    private readonly IBasicRepository<CommandModel> repository;
    private readonly Logger logger;

    public CommandExecutor(IServiceFactory serviceFactory, IBasicRepository<CommandModel> repository)
    {
        this.serviceFactory = serviceFactory;
        this.repository = repository;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(CommandModel command)
    {
        if (command == null)
        {
            return;
        }

        this.logger.Trace("Executing {0}", command.Name);

        try
        {
            command.Status = CommandStatus.Running;
            command.StartedAt = DateTime.UtcNow;
            this.repository.Update(command);

            var commandType = FindCommandType(command.Name);
            if (commandType == null)
            {
                this.logger.Warn("No command type found for '{0}'", command.Name);
                command.Status = CommandStatus.Failed;
                command.Message = $"Unknown command: {command.Name}";
                return;
            }

            var typedCommand = DeserializeCommand(command.Body, commandType);
            var handlerType = typeof(IExecute<>).MakeGenericType(commandType);

            object handler;
            try
            {
                handler = this.serviceFactory.Build(handlerType);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "No handler registered for '{0}'", command.Name);
                command.Status = CommandStatus.Failed;
                command.Message = $"No handler for command: {command.Name}";
                return;
            }

            var executeMethod = handlerType.GetMethod("Execute");
            executeMethod!.Invoke(handler, new[] { typedCommand });

            command.Status = CommandStatus.Completed;
            this.logger.Debug("Completed {0}", command.Name);
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            command.Status = CommandStatus.Failed;
            command.Message = inner.Message;
            this.logger.Error(inner, "Error executing {0}", command.Name);
        }
        finally
        {
            command.EndedAt = DateTime.UtcNow;
            this.repository.Update(command);
        }
    }

    private static Type FindCommandType(string name)
    {
        return CommandTypeCache.GetOrAdd(name, static n =>
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch
                    {
                        return Array.Empty<Type>();
                    }
                })
                .FirstOrDefault(t =>
                    (t.Name.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                     t.Name.Equals(n + "Command", StringComparison.OrdinalIgnoreCase)) &&
                    t.IsClass &&
                    !t.IsAbstract &&
                    typeof(Command).IsAssignableFrom(t)));
    }

    private static Command DeserializeCommand(string body, Type commandType)
    {
        if (string.IsNullOrEmpty(body))
        {
            return (Command)Activator.CreateInstance(commandType)!;
        }

        return (Command)JsonSerializer.Deserialize(body, commandType, STJson.GetSerializerSettings());
    }
}
