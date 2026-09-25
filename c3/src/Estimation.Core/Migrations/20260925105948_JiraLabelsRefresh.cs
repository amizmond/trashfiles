using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class JiraLabelsRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LabelsSyncEnabled",
                table: "JiraSyncSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("DELETE FROM JiraLabelCaches WHERE CacheKey = N'_global'");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "JiraLabelCaches",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.Sql("UPDATE JiraLabelCaches SET UpdatedAt = NULL WHERE LabelsJson = N'[]'");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "JiraLabelCaches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "JiraLabelCaches",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LabelsSyncEnabled",
                table: "JiraSyncSettings");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "JiraLabelCaches");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "JiraLabelCaches");

            migrationBuilder.Sql("DELETE FROM JiraLabelCaches WHERE UpdatedAt IS NULL");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "JiraLabelCaches",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }
    }
}
