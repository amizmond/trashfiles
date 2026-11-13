# Estimation.HrReporting

Self-contained module for **HR Reporting**: HR uploads the Workday export, every upload is kept as an immutable
snapshot, any two snapshots can be compared, two report pages rebuild the HR workbook's *Metrics* and
*Teams Analysis* sheets from any snapshot, and *Organization Health* puts the MOR KPIs of two snapshots side by
side. Data changes only through uploads.

Built as a separate project so it can be removed in one piece.

## What it does

| Route | Page | `AppPages` key (id) | Access |
|---|---|---|---|
| `/hr-reporting/headcount` | Headcount table of one snapshot (newest by default, `?upload=<id>` for older ones): columns, filters, totals, Excel export, uploads history | `HrHeadcount` (36) | View |
| `/hr-reporting/headcount/upload` | Preview of a picked file against the previous snapshot, then **Save snapshot** (**Replace snapshot** when that date already has one) | `HrHeadcount` (36) | Edit |
| `/hr-reporting/headcount/person?brid=<BRID>&upload=<id>` | One person of a snapshot, every column read-only, plus their Resources *Teams & ARTs*, *Vacations* and *Skills* (read-only) when a Resources person has that BRID | `HrHeadcount` (36) | View |
| `/hr-reporting/headcount/compare?a=<id>&b=<id>` | Joiners, leavers and changes between two snapshots, Excel export | `HrHeadcount` (36) | View |
| `/hr-reporting/metrics` | The workbook's *Metrics* sheet: people per IMH L07 by colleague type, ART / NonART, Eng / NonEng, country and job, with the share rows | `HrMetrics` (37) | View |
| `/hr-reporting/teams-analysis` | The workbook's *Teams Analysis* sheet: per IMH L07 a Train/Team × Country table with Locations, Friction and Team Size, plus the summary | `HrTeamsAnalysis` (38) | View; Edit to hide trains/teams |
| `/hr-reporting/org-health?b=<id>&a=<id>` | The MOR *Organizational Health* slide: KPIs per IMH L07 plus Total for one snapshot (`b`, newest by default) next to an older one (`a`, the one before by default, `a=0` for none), with arrows, targets and Excel export | `HrOrgHealth` (39) | View; Edit to change targets |

All pages are in group `HR Reporting` with `GeneralOnly` scope; nobody but Admins sees them until a profile grants
them. Metrics, Teams Analysis and Organization Health only show counts, so they can be granted without the
person-level Headcount page.
Deleting a snapshot is Admin only.

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
- There is one snapshot per **data as of** date: saving a file for a date that already has one deletes that
  snapshot (and any older duplicates of the date) in the same transaction. The preview warns before saving and
  then compares with the snapshot it replaces.
- People are matched by BRID. *Length of Service In Years* is ignored by default because it changes on every work
  anniversary; the compare views let users change the ignored fields.
- Rows of a snapshot are cached in memory (`HeadcountRowsCache`, 30 minutes sliding, at most 250,000 rows) and all
  filtering, sorting and comparing happens on that copy.
- On SQL Server the rows are written with `SqlBulkCopy` (20,000 rows in well under a second); other providers use
  EF `SaveChanges`.

## Metrics and Teams Analysis

Both pages use the Headcount filter drawer. Metrics starts filtered to *Operational Status = Operational*, Teams
Analysis to that plus *ART / NonART = ART*, like the workbook's pivot filters. The fixed names they rely on are in
`Services/HeadcountRules.cs`.

- **Metrics** (`HeadcountMetrics`): columns are the IMH L07 values plus Grand Total. *Perm* = Apprentice, Graduate,
  Intern, Permanent/Regular; *fixed price* = Resource Augmentation (Fixed Price Non Headcount); the employee country
  table keeps Perm plus Temp/Contractor Payroll (Fixed Term). ART % and Engineering % divide by the column total and
  Non ART % = 1 − ART %, as in the workbook.
- **Teams Analysis** (`TeamsAnalysis`): people whose Train is empty, `N/A` or `#N/A` are left out. Locations =
  countries with at least one person; Friction: one country = none, four or more or India and USA together = high,
  neither India nor USA = low, otherwise med; Team Size: under 8, 8-12, over 12. # ARTs is the number of trains.
- **Hide list**: editors can hide a train or a team for one IMH L07 domain from the row menu, like the hand-picked
  pivot hides. Entries are stored by name in `HrTeamsAnalysisExclusions`, shared by everyone and applied to every
  snapshot; they are shown as chips and removed with ×.
- Checked against the HR workbook of Nov 2026: with its hand-picked hides entered, every Teams Analysis number
  matches, and Metrics matches apart from one person who changed between the workbook's two pivot refreshes.

## Organization Health

Rebuilds the *Wholesale Risk MOR | Organizational Health* slide (`OrgHealthReport`, `OrgHealthKpis`): one row per
KPI, grouped Workforce / Org Health Objectives / Agile Org Design, a Target column, then for Total and each IMH L07
value the older snapshot (grey) and the newer one (bold). No filter drawer: every KPI has its own fixed population.

- Only *Operational Status = Operational* people are counted (the optional *Non-operational headcount* KPI shows the
  rest). Total is everyone in the snapshot, also the domains switched off.
- Workforce and objective KPIs use the Metrics rules: fixed price, Perm, the country shares (one KPI per country,
  fixed price included), Engineering share, and *% Outside of train* = ART / NonART not ART.
- Team KPIs use the Teams Analysis rules and its hide list on operational ART people: trains and teams, the share
  of teams sized 8-12, co-located in at most two countries, and with no or low friction.
- Agile roles: a person's role is the *Target Agile Role* (split on commas; a bracket such as "(part time)" is a
  qualifier), or the Workday *Job* when the target role is empty. "in WD" counts people whose Workday *Job* is the
  role (Lean Portfolio Manager, Solution Train Engineer, Release Train Engineer, Scrum Master). A full-time Scrum
  Master has only the Scrum Master role and no "part" qualifier.
- Percentages are whole numbers, halves rounded up like the slide; arrows compare the shown values. Green/red say
  whether the KPI moved the better or worse way; KPIs with no better direction get grey arrows; "a | b" KPIs get none.
- Targets: defaults from the slide live in code; editors override them for everyone in `HrOrgHealthTargets` (only
  differences from the default are stored). A target has a text for the Target column and an optional rule (>, ≥,
  <, ≤ and a value) that marks the newer cell red when the shown value misses it.
- KPI and column choices are per browser (`localStorage` key `hrReporting.orgHealth.layout`).
- Checked against the slide's Sept-26 column with an anonymized copy of the HR workbook of Nov 2026: every
  data-driven cell matches. The slide's hand-entered numbers differ: CCR # Trains (typed 3, data 4), the WCR LPM
  (none in the data), the first RTE and Scrum Master numbers and the full-time Scrum Masters of WCR and CCR (role
  tags were still being updated), and Market Risk is one person short in that workbook's tagged cache.

## Isolation

- Own DbContext (`HrReportingDbContext`) on the same database with its own history table
  `__HrReportingMigrationsHistory`; the only outside table it touches is `AppPages` (mirror excluded from migrations).
- Rows are not written to `AuditLogs`; uploads, hide entries and targets record who made them and when.
- `SearchableDropdown` and `UserTimeZoneService` were moved unchanged (same namespaces) from the web project to
  `Estimation.Shared.UI` so this module can use them, and `Estimation.Excel` gained `GetOrCreateCellStyle` (also
  with a fill colour, for missed targets) and a `double` `StyledNumber` for the percentage cells of the exports.
- The same way, `PersonAvatar`, `ProfilePhotoUrls` and the Resources panels `HumanResourceMembershipPanel`,
  `HumanResourceVacationsPanel` and `HumanResourceSkillsPanel` (which gained `ReadOnly`) live in
  `Estimation.Shared.UI`. The person page reads Resources only through the Core services
  (`IHumanResourceService.GetByEmployeeNumberAsync`, `ISkillService`, `ICapacityCalendarService`) and never writes.

## Migrations

```bash
dotnet ef migrations add <Name> --project src/Estimation.HrReporting --startup-project src/Estimation --context HrReportingDbContext
```

Applied at start-up by `UseHrReportingModuleAsync`, which also registers the `AppPages` rows (`HrReportingRoutes.NavPages`).

## Removing the feature

1. Delete `src/Estimation.HrReporting/` and `tests/Estimation.HrReporting.Tests/`.
2. `dotnet sln Estimation.sln remove` both projects; remove the `ProjectReference` from `src/Estimation/Estimation.csproj`.
3. In `src/Estimation/Program.cs`: remove `using Estimation.HrReporting;`, `AddHrReportingModule(...)`,
   `UseHrReportingModuleAsync()` and the assembly in `AddAdditionalAssemblies`.
4. In `src/Estimation/Components/Routes.razor`: drop the module's assembly from `AdditionalAssemblies`.
5. In `src/Estimation/Components/MainLayout.razor`: remove the `HrReportingRoutes` arms from `GetPageHref`,
   `GetPageIcon` and `GetGroupIcon`.
6. In the database: `DROP TABLE` `HrOrgHealthTargets`, `HrTeamsAnalysisExclusions`, `HrHeadcountRows`,
   `HrHeadcountUploads` and `__HrReportingMigrationsHistory`, and `DELETE FROM AppPages WHERE Id IN (36, 37, 38, 39)`
   (plus any `ProfilePagePermissions` rows for them).
