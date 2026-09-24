# Estimation.ArtPlanning

Self-contained module for the **ART capacity** page: a skill capacity table where every resource contributes their
whole day pool to a single skill, plus a separate settings page behind it holding five editable setup tables.

Built as a separate project so it can be removed in one piece.

## What it does

| Requirement | Table | Purpose |
|---|---|---|
| 1 | `ArtPlanningSkillRankings` | Skill priority per (ART, PI). A skill with no row is unranked, which makes it win ties. |
| 2 | `ArtPlanningSkillLevelAllocations` | Ratio and order of usage per skill level. A level with no row is not selectable at all. |
| 3 | `ArtPlanningRoleCoefficients` | Coefficient per team role; a role with no row counts as 1. |
| 4 | `ArtPlanningRoleSkillLimits` | Restricts a role to specific skills. A role with no rows may use any skill its members hold. |
| 5 | `ArtPlanningTrainCoefficients` | One multiplier per (ART, PI), applied last. 1 leaves the numbers unchanged. |
| 6 | — | The result table, calculated by `ArtPlanningCapacityService`. |
| 7 | — | Two routes: `/art-capacity` (`ArtCapacityPage.razor`) and `/art-capacity/settings` (`ArtPlanningSettingsPage.razor`). |

## The two pages

`/art-capacity` is where the menu lands. It shows the skill capacity table and nothing else to press: the table is
recalculated from the current settings every time the page is opened, and on every ART or PI change. A **Settings**
button leads to `/art-capacity/settings`; both pages share the ART and PI selection through local storage, and both
sit behind the same `AppPages` key so one profile grant covers them.

The question mark on the capacity page opens `ArtCapacityHelpDialog.razor`, which is the canonical explanation of
the whole calculation — window, day deductions, skill selection, the three multipliers, the Unassigned row, and the
rules that surprise people. **It is written for planners, and it is the thing to keep in step when the calculation
changes.**

## Scopes: Default and per-PI

Settings are stored per (ART, PI), and a NULL `PiId` is the ART's **Default** — the same shape
`TeamMemberCoefficient` uses for team defaults in Estimation.Core. The settings page's PI dropdown has a `Default`
entry alongside the real PIs.

Resolution happens **per table, independently**: a PI uses its own rows for a table if it has any, otherwise the
ART default's. So saving only the skill priorities for a PI leaves its level ratios still following the default.
The capacity page says which of the two it used.

Nothing is preset. An ART with no settings at all puts every resource in the Unassigned row, and the capacity page
says so. The skill priority grid still lists the ART's skills, because that list is derived from its resources
rather than configured, but they all start unranked.

**Copy from previous** replaces the selected PI's settings with those of the nearest earlier PI that has any,
naming the source in the confirmation. It leaves the current settings untouched when nothing earlier is configured.

## Rules that are deliberate

`ArtPlanningCapacityService` contains several rules that look like defects and are not. Each is marked
`DELIBERATE` in the source, listed on the help dialog, and pinned by a test in
`tests/Estimation.ArtPlanning.Tests/ArtPlanningDeliberateRuleTests.cs`:

- **Unranked skills sort first**, beating every ranked skill in their usage order.
- **Inactive members and empty teams** survive as nameless rows that still accrue working days into Unassigned.
- **A member with no team role, or no employee number, matches no skill** and lands in Unassigned.
- **A member holding a limited role on one team and an ordinary role on another is counted twice.**
- **Public holidays match on city only**; country-scoped rows deduct nothing.
- **Skill selection spans every ART**, so a role held on another ART's team can decide this one's coefficient.

Two places where the rules do not fully determine the answer, and what was chosen:

- They stop at (usage order, ranking), which can still tie. `SkillId` then team role name are appended so results
  are at least reproducible.
- Dates are normalised with `.Date`, which assumes PI, holiday and public-holiday dates are stored at midnight — as
  they are in practice.

## Isolation

- **Own DbContext** (`ArtPlanningDbContext`) against the same database, with its own migration history table
  `__ArtPlanningMigrationsHistory`. Nothing in `EstimationDbContext` or its migrations changes.
- **Own read-only mirrors** of the tables it reads (`Models/Source/SourceEntities.cs`), all mapped with
  `ExcludeFromMigrations()`. The generated migration creates only the five `ArtPlanning*` tables — verify with
  `grep CreateTable Migrations/*_InitialArtPlanningSettings.cs`.
- **Own tests** in `tests/Estimation.ArtPlanning.Tests`.
- Its rows are **not** written to `AuditLogs`: the audit interceptor is attached to `EstimationDbContext`'s factory,
  not this one.
- The ratio and coefficient columns deliberately carry **no store default**. EF Core treats a property equal to its
  CLR default as "not set" when the column has one, which would silently turn a ratio of 0 into 1.

## Migrations

```bash
dotnet ef migrations add <Name> --project src/Estimation.ArtPlanning --startup-project src/Estimation --context ArtPlanningDbContext
```

Applied at start-up by `UseArtPlanningModuleAsync`, which also registers the `AppPages` row (id 28, key
`ArtCapacity`) that puts the page in the navigation drawer and on the Profiles permission grid. The row is owned by
its **id**, so renaming the page or moving it in the menu updates the existing row and keeps profile grants intact.
A `HostAbortedException` at the end of the `dotnet ef` output is normal design-time noise.

## Removing the feature

1. Delete `src/Estimation.ArtPlanning/` and `tests/Estimation.ArtPlanning.Tests/`.
2. `dotnet sln Estimation.sln remove` both projects; remove the `ProjectReference` from `src/Estimation/Estimation.csproj`.
3. In `src/Estimation/Program.cs`: remove `using Estimation.ArtPlanning;`, the `AddArtPlanningModule(...)` call and
   the `UseArtPlanningModuleAsync()` call.
4. In `src/Estimation/Components/Routes.razor`: drop the module's assembly from `AdditionalAssemblies`.
5. In `src/Estimation/Components/MainLayout.razor`: remove the `ArtPlanningRoutes.PageKey` arms from `GetPageHref`
   and `GetPageIcon`.
6. In the database: `DROP TABLE` the five `ArtPlanning*` tables and `__ArtPlanningMigrationsHistory`, and
   `DELETE FROM AppPages WHERE [Key] = 'ArtCapacity'` (plus any `ProfilePagePermissions` rows for it).

## One thing this module changed outside itself

Menu order is a host concern, so `Risk Register`'s `SortOrder` moved from 102 to 103 in Estimation.Core's `AppPage`
seed (migration `MoveRiskRegisterBelowArtCapacity`) to free 102 for this page.

## Not built

Requirement 6's closing paragraph also sketches two allocation tables pairing skill capacity against skill demand.
That is out of scope here — the agreed deliverable was one result table.

The ART capacity board (`Components/Capacity/ArtCapacityView.razor`) used to render a pair of Resource Skills /
Demand Skills tables under a different capacity model, where a member's days were split across *all* their skills.
Those were removed when this module landed, along with the allocator in
`Estimation.Core/Capacity/Services/ArtCapacityService.cs` that fed them; that service now returns only the per-team
rows and the ART total.
