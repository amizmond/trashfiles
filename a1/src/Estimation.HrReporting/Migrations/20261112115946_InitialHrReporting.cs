using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.HrReporting.Migrations
{
    /// <inheritdoc />
    public partial class InitialHrReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HrHeadcountUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DataDate = table.Column<DateTime>(type: "date", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    TotalHc = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalFte = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrHeadcountUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrHeadcountRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UploadId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Brid = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OperationalStatus = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CorporateGradeDescription = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ColleagueNonPermType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EmployeeEmailAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    EmployeePositionId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PositionDescription = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    JobDescription = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    LengthOfServiceDate = table.Column<DateTime>(type: "date", nullable: true),
                    CostCentre = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CostCentreName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Vendor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LineManagerBrid = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LineManagerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LineManagerEmailAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    ImhL07 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OrgUnitId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Building = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LengthOfServiceInYears = table.Column<decimal>(type: "decimal(12,4)", nullable: true),
                    ControlSpan = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ControlTier = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    JobFamilyGroup = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    JobFamilyDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Job = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    JobProfile = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Hc = table.Column<decimal>(type: "decimal(12,4)", nullable: true),
                    Fte = table.Column<decimal>(type: "decimal(12,4)", nullable: true),
                    Team = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Train = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ArtNonArt = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EngNonEng = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TargetAgileRole = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TargetLevel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Actions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrHeadcountRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HrHeadcountRows_HrHeadcountUploads_UploadId",
                        column: x => x.UploadId,
                        principalTable: "HrHeadcountUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HrHeadcountRows_UploadId_Brid",
                table: "HrHeadcountRows",
                columns: new[] { "UploadId", "Brid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HrHeadcountUploads_DataDate_UploadedAt",
                table: "HrHeadcountUploads",
                columns: new[] { "DataDate", "UploadedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HrHeadcountRows");

            migrationBuilder.DropTable(
                name: "HrHeadcountUploads");
        }
    }
}
