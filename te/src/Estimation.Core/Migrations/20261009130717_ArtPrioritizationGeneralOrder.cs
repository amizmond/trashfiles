using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class ArtPrioritizationGeneralOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ranking",
                table: "PortfolioEpics");

            migrationBuilder.DropColumn(
                name: "Ranking",
                table: "BusinessOutcomes");

            migrationBuilder.AlterColumn<int>(
                name: "PiId",
                table: "ArtPrioritizationOrders",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.UpdateData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 32,
                column: "SortOrder",
                value: 21);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Ranking",
                table: "PortfolioEpics",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Ranking",
                table: "BusinessOutcomes",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PiId",
                table: "ArtPrioritizationOrders",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AppPages",
                keyColumn: "Id",
                keyValue: 32,
                column: "SortOrder",
                value: 29);
        }
    }
}
