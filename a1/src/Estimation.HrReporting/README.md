# Estimation.HrReporting

Self-contained module for **HR Reporting → Headcount**: HR uploads the Workday export, every upload is kept as an
immutable snapshot, and any two snapshots can be compared. Data changes only through uploads.

Built as a separate project so it can be removed in one piece.

## What it does

| Route | Page | Access |
|---|---|---|
| `/hr-reporting/headcount` | Headcount table of one snapshot (newest by default, `?upload=<id>` for older ones): columns, filters, totals, Excel export, uploads history | View |
| `/hr-reporting/headcount/upload` | Preview of a picked file against the previous snapshot, then **Save snapshot** | Edit |
| `/hr-reporting/headcount/compare?a=<id>&b=<id>` | Joiners, leavers and changes between two snapshots, Excel export | View |

All three share the `AppPages` key `HrHeadcount` (id 36, group `HR Reporting`, `GeneralOnly`): one profile grant
covers them, and nobody but Admins sees the page until a profile grants it. Deleting a snapshot is Admin only.

## The file

- Sheet `Headcount`; the header row is the first row (of the first 20) with a `BRID` cell.
- All 37 columns in `Models/HeadcountColumns.cs` are required; headers match ignoring case and spaces. Other
  columns are ignored and listed in the preview.
- The upload is blocked when a column is missing or doubled, a BRID is empty or repeated (ignoring case), a number
  or date can't be read, or a text is longer than its column. `-` counts as empty in number and date columns.
- Dates may be Excel dates, Excel serial numbers or text such as `15/03/2021` or `2021-03-15`.

## Snapshots and comparing

- `HrHeadcountUploads` holds one row per upload (data-as-of date, file, comment, who/when, totals) and
  `HrHeadcountRows` every row of every upload. There is no merged "current" table: each upload is the full
  headcount, so the newest snapshot is the current state.
- Snapshots are ordered by **data as of** date, then upload time. The preview compares with the newest snapshot on
  or before the chosen date, so older exports can be loaded later as history.
- People are matched by BRID. *Length of Service In Years* is ignored by default because it changes on every work
  anniversary; the compare views let users change the ignored fields.
- Rows of a snapshot are cached in memory (`HeadcountRowsCache`, 30 minutes sliding, at most 250,000 rows) and all
  filtering, sorting and comparing happens on that copy.
- On SQL Server the rows are written with `SqlBulkCopy` (20,000 rows in well under a second); other providers use
  EF `SaveChanges`.

## Isolation

- Own DbContext (`HrReportingDbContext`) on the same database with its own history table
  `__HrReportingMigrationsHistory`; the only outside table it touches is `AppPages` (mirror excluded from migrations).
- Rows are not written to `AuditLogs`; the upload row records who uploaded what and when.
- `SearchableDropdown` and `UserTimeZoneService` were moved unchanged (same namespaces) from the web project to
  `Estimation.Shared.UI` so this module can use them.

## Migrations

```bash
dotnet ef migrations add <Name> --project src/Estimation.HrReporting --startup-project src/Estimation --context HrReportingDbContext
```

Applied at start-up by `UseHrReportingModuleAsync`, which also registers the `AppPages` row.

## Removing the feature

1. Delete `src/Estimation.HrReporting/` and `tests/Estimation.HrReporting.Tests/`.
2. `dotnet sln Estimation.sln remove` both projects; remove the `ProjectReference` from `src/Estimation/Estimation.csproj`.
3. In `src/Estimation/Program.cs`: remove `using Estimation.HrReporting;`, `AddHrReportingModule(...)`,
   `UseHrReportingModuleAsync()` and the assembly in `AddAdditionalAssemblies`.
4. In `src/Estimation/Components/Routes.razor`: drop the module's assembly from `AdditionalAssemblies`.
5. In `src/Estimation/Components/MainLayout.razor`: remove the `HrReportingRoutes` arms from `GetPageHref`,
   `GetPageIcon` and `GetGroupIcon`.
6. In the database: `DROP TABLE` `HrHeadcountRows`, `HrHeadcountUploads` and `__HrReportingMigrationsHistory`, and
   `DELETE FROM AppPages WHERE Id = 36` (plus any `ProfilePagePermissions` rows for it).
