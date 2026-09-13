#nullable enable
using System.Collections.Generic;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Automation;

public interface IAutomationService
{
    List<AutomationScript> GetAll();

    AutomationScript Get(int id);

    AutomationScript Add(AutomationScript script);

    AutomationScript Update(AutomationScript script);

    void Delete(int id);

    AutomationExecutionResult ExecuteScript(int scriptId, int? torrentId = null, Dictionary<string, object>? customInputs = null);

    AutomationExecutionResult ExecuteScript(AutomationScript script, Torrent? torrent = null, Dictionary<string, object>? customInputs = null);

    AutomationExecutionResult TestScript(AutomationScript script, int? torrentId = null, Dictionary<string, object>? customInputs = null);
}
