using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimation.Core.Migrations
{
    /// <inheritdoc />
    public partial class JiraSyncPerKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JiraSyncKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JiraKey = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreateNotExisted = table.Column<bool>(type: "bit", nullable: false),
                    UpdateExisted = table.Column<bool>(type: "bit", nullable: false),
                    IssueTypesCsv = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LabelsCsv = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StatusesCsv = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExcludeCreateStatusesCsv = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DateFilterMode = table.Column<int>(type: "int", nullable: false),
                    SinceFloorUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSyncedWatermarkUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JiraSyncKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JiraSyncKeys_JiraKey",
                table: "JiraSyncKeys",
                column: "JiraKey",
                unique: true);

            migrationBuilder.Sql(@"
SELECT k.JiraKey,
       s.Id AS SettingsId,
       s.CreateNotExisted,
       s.UpdateExisted,
       s.IssueTypesCsv,
       s.LabelsCsv,
       s.StatusesCsv,
       s.ExcludeCreateStatusesCsv,
       s.DateFilterMode,
       s.SinceFloorUtc,
       CASE WHEN s.CreateNotExisted = 0 AND s.UpdateExisted = 0 THEN 0
            WHEN EXISTS (SELECT 1 FROM STRING_SPLIT(s.IssueTypesCsv, ',') x WHERE LTRIM(RTRIM(x.value)) <> '') THEN 2
            ELSE 1 END AS SourceRank
INTO #Src
FROM JiraSyncProjectSettings s
INNER JOIN CapitalProjectJiraKeys k ON k.CapitalProjectId = s.CapitalProjectId;

DELETE a
FROM #Src a
WHERE EXISTS (SELECT 1 FROM #Src b WHERE b.JiraKey = a.JiraKey AND b.SourceRank > a.SourceRank);

WITH Keys AS (
    SELECT JiraKey,
           CAST(MAX(CAST(CreateNotExisted AS int)) AS bit) AS CreateNotExisted,
           CAST(MAX(CAST(UpdateExisted AS int)) AS bit) AS UpdateExisted,
           CASE WHEN MIN(DateFilterMode) = MAX(DateFilterMode) THEN MIN(DateFilterMode) ELSE 0 END AS DateFilterMode,
           CASE WHEN COUNT(SinceFloorUtc) = COUNT(*) THEN MIN(SinceFloorUtc) END AS SinceFloorUtc,
           SUM(CASE WHEN NULLIF(LTRIM(RTRIM(LabelsCsv)), '') IS NULL THEN 1 ELSE 0 END) AS AnyLabel,
           SUM(CASE WHEN NULLIF(LTRIM(RTRIM(StatusesCsv)), '') IS NULL THEN 1 ELSE 0 END) AS AnyStatus,
           SUM(CASE WHEN CreateNotExisted = 1 THEN 1 ELSE 0 END) AS Creators,
           SUM(CASE WHEN CreateNotExisted = 1 AND NULLIF(LTRIM(RTRIM(ExcludeCreateStatusesCsv)), '') IS NULL THEN 1 ELSE 0 END) AS NoExclusion
    FROM #Src
    GROUP BY JiraKey
)
INSERT INTO JiraSyncKeys
    (JiraKey, CreateNotExisted, UpdateExisted, IssueTypesCsv, LabelsCsv, StatusesCsv,
     ExcludeCreateStatusesCsv, DateFilterMode, SinceFloorUtc, LastSyncedWatermarkUtc)
SELECT k.JiraKey,
       k.CreateNotExisted,
       k.UpdateExisted,
       CASE WHEN LEN(t.Csv) <= 1000 THEN t.Csv END,
       CASE WHEN k.AnyLabel = 0 AND LEN(l.Csv) <= 2000 THEN l.Csv END,
       CASE WHEN k.AnyStatus = 0 AND LEN(st.Csv) <= 2000 THEN st.Csv END,
       CASE WHEN k.Creators > 0 AND k.NoExclusion = 0 AND LEN(e.Csv) <= 2000 THEN e.Csv END,
       k.DateFilterMode,
       k.SinceFloorUtc,
       NULL
FROM Keys k
OUTER APPLY (
    SELECT STRING_AGG(CAST(v.Value AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY v.Value) AS Csv
    FROM (SELECT DISTINCT LTRIM(RTRIM(x.value)) AS Value
          FROM #Src s CROSS APPLY STRING_SPLIT(s.IssueTypesCsv, ',') x
          WHERE s.JiraKey = k.JiraKey AND LTRIM(RTRIM(x.value)) <> '') v) t
OUTER APPLY (
    SELECT STRING_AGG(CAST(v.Value AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY v.Value) AS Csv
    FROM (SELECT DISTINCT LTRIM(RTRIM(x.value)) AS Value
          FROM #Src s CROSS APPLY STRING_SPLIT(s.LabelsCsv, ',') x
          WHERE s.JiraKey = k.JiraKey AND LTRIM(RTRIM(x.value)) <> '') v) l
OUTER APPLY (
    SELECT STRING_AGG(CAST(v.Value AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY v.Value) AS Csv
    FROM (SELECT DISTINCT LTRIM(RTRIM(x.value)) AS Value
          FROM #Src s CROSS APPLY STRING_SPLIT(s.StatusesCsv, ',') x
          WHERE s.JiraKey = k.JiraKey AND LTRIM(RTRIM(x.value)) <> '') v) st
OUTER APPLY (
    SELECT STRING_AGG(CAST(v.Value AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY v.Value) AS Csv
    FROM (SELECT LTRIM(RTRIM(x.value)) AS Value
          FROM #Src s CROSS APPLY STRING_SPLIT(s.ExcludeCreateStatusesCsv, ',') x
          WHERE s.JiraKey = k.JiraKey AND s.CreateNotExisted = 1 AND LTRIM(RTRIM(x.value)) <> ''
          GROUP BY LTRIM(RTRIM(x.value))
          HAVING COUNT(DISTINCT s.SettingsId) = k.Creators) v) e;

DROP TABLE #Src;");

            migrationBuilder.DropTable(
                name: "JiraSyncProjectSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JiraSyncProjectSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CapitalProjectId = table.Column<int>(type: "int", nullable: false),
                    CreateNotExisted = table.Column<bool>(type: "bit", nullable: false),
                    DateFilterMode = table.Column<int>(type: "int", nullable: false),
                    ExcludeCreateStatusesCsv = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IssueTypesCsv = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LabelsCsv = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastSyncedWatermarkUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SinceFloorUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusesCsv = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdateExisted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JiraSyncProjectSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JiraSyncProjectSettings_CapitalProjects_CapitalProjectId",
                        column: x => x.CapitalProjectId,
                        principalTable: "CapitalProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JiraSyncProjectSettings_CapitalProjectId",
                table: "JiraSyncProjectSettings",
                column: "CapitalProjectId",
                unique: true);

            migrationBuilder.Sql(@"
INSERT INTO JiraSyncProjectSettings
    (CapitalProjectId, CreateNotExisted, UpdateExisted, IssueTypesCsv, LabelsCsv, StatusesCsv,
     ExcludeCreateStatusesCsv, DateFilterMode, SinceFloorUtc, LastSyncedWatermarkUtc)
SELECT f.CapitalProjectId, s.CreateNotExisted, s.UpdateExisted, s.IssueTypesCsv, s.LabelsCsv, s.StatusesCsv,
       s.ExcludeCreateStatusesCsv, s.DateFilterMode, s.SinceFloorUtc, NULL
FROM (
    SELECT k.CapitalProjectId, MIN(k.Id) AS KeyRowId
    FROM CapitalProjectJiraKeys k
    INNER JOIN JiraSyncKeys s ON s.JiraKey = k.JiraKey
    GROUP BY k.CapitalProjectId
) f
INNER JOIN CapitalProjectJiraKeys k ON k.Id = f.KeyRowId
INNER JOIN JiraSyncKeys s ON s.JiraKey = k.JiraKey;");

            migrationBuilder.DropTable(
                name: "JiraSyncKeys");
        }
    }
}
