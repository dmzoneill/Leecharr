namespace NzbDrone.Core.Datastore;

public abstract class ModelBase
{
    public int Id { get; set; }
}

public enum ModelAction
{
    Unknown = 0,
    Created = 1,
    Updated = 2,
    Deleted = 3,
    Sync = 4
}
