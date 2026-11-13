using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.HrReporting.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgHealthTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HrOrgHealthTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KpiKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Comparison = table.Column<int>(type: "int", nullable: true),
                    Value = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrOrgHealthTargets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HrOrgHealthTargets_KpiKey",
                table: "HrOrgHealthTargets",
                column: "KpiKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HrOrgHealthTargets");
        }
    }
}
