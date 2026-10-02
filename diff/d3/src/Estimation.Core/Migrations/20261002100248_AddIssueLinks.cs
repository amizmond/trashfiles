using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssueLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JiraLinkId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TypeName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    OutwardLabel = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    InwardLabel = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    FromKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ToKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueLinks_FromKey",
                table: "IssueLinks",
                column: "FromKey");

            migrationBuilder.CreateIndex(
                name: "IX_IssueLinks_JiraLinkId",
                table: "IssueLinks",
                column: "JiraLinkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueLinks_ToKey",
                table: "IssueLinks",
                column: "ToKey");

            migrationBuilder.Sql("UPDATE JiraSyncKeys SET LastSyncedWatermarkUtc = NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueLinks");
        }
    }
}
