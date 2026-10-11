// Copyright (c) FeedItOut. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Automation;

public class AutomationScheduledCommand : Command
{
}

public class AutomationScheduledTask : IExecute<AutomationScheduledCommand>, IExecuteAsync<AutomationScheduledCommand>
{
    private readonly AutomationEventService automationEventService;
    private readonly Logger logger;

    public AutomationScheduledTask(AutomationEventService automationEventService)
    {
        this.automationEventService = automationEventService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task ExecuteAsync(AutomationScheduledCommand message, CancellationToken cancellationToken = default)
    {
        this.logger.Debug("Dispatching scheduled automation scripts");
        await this.automationEventService.DispatchTrigger(AutomationTrigger.Scheduled, null).ConfigureAwait(false);
    }

    public void Execute(AutomationScheduledCommand message)
    {
        this.ExecuteAsync(message).GetAwaiter().GetResult();
    }
}
