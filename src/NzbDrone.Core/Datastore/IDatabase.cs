using System;
using System.Data;

namespace NzbDrone.Core.Datastore;

public interface IDatabase
{
    IDbConnection OpenConnection();
    DatabaseType DatabaseType { get; }
    Version Version { get; }
}
