using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSprintUatAndFeatureTeamSprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FixVersion",
                table: "Sprints",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UatEnd",
                table: "Sprints",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UatStart",
                table: "Sprints",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "FixVersion",
                table: "CapitalProjectSprints",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UatEnd",
                table: "CapitalProjectSprints",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UatStart",
                table: "CapitalProjectSprints",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql(@"
UPDATE Sprints SET UatStart = StartDate, UatEnd = EndDate WHERE UatStart = '0001-01-01' OR UatEnd = '0001-01-01';
UPDATE CapitalProjectSprints SET UatStart = StartDate, UatEnd = EndDate WHERE UatStart = '0001-01-01' OR UatEnd = '0001-01-01';");

            migrationBuilder.CreateTable(
                name: "FeatureTeamSprints",
                columns: table => new
                {
                    FeatureId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    SprintId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureTeamSprints", x => new { x.FeatureId, x.TeamId, x.SprintId });
                    table.ForeignKey(
                        name: "FK_FeatureTeamSprints_FeatureTeams_FeatureId_TeamId",
                        columns: x => new { x.FeatureId, x.TeamId },
                        principalTable: "FeatureTeams",
                        principalColumns: new[] { "FeatureId", "TeamId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FeatureTeamSprints_Sprints_SprintId",
                        column: x => x.SprintId,
                        principalTable: "Sprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureTeamSprints_SprintId",
                table: "FeatureTeamSprints",
                column: "SprintId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureTeamSprints");

            migrationBuilder.DropColumn(
                name: "FixVersion",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "UatEnd",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "UatStart",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "FixVersion",
                table: "CapitalProjectSprints");

            migrationBuilder.DropColumn(
                name: "UatEnd",
                table: "CapitalProjectSprints");

            migrationBuilder.DropColumn(
                name: "UatStart",
                table: "CapitalProjectSprints");
        }
    }
}
