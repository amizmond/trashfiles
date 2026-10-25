# Department Planning views

Each alternative view of the Department Planning page lives in its own folder here and is listed once in
`PlanningViewCatalog.All`. The page renders the selected entry through `DynamicComponent`; every view inherits
`PlanningViewBase`, receives the filtered `PlanningTree` and `DepartmentPlanningData`, and reports a row id through
`OnSelect` so the shared details panel opens.

A catalog entry with `CanZoom: true` shows the page's zoom buttons for that view. The page then sets the CSS variable
`--dp-view-zoom` (0.6 to 1.6) around the view, and the view's stylesheet multiplies its drawing size by it.

To remove a view that is not needed:

1. Delete its folder under `Components/DepartmentPlanning/Views/`.
2. Delete its line in `PlanningViewCatalog.All` (the build fails until you do, which points you to the line).
3. Delete its layout code under `src/Estimation.Core/DepartmentPlanning/Views/` and its tests under
   `tests/Estimation.Core.Tests/DepartmentPlanning/Views/`, when it has any.

Each view was committed separately, so `git revert <commit>` undoes one view as well.
