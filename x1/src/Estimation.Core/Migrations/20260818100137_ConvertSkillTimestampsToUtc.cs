using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    // Skills.Created/Updated were written with DateTime.Now, so existing rows hold the application
    // server's local time while every other timestamp in the database is UTC. The services now write
    // UTC too; this rebases the rows that were already there. With this the column is the last one
    // that was storing local time, so the database is uniformly UTC afterwards.
    public partial class ConvertSkillTimestampsToUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ConvertSql(fromLocalToUtc: true));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ConvertSql(fromLocalToUtc: false));
        }

        private static string ConvertSql(bool fromLocalToUtc)
        {
            // Evaluated on the application server as it migrates, which is the machine whose clock
            // produced the values — not the machine this migration was scaffolded on.
            var timeZone = TimeZoneInfo.Local;

            if (timeZone.BaseUtcOffset == TimeSpan.Zero && !timeZone.SupportsDaylightSavingTime)
            {
                return "-- The application server runs on UTC; the stored values need no conversion.";
            }

            // SQL Server names zones the Windows way, so translate when the host reports an IANA id.
            if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone.Id, out var windowsId))
            {
                windowsId = timeZone.Id;
            }

            var zoneName = windowsId.Replace("'", "''");
            var from = fromLocalToUtc ? zoneName : "UTC";
            var to = fromLocalToUtc ? "UTC" : zoneName;

            // Both columns are nullable, so each is guarded rather than rewritten unconditionally.
            return $"""
                    UPDATE Skills
                    SET Created = CASE
                            WHEN Created IS NULL THEN NULL
                            ELSE CAST(Created AT TIME ZONE '{from}' AT TIME ZONE '{to}' AS datetime2)
                        END,
                        Updated = CASE
                            WHEN Updated IS NULL THEN NULL
                            ELSE CAST(Updated AT TIME ZONE '{from}' AT TIME ZONE '{to}' AS datetime2)
                        END;
                    """;
        }
    }
}
