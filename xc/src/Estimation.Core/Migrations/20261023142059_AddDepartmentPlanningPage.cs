using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentPlanningPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AppPages",
                columns: new[] { "Id", "DisplayName", "Group", "IsAdminOnly", "Key", "ScopeMode", "SortOrder" },
                values: new object[] { 33, "Department Planning", "Planning", false, "DepartmentPlanning", 2, 5 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 33);
        }
    }
}
