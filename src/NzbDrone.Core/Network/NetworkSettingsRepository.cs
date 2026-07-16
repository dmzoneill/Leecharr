using System.Linq;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Network;

public interface INetworkSettingsRepository : IBasicRepository<NetworkSettings>
{
    NetworkSettings GetSettings();
}

public class NetworkSettingsRepository : BasicRepository<NetworkSettings>, INetworkSettingsRepository
{
    public NetworkSettingsRepository(IDatabase database)
        : base(database)
    {
    }

    public NetworkSettings GetSettings()
    {
        return All().FirstOrDefault() ?? new NetworkSettings();
    }
}
