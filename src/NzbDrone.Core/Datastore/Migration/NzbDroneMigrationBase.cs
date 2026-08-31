using System;
using System.Linq;
using System.Reflection;
using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

public abstract class NzbDroneMigrationBase : FluentMigrator.Migration
{
    private static readonly Lazy<int> _latestMigration = new(() =>
        typeof(NzbDroneMigrationBase).Assembly
            .GetTypes()
            .Select(t => t.GetCustomAttribute(typeof(MigrationAttribute), false) as MigrationAttribute)
            .Where(a => a != null)
            .Select(a => (int)a.Version)
            .DefaultIfEmpty(0)
            .Max());

    public static int LatestMigration => _latestMigration.Value;
}
