using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddJiraComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Components",
                table: "StrategicObjectives",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Components",
                table: "PortfolioEpics",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Components",
                table: "Features",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Components",
                table: "BusinessOutcomes",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Components",
                table: "StrategicObjectives");

            migrationBuilder.DropColumn(
                name: "Components",
                table: "PortfolioEpics");

            migrationBuilder.DropColumn(
                name: "Components",
                table: "Features");

            migrationBuilder.DropColumn(
                name: "Components",
                table: "BusinessOutcomes");
        }
    }
}
