using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSprintSignOffAndReleaseDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseDate",
                table: "Sprints",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignOffDate",
                table: "Sprints",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseDate",
                table: "CapitalProjectSprints",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignOffDate",
                table: "CapitalProjectSprints",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "SignOffDate",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "CapitalProjectSprints");

            migrationBuilder.DropColumn(
                name: "SignOffDate",
                table: "CapitalProjectSprints");
        }
    }
}
