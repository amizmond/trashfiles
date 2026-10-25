using System.Globalization;
using System.Text;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.DepartmentPlanning.Views;
using Estimation.Core.JiraIntegration.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Estimation.Components.DepartmentPlanning.Views;

public enum TreemapColour
{
    Level = 0,
    Progress = 1,
    Rag = 2,
}

public partial class TreemapView
{
    private sealed record TileView(
        TreemapTile Tile,
        PlanningRow? Row,
        string Line1,
        string Line2,
        string Tooltip,
        string Classes,
        string RagClass,
        bool ShowRagDot,
        double Progress,
        string Badges);

    private const string AlsoGlyph = "⧉";

    private TreemapArea _area = TreemapArea.Demand;
    private TreemapColour _colour = TreemapColour.Level;
    private int _depth = 2;
    private string? _zoomId;
    private TreemapLayoutResult _layout = TreemapLayoutResult.Empty;
    private List<TileView> _tiles = [];
    private PlanningTree? _builtTree;
    private TreemapArea _builtArea;
    private int _builtDepth;
    private string? _builtZoom;
    private string? _holderTileId;
    private string? _holderFor;
    private ElementReference _canvas;
    private bool _focusCanvas;

    private bool HasCapacity => Tree.TotalCapacitySp > 0m;

    private TreemapArea EffectiveArea => HasCapacity ? _area : TreemapArea.Demand;

    private PlanningLevel ZoomLevel => _layout.Root?.Level ?? PlanningLevel.Art;

    private string ViewBox => string.Create(CultureInfo.InvariantCulture, $"0 0 {TreemapLayout.Width} {TreemapLayout.Height}");

    private TreemapTile? SelectedTile => SelectedId is null ? null : _layout.Tiles.FirstOrDefault(t => t.Kind == TreemapTileKind.Row && t.RowId == SelectedId);

    private string ModeClass => _colour switch
    {
        TreemapColour.Progress => "mode-progress",
        TreemapColour.Rag => "mode-rag",
        _ => "mode-level",
    };

    private string EmptyText
    {
        get
        {
            if (Tree.FeaturesInScope == 0)
            {
                return $"No business epics are planned on the teams in scope for {PiName ?? "this PI"}.";
            }

            if (Tree.Roots.Count == 0)
            {
                return "No rows match the search and filters.";
            }

            if (_layout.IsZoomed && _layout.Tiles.Count == 0)
            {
                return EffectiveArea == TreemapArea.Planned
                    ? "Nothing under this item has planned story points: every business epic is below a line. Switch Area to Demand to see the ask."
                    : "Nothing under this item has story points.";
            }

            return EffectiveArea == TreemapArea.Planned
                ? "Nothing is planned: every business epic is below a team or ART line. Switch Area to Demand to see the ask."
                : "The business epics in scope have no story points.";
        }
    }

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(Tree, _builtTree) || _builtArea != EffectiveArea || _builtDepth != _depth || _builtZoom != _zoomId)
        {
            Rebuild();
        }

        if (_holderFor != SelectedId || (_holderTileId is not null && _layout.Tiles.All(t => t.RowId != _holderTileId)))
        {
            _holderFor = SelectedId;
            _holderTileId = FindHolder();
        }
    }

    private void Rebuild()
    {
        _zoomId = ResolveZoom(_zoomId);
        _layout = TreemapLayout.Build(Tree, EffectiveArea, _depth, _zoomId, Options.ActiveFilterCount == 0);
        _tiles = _layout.Tiles.Select(BuildTile).ToList();
        _builtTree = Tree;
        _builtArea = EffectiveArea;
        _builtDepth = _depth;
        _builtZoom = _zoomId;
        _holderFor = SelectedId;
        _holderTileId = FindHolder();
    }

    private string? ResolveZoom(string? id)
    {
        while (id is not null && Tree.Find(id) is null)
        {
            var slash = id.LastIndexOf('/');
            id = slash > 0 ? id[..slash] : null;
        }

        return id;
    }

    private string? FindHolder()
    {
        if (SelectedId is null || SelectedTile is not null)
        {
            return null;
        }

        var path = Tree.PathTo(SelectedId);
        for (var i = path.Count - 1; i >= 0; i--)
        {
            var id = path[i].Id;
            if (_layout.Tiles.Any(t => t.Kind == TreemapTileKind.Row && t.RowId == id))
            {
                return id;
            }

            var parentId = i > 0 ? path[i - 1].Id : null;
            var more = _layout.Tiles.FirstOrDefault(t => t.Kind == TreemapTileKind.More && t.ParentId == parentId);
            if (more is not null)
            {
                return more.RowId;
            }
        }

        return null;
    }

    private TileView BuildTile(TreemapTile tile) => tile.Kind switch
    {
        TreemapTileKind.Unused => UnusedTile(tile),
        TreemapTileKind.More => MoreTile(tile),
        _ => RowTile(tile),
    };

    private TileView UnusedTile(TreemapTile tile)
    {
        var sp = PlanningFormat.Sp(tile.Value);
        var line1 = tile.LabelMode == TreemapLabelMode.None ? "" : TreemapLayout.Fit("Unused capacity", tile.LabelChars);
        var line2 = tile.LabelMode == TreemapLabelMode.Full ? TreemapLayout.Fit($"{sp} SP", tile.LabelChars) : "";
        var tooltip = $"Unused capacity: {sp} SP\nThe teams in scope can deliver {PlanningFormat.Sp(Tree.TotalCapacitySp)} SP in this PI and {PlanningFormat.Sp(Tree.Total.PlannedSp)} SP are planned above the lines.";
        return new TileView(tile, null, line1, line2, tooltip, "unused", "rag-none", false, 0d, "");
    }

    private TileView MoreTile(TreemapTile tile)
    {
        var text = $"+{tile.MoreCount} more";
        var sp = PlanningFormat.Sp(tile.Value);
        var line1 = tile.LabelMode == TreemapLabelMode.None ? "" : TreemapLayout.Fit(text, tile.LabelChars);
        var line2 = tile.LabelMode == TreemapLabelMode.Full ? TreemapLayout.Fit($"{sp} SP", tile.LabelChars) : "";
        var kind = PlanningFormat.LevelPlural(tile.Level).ToLowerInvariant();
        var parent = tile.ParentId is null ? null : Tree.Find(tile.ParentId);
        var hint = parent is null || parent.Id == _zoomId
            ? "They are too small to draw here; the grid lists them."
            : "Click to zoom into the parent and see them.";
        var tooltip = $"{tile.MoreCount} smaller {kind} with {sp} SP {AreaWord} in total, drawn as one tile at least large enough to see.\n{hint}";
        return new TileView(tile, null, line1, line2, tooltip, $"more lvl-{(int)tile.Level}", "rag-none", false, 0d, "");
    }

    private TileView RowTile(TreemapTile tile)
    {
        var row = Tree.Find(tile.RowId);
        if (row is null)
        {
            return new TileView(tile, null, "", "", "", "lvl-1", "rag-none", false, 0d, "");
        }

        var metrics = row.Metrics;
        var key = row.JiraId;
        var name = row.Title + (row.AlsoUnder.Count > 0 ? " " + AlsoGlyph : "");
        var headline = string.IsNullOrEmpty(key) ? name : $"{key} {name}";
        var sp = $"{PlanningFormat.Sp(tile.Value)} SP";
        var share = EffectiveArea == TreemapArea.Planned && row.ShareOfCapacity is { } s ? $" · {PlanningFormat.Percent(s)} of capacity" : "";
        var notPlanned = EffectiveArea == TreemapArea.Demand && metrics.UnplannedSp > 0m ? $" · {PlanningFormat.Sp(metrics.UnplannedSp)} not planned" : "";
        var progress = _colour == TreemapColour.Progress && metrics.Features > 0 ? $" · {PlanningFormat.Percent(metrics.Progress)} done" : "";

        string line1;
        string line2;
        switch (tile.LabelMode)
        {
            case TreemapLabelMode.Full when tile.HasChildrenDrawn:
                line1 = TreemapLayout.Fit($"{headline} · {sp}{share}{notPlanned}", tile.LabelChars);
                line2 = "";
                break;
            case TreemapLabelMode.Full:
                line1 = TreemapLayout.Fit(headline, tile.LabelChars);
                line2 = TreemapLayout.Fit($"{sp}{share}{notPlanned}{progress}", tile.LabelChars);
                break;
            case TreemapLabelMode.Key:
                line1 = TreemapLayout.Fit(string.IsNullOrEmpty(key) ? name : key, tile.LabelChars);
                line2 = "";
                break;
            default:
                line1 = "";
                line2 = "";
                break;
        }

        var own = RagStatuses.Normalize(row.RagStatus);
        var worst = row.Level == PlanningLevel.BusinessEpic ? null : metrics.WorstRag;
        var rag = own ?? worst;
        var showDot = _colour != TreemapColour.Rag && rag is not null && Math.Min(tile.Width, tile.Height) >= 24d;

        var classes = new StringBuilder($"lvl-{(int)row.Level}");
        classes.Append(tile.HasChildrenDrawn ? " parent" : " leaf");
        if (row.IsBucket) classes.Append(" bucket");
        if (row.IsBelowArtLine) classes.Append(" below");
        if (row.Feature?.IsDone == true) classes.Append(" done");
        if (row.AlsoUnder.Count > 0) classes.Append(" also");

        var badges = tile.LabelMode == TreemapLabelMode.Full ? Badges(row) : "";

        return new TileView(tile, row, line1, line2, Tooltip(row, tile, own, worst), classes.ToString(), PlanningFormat.RagClass(rag), showDot, metrics.Progress, badges);
    }

    private static string Badges(PlanningRow row)
    {
        var parts = new List<string>();
        if (row.Metrics.DependenciesAtRisk > 0)
        {
            parts.Add($"{row.Metrics.DependenciesAtRisk} dep. at risk");
        }

        var hygiene = row.Level == PlanningLevel.BusinessEpic ? row.Feature?.HygieneFailures ?? 0 : row.Metrics.HygieneFailingFeatures;
        if (hygiene > 0)
        {
            parts.Add($"{hygiene} data quality");
        }

        return string.Join(" · ", parts);
    }

    private string Tooltip(PlanningRow row, TreemapTile tile, string? own, string? worst)
    {
        var m = row.Metrics;
        var text = new StringBuilder();
        text.Append(string.IsNullOrEmpty(row.JiraId) ? row.Title : $"{row.JiraId} {row.Title}").Append('\n');
        text.Append(PlanningFormat.LevelName(row.Level));
        if (row.IsBucket)
        {
            text.Append(" bucket: business epics without this level in Jira");
        }

        if (!string.IsNullOrWhiteSpace(row.Status))
        {
            text.Append(" · ").Append(row.Status);
        }

        text.Append('\n');
        text.Append($"Demand {PlanningFormat.Sp(m.DemandSp)} SP · Planned {PlanningFormat.Sp(m.PlannedSp)} SP · Not planned {PlanningFormat.Sp(m.UnplannedSp)} SP\n");
        if (row.ShareOfCapacity is { } share)
        {
            text.Append($"Share of capacity {PlanningFormat.Percent(share)}\n");
        }

        if (row.Level == PlanningLevel.BusinessEpic)
        {
            text.Append(row.Feature?.IsDone == true ? "Done\n" : $"Status class: {PlanningStatus.Label(PlanningStatus.Classify(row.Status))}\n");
        }
        else
        {
            text.Append($"Progress {PlanningFormat.Percent(m.Progress)} · {m.Done} of {m.Features} business epics done\n");
        }

        if (own is not null)
        {
            text.Append($"RAG {own}");
            if (worst is not null && DepartmentPlanningTree.RagRank(worst) < DepartmentPlanningTree.RagRank(own))
            {
                text.Append($" · worst underneath {worst}");
            }

            text.Append('\n');
        }
        else if (worst is not null)
        {
            text.Append($"Worst RAG underneath {worst}\n");
        }

        if (row.Ranks.Count > 0)
        {
            text.Append("Ranks: ").Append(string.Join(", ", row.Ranks.Select(r => $"{r.ArtAbbreviation} {r.Rank}{(r.BelowLine ? " (below the line)" : "")}"))).Append('\n');
        }

        if (row.IsBelowArtLine)
        {
            text.Append("Below the ART line: no capacity is reserved for it in this PI\n");
        }

        if (row.AlsoUnder.Count > 0)
        {
            text.Append($"{AlsoGlyph} Also under {string.Join(", ", row.AlsoUnder)}: drawn under each objective, counted once in the total\n");
        }

        if (m.DependenciesAtRisk > 0)
        {
            text.Append($"{m.DependenciesAtRisk} dependencies at risk\n");
        }

        var hygiene = row.Level == PlanningLevel.BusinessEpic ? row.Feature?.HygieneFailures ?? 0 : m.HygieneFailingFeatures;
        if (hygiene > 0)
        {
            text.Append(row.Level == PlanningLevel.BusinessEpic ? $"{hygiene} data quality rules failing\n" : $"{hygiene} business epics failing data quality rules\n");
        }

        text.Append(CanZoom(row) ? "Click to zoom in and open the details · Ctrl+click to open the details without zooming" : "Click to open the details");
        return text.ToString();
    }

    private bool CanZoom(PlanningRow row) =>
        row.Children.Any(c => c.Kind == PlanningRowKind.Node && TreemapLayout.ValueOf(c.Metrics, EffectiveArea) > 0m);

    private string AreaWord => EffectiveArea == TreemapArea.Planned ? "planned" : "demand";

    private string LegendText
    {
        get
        {
            var total = Tree.Total;
            var text = new StringBuilder();
            text.Append(EffectiveArea == TreemapArea.Planned ? "Area = planned story points." : "Area = demand story points; hatched = not planned.");
            if (_layout.UnusedCapacitySp > 0m)
            {
                text.Append(" Hollow tile = unused capacity.");
            }

            if (_tiles.Any(t => t.Classes.Contains(" also", StringComparison.Ordinal)))
            {
                text.Append($" {AlsoGlyph} = portfolio epic also under another objective, drawn under each; tile areas are not added up.");
            }

            text.Append($" Total: {PlanningFormat.Sp(total.DemandSp)} SP demand · {PlanningFormat.Sp(total.PlannedSp)} SP planned");
            if (HasCapacity)
            {
                text.Append($" ({PlanningFormat.Percent((double)(total.PlannedSp / Tree.TotalCapacitySp))} of {PlanningFormat.Sp(Tree.TotalCapacitySp)} SP capacity)");
            }

            text.Append($" · {PlanningFormat.Sp(total.UnplannedSp)} SP not planned");
            if (!HasCapacity)
            {
                text.Append(" · no capacity on the teams in scope, so shares are hidden and the area is the demand");
            }

            if (_layout.HiddenRows > 0)
            {
                text.Append($" · {_layout.HiddenRows} {(_layout.HiddenRows == 1 ? "item has" : "items have")} 0 {AreaWord} SP and take no area");
            }

            return text.ToString();
        }
    }

    private IEnumerable<(int Depth, string Label, bool Enabled)> DepthChoices()
    {
        var baseLevel = (int)ZoomLevel;
        for (var d = 1; d <= TreemapLayout.MaxDepth; d++)
        {
            var level = baseLevel + d;
            var enabled = level <= (int)PlanningLevel.BusinessEpic;
            var label = enabled ? ShortLevelName((PlanningLevel)level) : "—";
            yield return (d, label, enabled);
        }
    }

    private static string ShortLevelName(PlanningLevel level) => level switch
    {
        PlanningLevel.StrategicObjective => "Objectives",
        PlanningLevel.PortfolioEpic => "Epics",
        PlanningLevel.BusinessOutcome => "Outcomes",
        _ => "Business epics",
    };

    private static string CrumbText(PlanningRow row) => string.IsNullOrEmpty(row.JiraId) ? row.Title : $"{row.JiraId} {row.Title}";

    private static string Num(double value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static RenderFragment SvgText(string cssClass, double x, double y, double fontSize, string content) => builder =>
    {
        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "class", cssClass);
        builder.AddAttribute(2, "x", Num(x));
        builder.AddAttribute(3, "y", Num(y));
        builder.AddAttribute(4, "style", $"font-size:{Num(fontSize)}px");
        builder.AddContent(5, content);
        builder.CloseElement();
    };

    private static string ProgressWidth(TileView tile) => Num(Math.Max(0d, (tile.Tile.Width - 4d) * Math.Clamp(tile.Progress, 0d, 1d)));

    private void SetArea(TreemapArea area)
    {
        _area = area;
        Rebuild();
    }

    private void SetColour(TreemapColour colour)
    {
        _colour = colour;
        _tiles = _layout.Tiles.Select(BuildTile).ToList();
    }

    private void SetDepth(int depth)
    {
        _depth = Math.Clamp(depth, 1, TreemapLayout.MaxDepth);
        Rebuild();
    }

    private void ZoomTo(string? id)
    {
        _zoomId = id;
        Rebuild();
    }

    private void ZoomOut()
    {
        if (_layout.Path.Count == 0)
        {
            return;
        }

        ZoomTo(_layout.Path.Count > 1 ? _layout.Path[^2].Id : null);
        _focusCanvas = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusCanvas)
        {
            return;
        }

        _focusCanvas = false;
        try
        {
            await _canvas.FocusAsync(preventScroll: true);
        }
        catch (Microsoft.JSInterop.JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task ActivateAsync(TileView tile, bool selectOnly)
    {
        switch (tile.Tile.Kind)
        {
            case TreemapTileKind.Unused:
                return;
            case TreemapTileKind.More:
                if (tile.Tile.ParentId is { } parentId && parentId != _zoomId)
                {
                    ZoomTo(parentId);
                }

                return;
        }

        if (tile.Row is null)
        {
            return;
        }

        if (!selectOnly && CanZoom(tile.Row))
        {
            ZoomTo(tile.Row.Id);
            _focusCanvas = true;
        }

        await SelectAsync(tile.Row.Id);
    }

    private Task OnTileClickAsync(TileView tile, MouseEventArgs e) => ActivateAsync(tile, e.CtrlKey || e.ShiftKey || e.MetaKey);

    private Task OnTileKeyAsync(TileView tile, KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "Enter":
            case " ":
                return ActivateAsync(tile, e.CtrlKey || e.ShiftKey);
            case "Backspace":
            case "Escape":
                ZoomOut();
                return Task.CompletedTask;
            default:
                return Task.CompletedTask;
        }
    }

    private async Task OnCanvasKeyAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "Backspace":
            case "Escape":
                ZoomOut();
                break;
            case "Enter":
                if (_layout.Root is { } root)
                {
                    await SelectAsync(root.Id);
                }

                break;
        }
    }

    private async Task OnCrumbClickAsync(PlanningRow row)
    {
        if (row.Id == _zoomId)
        {
            await SelectAsync(row.Id);
        }
        else
        {
            ZoomTo(row.Id);
        }
    }
}
