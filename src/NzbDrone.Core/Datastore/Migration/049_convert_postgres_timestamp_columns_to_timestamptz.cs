// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(49)]
public class ConvertPostgresTimestampColumnsToTimestamptz : NzbDroneMigrationBase
{
    public override void Up()
    {
        // FluentMigrator AsDateTime() creates "timestamp without time zone". Npgsql 10 rejects
        // DateTime.UtcNow for that type. Leecharr stores UTC instants; promote columns to timestamptz.
        this.IfDatabase("Postgres", "Postgres92", "PostgreSQL")
            .Execute.Sql(
                @"
DO $migration$
DECLARE r record;
BEGIN
  FOR r IN
    SELECT table_schema, table_name, column_name
    FROM information_schema.columns
    WHERE table_schema = current_schema()
      AND udt_name = 'timestamp'
  LOOP
    EXECUTE format(
      'ALTER TABLE %I.%I ALTER COLUMN %I TYPE timestamptz USING %I AT TIME ZONE ''UTC''',
      r.table_schema,
      r.table_name,
      r.column_name,
      r.column_name);
  END LOOP;
END $migration$;");
    }

    public override void Down()
    {
    }
}
