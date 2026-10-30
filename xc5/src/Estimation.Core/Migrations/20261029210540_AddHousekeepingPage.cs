using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddHousekeepingPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AppPages",
                columns: new[] { "Id", "DisplayName", "Group", "IsAdminOnly", "Key", "ScopeMode", "SortOrder" },
                values: new object[] { 34, "Housekeeping", "Admin", true, "Housekeeping", 0, 97 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 34);
        }
    }
}
