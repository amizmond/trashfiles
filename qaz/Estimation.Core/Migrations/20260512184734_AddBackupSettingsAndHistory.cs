using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupSettingsAndHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TriggeredBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    BackupFolderPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ScheduleType = table.Column<int>(type: "int", nullable: false),
                    IntervalHours = table.Column<int>(type: "int", nullable: false),
                    DailyTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    WeeklyDay = table.Column<int>(type: "int", nullable: false),
                    RetentionCount = table.Column<int>(type: "int", nullable: false),
                    LastBackupAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextBackupAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AppPages",
                columns: new[] { "Id", "DisplayName", "Group", "IsAdminOnly", "Key", "SortOrder" },
                values: new object[] { 17, "Database Backup", "Admin", true, "DatabaseBackup", 93 });

            migrationBuilder.CreateIndex(
                name: "IX_BackupHistory_StartedAt",
                table: "BackupHistory",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupHistory");

            migrationBuilder.DropTable(
                name: "BackupSettings");

            migrationBuilder.DeleteData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 17);
        }
    }
}
