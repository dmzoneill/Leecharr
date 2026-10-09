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
        return this.ExecuteWithRetry(connection =>
            connection.Query<AutomationScript>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Trigger\" = @Trigger AND \"IsEnabled\" = @IsEnabled",
                new { Trigger = (int)trigger, IsEnabled = true })
            .ToList());
    }

    public List<AutomationScript> GetEnabled()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<AutomationScript>(
                $"SELECT * FROM \"{this.table}\" WHERE \"IsEnabled\" = @IsEnabled",
                new { IsEnabled = true })
            .ToList());
    }
}
