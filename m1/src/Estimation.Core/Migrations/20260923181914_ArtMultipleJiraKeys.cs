using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class ArtMultipleJiraKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ArtJiraKey",
                table: "FeatureSnapshots",
                newName: "ArtJiraKeys");

            migrationBuilder.AlterColumn<string>(
                name: "ArtJiraKeys",
                table: "FeatureSnapshots",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "CapitalProjectJiraKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CapitalProjectId = table.Column<int>(type: "int", nullable: false),
                    JiraKey = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapitalProjectJiraKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapitalProjectJiraKeys_CapitalProjects_CapitalProjectId",
                        column: x => x.CapitalProjectId,
                        principalTable: "CapitalProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CapitalProjectJiraKeys_CapitalProjectId_JiraKey",
                table: "CapitalProjectJiraKeys",
                columns: new[] { "CapitalProjectId", "JiraKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapitalProjectJiraKeys_JiraKey",
                table: "CapitalProjectJiraKeys",
                column: "JiraKey");

            migrationBuilder.Sql(@"
INSERT INTO CapitalProjectJiraKeys (CapitalProjectId, JiraKey)
SELECT Id, UPPER(LTRIM(RTRIM(JiraKey)))
FROM CapitalProjects
WHERE JiraKey IS NOT NULL AND LTRIM(RTRIM(JiraKey)) <> ''
ORDER BY Id;");

            migrationBuilder.DropColumn(
                name: "JiraKey",
                table: "CapitalProjects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JiraKey",
                table: "CapitalProjects",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE cp
SET JiraKey = (SELECT TOP 1 k.JiraKey FROM CapitalProjectJiraKeys k WHERE k.CapitalProjectId = cp.Id ORDER BY k.Id)
FROM CapitalProjects cp;");

            migrationBuilder.DropTable(
                name: "CapitalProjectJiraKeys");

            migrationBuilder.Sql(@"
UPDATE FeatureSnapshots
SET ArtJiraKeys = LEFT(LEFT(ArtJiraKeys, CHARINDEX(',', ArtJiraKeys + ',') - 1), 10)
WHERE ArtJiraKeys IS NOT NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "ArtJiraKeys",
                table: "FeatureSnapshots",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "ArtJiraKeys",
                table: "FeatureSnapshots",
                newName: "ArtJiraKey");
        }
    }
}
