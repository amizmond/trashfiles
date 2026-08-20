using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureChangeReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureChangeReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CapitalProjectId = table.Column<int>(type: "int", nullable: true),
                    ArtName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ArtJiraKey = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PiName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BaselineSnapshotId = table.Column<int>(type: "int", nullable: false),
                    ReviewSnapshotId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureChangeReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureChangeReviews_FeatureSnapshots_BaselineSnapshotId",
                        column: x => x.BaselineSnapshotId,
                        principalTable: "FeatureSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeatureChangeReviews_FeatureSnapshots_ReviewSnapshotId",
                        column: x => x.ReviewSnapshotId,
                        principalTable: "FeatureSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeatureChangeReviewItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    FeatureKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JiraId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FeatureName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ChangeKind = table.Column<int>(type: "int", nullable: false),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureChangeReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureChangeReviewItems_FeatureChangeReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "FeatureChangeReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureChangeReviewItems_ReviewId_Decision",
                table: "FeatureChangeReviewItems",
                columns: new[] { "ReviewId", "Decision" });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureChangeReviewItems_ReviewId_FeatureKey",
                table: "FeatureChangeReviewItems",
                columns: new[] { "ReviewId", "FeatureKey" });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureChangeReviews_ArtName_PiName_CreatedAt",
                table: "FeatureChangeReviews",
                columns: new[] { "ArtName", "PiName", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureChangeReviews_BaselineSnapshotId",
                table: "FeatureChangeReviews",
                column: "BaselineSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureChangeReviews_ReviewSnapshotId",
                table: "FeatureChangeReviews",
                column: "ReviewSnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureChangeReviewItems");

            migrationBuilder.DropTable(
                name: "FeatureChangeReviews");
        }
    }
}
