using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;

namespace Leecharr.Core.Test.Automation;

[TestFixture]
public class AutomationScheduledTaskTest
{
    [Test]
    public async Task ExecuteAsync_DispatchesScheduledTrigger()
    {
        var automationService = Substitute.For<IAutomationService>();
        automationService.GetAll().Returns(new List<AutomationScript>());
        var eventService = new AutomationEventService(automationService);
        var task = new AutomationScheduledTask(eventService);

        await task.ExecuteAsync(new AutomationScheduledCommand());

        automationService.Received(0).ExecuteScript(Arg.Any<AutomationScript>(), Arg.Any<NzbDrone.Core.Torrents.Torrent>());
    }

    [Test]
    public async Task ExecuteAsync_RunsEnabledScheduledScripts()
    {
        var script = new AutomationScript
        {
            Id = 1,
            Name = "Pruner",
            IsEnabled = true,
            Trigger = AutomationTrigger.Scheduled,
        };

        var automationService = Substitute.For<IAutomationService>();
        automationService.GetAll().Returns(new List<AutomationScript> { script });
        var eventService = new AutomationEventService(automationService);
        var task = new AutomationScheduledTask(eventService);

        await task.ExecuteAsync(new AutomationScheduledCommand());

        automationService.Received(1).ExecuteScript(script, null);
    }
}
