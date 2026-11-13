using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.HrReporting.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsAnalysisExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HrTeamsAnalysisExclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Domain = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrTeamsAnalysisExclusions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HrTeamsAnalysisExclusions_Domain_Kind_Value",
                table: "HrTeamsAnalysisExclusions",
                columns: new[] { "Domain", "Kind", "Value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HrTeamsAnalysisExclusions");
        }
    }
}
