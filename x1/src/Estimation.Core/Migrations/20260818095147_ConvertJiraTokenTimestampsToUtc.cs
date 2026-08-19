using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    // JiraTokens.Created/Updated were written with DateTime.Now, so existing rows hold the
    // application server's local time while every other timestamp in the database is UTC. The
    // service now writes UTC too; this rebases the rows that were already there.
    public partial class ConvertJiraTokenTimestampsToUtc : Migration
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

            return $"""
                    UPDATE JiraTokens
                    SET Created = CAST(Created AT TIME ZONE '{from}' AT TIME ZONE '{to}' AS datetime2),
                        Updated = CASE
                            WHEN Updated IS NULL THEN NULL
                            ELSE CAST(Updated AT TIME ZONE '{from}' AT TIME ZONE '{to}' AS datetime2)
                        END;
                    """;
        }
    }
}
