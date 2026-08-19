using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenameNavRisksAndSolutionTrains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 5,
                column: "DisplayName",
                value: "Agile Release Trains");

            migrationBuilder.UpdateData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 26,
                column: "DisplayName",
                value: "Risk Register");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 5,
                column: "DisplayName",
                value: "Solution Trains");

            migrationBuilder.UpdateData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 26,
                column: "DisplayName",
                value: "Risks");
        }
    }
}
