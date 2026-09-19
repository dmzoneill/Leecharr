// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(CommandModel command, CancellationToken cancellationToken = default)
    {
        if (command == null)
        {
            return;
        }

        this.logger.Trace("Executing {0}", command.Name);

        object handler = null;

        try
        {
            command.Status = CommandStatus.Running;
            command.StartedAt = DateTime.UtcNow;
            this.SafeUpdate(command);

            var commandType = FindCommandType(command.Name);
            if (commandType == null)
            {
                this.logger.Warn("No command type found for '{0}'", command.Name);
                command.Status = CommandStatus.Failed;
                command.Message = $"Unknown command: {command.Name}";
                return;
            }

            var typedCommand = DeserializeCommand(command.Body, commandType);
            var asyncHandlerType = typeof(IExecuteAsync<>).MakeGenericType(commandType);
            var syncHandlerType = typeof(IExecute<>).MakeGenericType(commandType);

            MethodInfo executeMethod = null;
            var isAsync = false;

            try
            {
                handler = this.serviceFactory.Build(asyncHandlerType);
                if (handler != null)
                {
                    executeMethod = asyncHandlerType.GetMethod("ExecuteAsync");
                    isAsync = true;
                }
            }
            catch
            {
                // Fall back to sync handler
            }

            if (handler == null)
            {
                try
                {
                    handler = this.serviceFactory.Build(syncHandlerType);
                    if (handler != null)
                    {
                        executeMethod = syncHandlerType.GetMethod("Execute");
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "No handler registered for '{0}'", command.Name);
                    command.Status = CommandStatus.Failed;
                    command.Message = $"No handler for command: {command.Name}";
                    return;
                }
            }

            if (handler == null || executeMethod == null)
            {
                this.logger.Warn("No handler registered for '{0}'", command.Name);
                command.Status = CommandStatus.Failed;
                command.Message = $"No handler for command: {command.Name}";
                return;
            }

            if (isAsync)
            {
                var parameters = executeMethod.GetParameters();
                var args = parameters.Length switch
                {
                    1 => new object[] { typedCommand },
                    2 => new object[] { typedCommand, cancellationToken },
                    _ => new object[] { typedCommand },
                };

                var task = (Task)executeMethod.Invoke(handler, args)!;
                if (task != null)
                {
                    await task.ConfigureAwait(false);
                }
            }
            else
            {
                var result = executeMethod.Invoke(handler, new[] { typedCommand });
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                }
            }

            command.Status = CommandStatus.Completed;
            this.logger.Debug("Completed {0}", command.Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            command.Status = CommandStatus.Cancelled;
            command.Message = "Command execution cancelled.";
            this.logger.Warn("Command {0} was cancelled", command.Name);
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
            if (handler is IAsyncDisposable asyncDisposable)
            {
                try
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Error disposing async command handler for {0}", command.Name);
                }
            }
            else if (handler is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Error disposing command handler for {0}", command.Name);
                }
            }

            command.EndedAt = DateTime.UtcNow;
            this.SafeUpdate(command);
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

    private void SafeUpdate(CommandModel command)
    {
        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                this.repository.Update(command);
                return;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    this.logger.Error(ex, "Failed to update command {0} (ID: {1}) status to {2} after {3} attempts", command.Name, command.Id, command.Status, maxRetries);
                }
                else
                {
                    this.logger.Warn(ex, "Transient error updating command {0} (ID: {1}) status, retrying (attempt {2}/{3})", command.Name, command.Id, attempt, maxRetries);
                    Thread.Sleep(50 * attempt);
                }
            }
        }
    }
}
