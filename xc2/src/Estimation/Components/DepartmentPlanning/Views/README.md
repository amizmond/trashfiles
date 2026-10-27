# Department Planning views

Each alternative view of the Department Planning page lives in its own folder here and is listed once in
`PlanningViewCatalog.All`. The page renders the selected entry through `DynamicComponent`; every view inherits
`PlanningViewBase`, receives the filtered `PlanningTree` and `DepartmentPlanningData`, and reports a row id through
`OnSelect` to select it and through `OnShowDetails` to open the details dialog.

A catalog entry with `CanZoom: true` shows the page's zoom buttons for that view. The page then sets the CSS variable
`--dp-view-zoom` (0.6 to 1.6) around the view, and the view's stylesheet multiplies its drawing size by it.

`PlanningViewBase` skips the render that a page render causes when `Tree`, `Data` and `Options` are the same instances
and `SelectedId`, `JiraTaskUrl` and `PiName` the same text; the view's own events still render it. A view whose own
state changes in `OnParametersSet` without a parameter changing calls `RequestRender()`. Give drawn elements handlers
that keep their instance across renders (`SelectHandler`, `DetailsHandler`, `DetailsKeyHandler` or a
`PlanningHandlerCache` cleared when the layout is rebuilt), so a hover does not send every element's handlers again.

To remove a view that is not needed:

1. Delete its folder under `Components/DepartmentPlanning/Views/`.
2. Delete its line in `PlanningViewCatalog.All` (the build fails until you do, which points you to the line).
3. Delete its layout code under `src/Estimation.Core/DepartmentPlanning/Views/` and its tests under
   `tests/Estimation.Core.Tests/DepartmentPlanning/Views/`, when it has any.

Each view was committed separately, so `git revert <commit>` undoes one view as well.
