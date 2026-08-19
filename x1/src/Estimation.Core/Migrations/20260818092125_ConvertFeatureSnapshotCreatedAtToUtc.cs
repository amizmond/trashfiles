using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <summary>
    /// FeatureSnapshots.CreatedAt used to be written with <c>DateTime.Now</c>, so existing rows hold
    /// the application server's local time while every other timestamp in the database is UTC. The
    /// service now writes UTC too; this rebases the rows that were already there so the whole column
    /// means the same thing and the UI can convert it to each user's timezone.
    /// </summary>
    public partial class ConvertFeatureSnapshotCreatedAtToUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Evaluated on the application server as it migrates, which is the machine whose clock
            // produced the values — not the machine this migration was scaffolded on.
            migrationBuilder.Sql(ConvertSql(fromLocalToUtc: true));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ConvertSql(fromLocalToUtc: false));
        }

        private static string ConvertSql(bool fromLocalToUtc)
        {
            var timeZone = TimeZoneInfo.Local;

            // Nothing to rebase when the server already runs on UTC, and 'UTC' is the one zone name
            // SQL Server is guaranteed to know regardless of the host's registry.
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

            // AT TIME ZONE reads the naked datetime2 as wall-clock time in the first zone, then
            // shifts it into the second. The CAST drops the offset so the column stays datetime2.
            return fromLocalToUtc
                ? $"""
                   UPDATE FeatureSnapshots
                   SET CreatedAt = CAST(CreatedAt AT TIME ZONE '{zoneName}' AT TIME ZONE 'UTC' AS datetime2);
                   """
                : $"""
                   UPDATE FeatureSnapshots
                   SET CreatedAt = CAST(CreatedAt AT TIME ZONE 'UTC' AT TIME ZONE '{zoneName}' AS datetime2);
                   """;
        }
    }
}
