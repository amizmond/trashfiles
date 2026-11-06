using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSprintReportsPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AppPages",
                columns: new[] { "Id", "DisplayName", "Group", "IsAdminOnly", "Key", "ScopeMode", "SortOrder" },
                values: new object[] { 35, "Sprint Reports", "Team planning", false, "SprintReports", 2, 34 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 35);
        }
    }
}
