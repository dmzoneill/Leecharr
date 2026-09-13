#nullable enable
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Automation;

public class AutomationScriptRepository : BasicRepository<AutomationScript>, IAutomationScriptRepository
{
    public AutomationScriptRepository(IDatabase database, IEventAggregator eventAggregator)
        : base(database, eventAggregator)
    {
    }

    public List<AutomationScript> GetByTrigger(AutomationTrigger trigger)
    {
        using var connection = database.OpenConnection();
        return connection.Query<AutomationScript>(
            $"SELECT * FROM \"{table}\" WHERE \"Trigger\" = @Trigger AND \"IsEnabled\" = 1",
            new { Trigger = (int)trigger }).ToList();
    }

    public List<AutomationScript> GetEnabled()
    {
        using var connection = database.OpenConnection();
        return connection.Query<AutomationScript>(
            $"SELECT * FROM \"{table}\" WHERE \"IsEnabled\" = 1").ToList();
    }
}
