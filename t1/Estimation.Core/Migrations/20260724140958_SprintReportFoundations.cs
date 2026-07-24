using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class SprintReportFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "JiraBoardId",
                table: "Teams",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "JiraCompleteDate",
                table: "Sprints",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JiraSprintId",
                table: "Sprints",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "JiraStartDate",
                table: "Sprints",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JiraState",
                table: "Sprints",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SprintJiraMetrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SprintId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DataSource = table.Column<int>(type: "int", nullable: true),
                    CommittedSp = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    CompletedFromCommittedSp = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    NotCompletedFromCommittedSp = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    DeliveredSp = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    AddedSp = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    RemovedSp = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    ReEstimationNetSp = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    ReEstimatedIssueCount = table.Column<int>(type: "int", nullable: true),
                    LateEstimatedIssueCount = table.Column<int>(type: "int", nullable: true),
                    LateEstimatedSp = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    UnestimatedIssueCount = table.Column<int>(type: "int", nullable: true),
                    CarryOverIssueCount = table.Column<int>(type: "int", nullable: true),
                    BaselineCapturedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinalCapturedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintJiraMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintJiraMetrics_Sprints_SprintId",
                        column: x => x.SprintId,
                        principalTable: "Sprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SprintMetricsSyncHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TriggeredBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    BaselinesCaptured = table.Column<int>(type: "int", nullable: false),
                    FinalsComputed = table.Column<int>(type: "int", nullable: false),
                    Backfilled = table.Column<int>(type: "int", nullable: false),
                    Skipped = table.Column<int>(type: "int", nullable: false),
                    Failed = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintMetricsSyncHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SprintMetricsSyncSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CycleCooldownMinutes = table.Column<int>(type: "int", nullable: false),
                    BackfillBatchSize = table.Column<int>(type: "int", nullable: false),
                    IssueTypesCsv = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DoneStatusesCsv = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EnablementFloorUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AgileApiAvailable = table.Column<bool>(type: "bit", nullable: true),
                    SprintReportAvailable = table.Column<bool>(type: "bit", nullable: true),
                    LastProbedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastProbeMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintMetricsSyncSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SprintJiraMetricsIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SprintJiraMetricsId = table.Column<int>(type: "int", nullable: false),
                    IssueKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IssueType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SpAtStart = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    SpAtEnd = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    StatusAtEnd = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDoneAtEnd = table.Column<bool>(type: "bit", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    WasCarriedOver = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintJiraMetricsIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintJiraMetricsIssues_SprintJiraMetrics_SprintJiraMetricsId",
                        column: x => x.SprintJiraMetricsId,
                        principalTable: "SprintJiraMetrics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_JiraSprintId",
                table: "Sprints",
                column: "JiraSprintId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintJiraMetrics_SprintId",
                table: "SprintJiraMetrics",
                column: "SprintId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SprintJiraMetricsIssues_SprintJiraMetricsId",
                table: "SprintJiraMetricsIssues",
                column: "SprintJiraMetricsId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintMetricsSyncHistory_StartedAt",
                table: "SprintMetricsSyncHistory",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SprintJiraMetricsIssues");

            migrationBuilder.DropTable(
                name: "SprintMetricsSyncHistory");

            migrationBuilder.DropTable(
                name: "SprintMetricsSyncSettings");

            migrationBuilder.DropTable(
                name: "SprintJiraMetrics");

            migrationBuilder.DropIndex(
                name: "IX_Sprints_JiraSprintId",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "JiraBoardId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "JiraCompleteDate",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "JiraSprintId",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "JiraStartDate",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "JiraState",
                table: "Sprints");
        }
    }
}
