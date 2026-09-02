# Feature hygiene

Per-ART rules that say when a feature is fit for planning, and the evaluation that lists the
features that are not. Self-contained on the hash-approvals pattern: everything lives in this
folder, the host touches it in a handful of lines.

## Shape

- `Models/FeatureHygieneRule` — one row per rule: ART, field, check, parameters as JSON, optional
  message, order, enabled flag. Rules are pass or fail; a feature that fails any enabled rule of
  its ART is not healthy. `PiId` is always null today and reserved for per-PI overrides.
- `Models/HygieneField` and `HygieneCheck` — the catalogue. A field's kind (text, number, date,
  reference, choice, flag) decides which checks apply and how the parameters are edited. Both enums
  are stored as text, so add members at the end and never rename a saved one.
- `Models/HygieneRuleParameters` — the JSON payload: phrases and mode, minimum other words, a
  number, a date, or values. `"(empty)"` in a value list stands for an empty field.
- `Services/HygieneText` — strips Jira wiki markup, matches whole-word phrases, counts words.
- `Services/FeatureHygieneEvaluator` — pure: a feature with its lookups plus the rules gives the
  failures. No database, no services.
- `Services/FeatureHygieneRuleService` — rule sets per ART, copy between ARTs, starter defaults,
  the values a choice field can take.
- `Services/FeatureHygieneService` — the report for an ART and PI, and the rules keyed by Jira
  project key for the Features list.
- `Services/FeatureHygieneExcelExportService` — the report as a workbook.

## Decisions that are easy to get wrong

- Empty values fail *Not empty*, *Contains words*, *Not only these words* and *In values*, and pass
  the range and date checks. Emptiness is its own rule so "if present, at most 21" stays sayable.
- *Not only these words* checks other content only. It does not require the phrases to be present;
  pair it with *Contains words* for that.
- Text is normalised before any text check: markup removed, whitespace collapsed, matching is
  case-insensitive and whole-word, so *Result* does not match *Results*.
- Choice values compare by name, case-insensitively, and "(empty)" is a choosable value.
- Dates compare on the date part only.
- The ART and PI slice is the snapshot slice, through `Features/Services/FeatureScope`.

## Host footprint

- `EstimationDbContext.OnModelCreating` calls `ApplyFeatureHygiene()` and seeds `AppPages` row 29
  (`FeatureHygiene`, train-scoped, sort order 104, right under Risk Register).
- `Program.cs` calls `AddFeatureHygiene()`.
- `RoutingPath.FeatureHygiene` and `RoutingPath.FeatureHygieneRules`; `MainLayout` has one href arm
  and one icon arm for the page key.
- Pages in `src/Estimation/Components/FeatureHygiene`; the Features list shows a hygiene column
  through `IFeatureHygieneService.GetRulesByArtJiraKeyAsync()`.
- Tests in `tests/Estimation.Core.Tests/Features/Hygiene`.

## Migrations

```bash
dotnet ef migrations add <Name> --project src/Estimation.Core --startup-project src/Estimation
```

## Removing the feature

1. Delete this folder, `src/Estimation/Components/FeatureHygiene` and
   `tests/Estimation.Core.Tests/Features/Hygiene`.
2. Remove `ApplyFeatureHygiene()` and the `AppPages` row 29 from `EstimationDbContext`, then add a
   migration that drops `FeatureHygieneRules` and the row.
3. Remove `AddFeatureHygiene()` from `Program.cs`, the two `RoutingPath` constants, the two
   `MainLayout` arms, and the hygiene column and filter from `FeatureList.razor`.
