using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureHygiene : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureHygieneRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CapitalProjectId = table.Column<int>(type: "int", nullable: false),
                    PiId = table.Column<int>(type: "int", nullable: true),
                    Field = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Check = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureHygieneRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureHygieneRules_CapitalProjects_CapitalProjectId",
                        column: x => x.CapitalProjectId,
                        principalTable: "CapitalProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FeatureHygieneRules_Pis_PiId",
                        column: x => x.PiId,
                        principalTable: "Pis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "AppPages",
                columns: new[] { "Id", "DisplayName", "Group", "IsAdminOnly", "Key", "ScopeMode", "SortOrder" },
                values: new object[] { 29, "Feature Hygiene", null, false, "FeatureHygiene", 2, 104 });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureHygieneRules_CapitalProjectId_PiId_SortOrder",
                table: "FeatureHygieneRules",
                columns: new[] { "CapitalProjectId", "PiId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureHygieneRules_PiId",
                table: "FeatureHygieneRules",
                column: "PiId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureHygieneRules");

            migrationBuilder.DeleteData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 29);
        }
    }
}
