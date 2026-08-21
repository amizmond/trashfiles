# Hash approvals (feature state approvals)

Self-contained feature area, the lightweight alternative to `ReviewRounds/`. On the Feature delta page
every Added / Changed / Removed row shows whether the feature's **current state** has been approved.
Approving stores a SHA-256 hash of the feature's compared fields (plus the values themselves); the
delta looks the hash up on every render, so an approval is recognised in every comparison that shows
that state and stops matching as soon as the feature changes again. There is no "reject": a change is
either approved or not approved yet. Approvals can be withdrawn (soft delete), and nothing is written
back to Jira.

The hash is computed from `FeatureSnapshotDeltaService.CanonicalValue`, the same definition of
"unchanged" the delta uses (trimmed text, date-only dates, order/case-insensitive label and team lists).
`FeatureStateHasherTests` asserts that hash equality and "delta reports no change" stay equivalent.

## Where the code lives

| Project | Folder | Contents |
|---|---|---|
| `Estimation.Core` | `HashApprovals/` | model, EF model configuration, hasher, approval service, DI registration |
| `Estimation` | `Components/HashApprovals/` | `DeltaApprovalState` (page helper), approval cell, toolbar (filters + bulk approve), approve dialog |
| `Estimation.Core.Tests` | `HashApprovals/` | hasher and service tests |

## Touch points outside these folders

Every one of them is marked with a `HashApprovals` comment, so `grep -r HashApprovals src` lists them:

| File | What |
|---|---|
| `Estimation.Core/Data/EstimationDbContext.cs` | `using Estimation.Core.HashApprovals.Data;` + `modelBuilder.ApplyHashApprovals();` |
| `Estimation/Program.cs` | `using Estimation.Core.HashApprovals;` + the `AddHashApprovals()` registration behind the `Approvals:HashApprovals` setting |
| `Estimation/appsettings.json` | `"Approvals": { "HashApprovals": true }` (the feature is on when the key is missing) |
| `Estimation/Components/Features/FeatureSnapshotDelta.razor` | the optional Approval column, the approval toolbar, the approval filter, the export column — all inside `_approvals is not null` blocks |
| `Estimation.Core/Features/Services/FeatureDeltaExcelExportService.cs` | the generic `ExtraColumn` parameter (not approval-specific, can stay) |
| `Estimation.Core/Features/Services/FeatureSnapshotDeltaService.cs` | `CanonicalValue` / `FeatureDeltaFields.All` (generic, the delta itself uses them; can stay) |
| `Estimation.Core/Migrations/*_AddFeatureStateApprovals.*` + `EstimationDbContextModelSnapshot.cs` | the migration that created the table (generated; stays in the migration history) |

## How to remove the feature

1. Delete the three `HashApprovals` folders.
2. Remove the marked lines listed above (the build points at the ones that no longer compile).
3. Add a migration — it will drop `FeatureStateApprovals`:
   `dotnet ef migrations add RemoveFeatureStateApprovals --project src/Estimation.Core --startup-project src/Estimation`

To switch it off without removing it, set `"Approvals": { "HashApprovals": false }` in `appsettings.json`.
