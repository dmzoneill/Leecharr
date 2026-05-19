using System.Text.Json.Serialization;
using NzbDrone.Core.Datastore;

namespace NzbDrone.SignalR;

public class SignalRMessage
{
    public object Body { get; set; }
    public string Name { get; set; }

    [JsonIgnore]
    public ModelAction Action { get; set; }
}
