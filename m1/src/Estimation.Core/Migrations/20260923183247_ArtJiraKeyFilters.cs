using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class ArtJiraKeyFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Components",
                table: "CapitalProjectJiraKeys",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Labels",
                table: "CapitalProjectJiraKeys",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.Sql("UPDATE JiraSyncProjectSettings SET LastSyncedWatermarkUtc = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Components",
                table: "CapitalProjectJiraKeys");

            migrationBuilder.DropColumn(
                name: "Labels",
                table: "CapitalProjectJiraKeys");
        }
    }
}
