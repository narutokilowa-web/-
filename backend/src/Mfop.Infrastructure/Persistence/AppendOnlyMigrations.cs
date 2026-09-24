using Microsoft.EntityFrameworkCore.Migrations;

namespace Mfop.Infrastructure.Persistence;

public static class AppendOnlyMigrations
{
    /// <summary>
    /// Adds triggers that reject UPDATE, DELETE and TRUNCATE on an append-only table. This is a guard against bugs
    /// and accidents, not against a privileged DBA; tampering is detected by tools/audit-verify (ADR-0007).
    /// The migrator also withholds UPDATE/DELETE privileges from mfop_app on these tables.
    /// </summary>
    public static void AppendOnly(this MigrationBuilder migrationBuilder, string schema, string table)
    {
        migrationBuilder.Sql($"""
            CREATE OR REPLACE FUNCTION {schema}.reject_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              RAISE EXCEPTION '{schema}.% is append-only: % rejected', TG_TABLE_NAME, TG_OP;
            END $$;
            CREATE TRIGGER {table}_no_update_delete BEFORE UPDATE OR DELETE ON {schema}.{table}
              FOR EACH ROW EXECUTE FUNCTION {schema}.reject_mutation();
            CREATE TRIGGER {table}_no_truncate BEFORE TRUNCATE ON {schema}.{table}
              FOR EACH STATEMENT EXECUTE FUNCTION {schema}.reject_mutation();
            """);
    }
}
