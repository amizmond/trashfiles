using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureStateApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureStateApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ArtName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PiName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FeatureKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JiraId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FeatureName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    StateHash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    StateJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BaselineSnapshotId = table.Column<int>(type: "int", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WithdrawnBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    WithdrawnAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureStateApprovals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureStateApprovals_ArtName_PiName_FeatureKey",
                table: "FeatureStateApprovals",
                columns: new[] { "ArtName", "PiName", "FeatureKey" });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureStateApprovals_ArtName_PiName_WithdrawnAt",
                table: "FeatureStateApprovals",
                columns: new[] { "ArtName", "PiName", "WithdrawnAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureStateApprovals");
        }
    }
}
