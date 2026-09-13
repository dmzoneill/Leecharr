#nullable enable
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Automation;

public interface IAutomationScriptRepository : IBasicRepository<AutomationScript>
{
    List<AutomationScript> GetByTrigger(AutomationTrigger trigger);

    List<AutomationScript> GetEnabled();
}
