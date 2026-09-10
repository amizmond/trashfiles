using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddArtSprintsAndTeamArtSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArtSharedTeam",
                table: "Teams",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "JiraName",
                table: "Teams",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE t
SET t.IsArtSharedTeam = 1
FROM Teams t
WHERE (SELECT COUNT(*) FROM CapitalProjectTeams cpt WHERE cpt.TeamId = t.Id) > 1;");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Sprints",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(70)",
                oldMaxLength: 70);

            migrationBuilder.AddColumn<int>(
                name: "SourceArtSprintId",
                table: "Sprints",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CapitalProjectSprints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CapitalProjectId = table.Column<int>(type: "int", nullable: false),
                    PiId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsIpSprint = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapitalProjectSprints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapitalProjectSprints_CapitalProjects_CapitalProjectId",
                        column: x => x.CapitalProjectId,
                        principalTable: "CapitalProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CapitalProjectSprints_Pis_PiId",
                        column: x => x.PiId,
                        principalTable: "Pis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_SourceArtSprintId",
                table: "Sprints",
                column: "SourceArtSprintId");

            migrationBuilder.CreateIndex(
                name: "IX_CapitalProjectSprints_CapitalProjectId_Name",
                table: "CapitalProjectSprints",
                columns: new[] { "CapitalProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapitalProjectSprints_CapitalProjectId_StartDate_EndDate",
                table: "CapitalProjectSprints",
                columns: new[] { "CapitalProjectId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CapitalProjectSprints_PiId",
                table: "CapitalProjectSprints",
                column: "PiId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sprints_CapitalProjectSprints_SourceArtSprintId",
                table: "Sprints",
                column: "SourceArtSprintId",
                principalTable: "CapitalProjectSprints",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sprints_CapitalProjectSprints_SourceArtSprintId",
                table: "Sprints");

            migrationBuilder.DropTable(
                name: "CapitalProjectSprints");

            migrationBuilder.DropIndex(
                name: "IX_Sprints_SourceArtSprintId",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "IsArtSharedTeam",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "JiraName",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "SourceArtSprintId",
                table: "Sprints");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Sprints",
                type: "nvarchar(70)",
                maxLength: 70,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);
        }
    }
}
