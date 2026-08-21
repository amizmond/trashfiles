# Review rounds (feature change reviews)

Self-contained feature area: users start a *change review* for an ART + PI, the app captures the
current features as a new snapshot, diffs it against a baseline snapshot and persists one row per
Added / Changed / Removed feature. Reviewers approve or reject each row, then complete the review.
Nothing is written back to Jira. It builds on Feature Snapshots (`Features/`) but nothing in
`Features/` depends on it.

## Where the code lives

| Project | Folder | Contents |
|---|---|---|
| `Estimation.Core` | `ReviewRounds/` | models, EF model configuration, service, Excel export, snapshot-deletion guard, DI registration |
| `Estimation` | `Components/ReviewRounds/` | list page, detail page, start-review dialog, reject-comment dialog, display names |
| `Estimation.Core.Tests` | `ReviewRounds/` | service tests |

## Touch points outside these folders

Every one of them is marked with a `ReviewRounds` comment, so `grep -r ReviewRounds src` lists them:

| File | What |
|---|---|
| `Estimation.Core/Data/EstimationDbContext.cs` | `using Estimation.Core.ReviewRounds.Data;` + `modelBuilder.ApplyReviewRounds();` |
| `Estimation/Program.cs` | `using Estimation.Core.ReviewRounds;` + the `AddReviewRounds()` registration behind the `Approvals:ReviewRounds` setting |
| `Estimation/appsettings.json` | `"Approvals": { "ReviewRounds": true }` (the feature is on when the key is missing) |
| `Estimation.Shared.UI/RoutingPath.cs` | the two route constants |
| `Estimation/Components/Features/FeatureSnapshotList.razor` | the "Change reviews" button (entry point, shown only while the setting is on) |
| `Estimation.Core/Administration/Audit/AuditSaveChangesInterceptor.cs` | `"FeatureChangeReviewItem"` in the audit exclusion list (harmless if left) |
| `Estimation.Core/Migrations/20260819150659_AddFeatureChangeReviews.*` + `EstimationDbContextModelSnapshot.cs` | the migration that created the tables (generated; stays in the migration history) |

`Features/Services/IFeatureSnapshotDeletionGuard.cs` is a generic hook ("something may veto deleting
a snapshot") that this area implements; it is not review-specific and can stay.

The lightweight alternative under evaluation is `HashApprovals/` (approve-only, hash of the feature
state, shown directly in the Feature delta). Both can be on at the same time; switch either off with
`"Approvals": { "ReviewRounds": false }` / `{ "HashApprovals": false }` in `appsettings.json`. With
review rounds switched off the review pages are not linked anywhere, but their URLs still exist.

## How to remove the feature

1. Delete the three `ReviewRounds` folders.
2. Remove the marked lines listed above (the build points at the ones that no longer compile).
3. Add a migration — it will drop `FeatureChangeReviews` and `FeatureChangeReviewItems`:
   `dotnet ef migrations add RemoveFeatureChangeReviews --project src/Estimation.Core --startup-project src/Estimation`
