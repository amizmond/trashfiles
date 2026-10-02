using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddRagStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RagStatus",
                table: "StrategicObjectives",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RagStatus",
                table: "PortfolioEpics",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RagStatus",
                table: "Features",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RagStatus",
                table: "BusinessOutcomes",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.Sql("UPDATE JiraSyncKeys SET LastSyncedWatermarkUtc = NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RagStatus",
                table: "StrategicObjectives");

            migrationBuilder.DropColumn(
                name: "RagStatus",
                table: "PortfolioEpics");

            migrationBuilder.DropColumn(
                name: "RagStatus",
                table: "Features");

            migrationBuilder.DropColumn(
                name: "RagStatus",
                table: "BusinessOutcomes");
        }
    }
}
