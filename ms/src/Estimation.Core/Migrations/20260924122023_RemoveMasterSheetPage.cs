using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMasterSheetPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 23);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AppPages",
                columns: new[] { "Id", "DisplayName", "Group", "IsAdminOnly", "Key", "ScopeMode", "SortOrder" },
                values: new object[] { 23, "Master Sheet", null, true, "MasterSheet", 0, 101 });
        }
    }
}
