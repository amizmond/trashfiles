namespace Estimation.Core.Features.DependencyMap.Services;

public sealed class DependencyFilter
{
    private const int RiskCount = 4;

    public IReadOnlyCollection<string> LinkTypes { get; set; } = [];

    public bool LinkTypesNot { get; set; }

    public bool ShowOtherPi { get; set; } = true;

    public bool ShowOtherArt { get; set; } = true;

    public bool ShowNotInTool { get; set; }

    public bool ShowWithinTeam { get; set; } = true;

    public IReadOnlyCollection<DependencyRisk> Risks { get; set; } = [];

    public bool ShowUnlinked { get; set; }

    public int ActiveCount
    {
        get
        {
            var count = FiltersLinkTypes ? 1 : 0;
            count += ShowOtherPi ? 0 : 1;
            count += ShowOtherArt ? 0 : 1;
            count += ShowNotInTool ? 1 : 0;
            count += ShowWithinTeam ? 0 : 1;
            count += RisksActive ? 1 : 0;
            count += ShowUnlinked ? 1 : 0;
            return count;
        }
    }

    public bool IsActive => ActiveCount > 0;

    public bool FiltersLinkTypes => LinkTypes.Any(t => !string.IsNullOrWhiteSpace(t));

    private bool RisksActive
    {
        get
        {
            var distinct = Risks.Distinct().Count();
            return distinct > 0 && distinct < RiskCount;
        }
    }

    public bool ShowsLinkType(string? typeName)
    {
        if (!FiltersLinkTypes)
        {
            return true;
        }

        var name = typeName?.Trim() ?? string.Empty;
        var listed = LinkTypes.Any(t => !string.IsNullOrWhiteSpace(t) && string.Equals(t.Trim(), name, StringComparison.OrdinalIgnoreCase));
        return listed != LinkTypesNot;
    }

    public void ToggleLinkType(string typeName, IEnumerable<string> knownTypes)
    {
        var name = typeName.Trim();
        var known = Distinct(knownTypes);

        if (name.Length == 0)
        {
            return;
        }

        if (!FiltersLinkTypes)
        {
            LinkTypesNot = true;
            LinkTypes = [name];
            return;
        }

        var shown = ShowsLinkType(name);
        var listed = Distinct(LinkTypes).Where(t => !string.Equals(t, name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (shown == LinkTypesNot)
        {
            listed.Add(name);
        }

        if (!LinkTypesNot && listed.Count == 0)
        {
            LinkTypesNot = true;
            LinkTypes = known;
            return;
        }

        if (!LinkTypesNot && known.Count > 0 && known.All(k => listed.Contains(k, StringComparer.OrdinalIgnoreCase)))
        {
            ShowAllLinkTypes();
            return;
        }

        LinkTypes = listed;

        if (listed.Count == 0)
        {
            LinkTypesNot = false;
        }
    }

    public void ShowAllLinkTypes()
    {
        LinkTypes = [];
        LinkTypesNot = false;
    }

    public void KeepKnownLinkTypes(IEnumerable<string> knownTypes)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in knownTypes.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            known.TryAdd(type.Trim(), type);
        }

        LinkTypes = LinkTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => known.GetValueOrDefault(t.Trim()) ?? (LinkTypesNot ? t.Trim() : null))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (LinkTypes.Count == 0)
        {
            LinkTypesNot = false;
        }
    }

    public void Clear()
    {
        LinkTypes = [];
        LinkTypesNot = false;
        ShowOtherPi = true;
        ShowOtherArt = true;
        ShowNotInTool = false;
        ShowWithinTeam = true;
        Risks = [];
        ShowUnlinked = false;
    }

    private static List<string> Distinct(IEnumerable<string> types) =>
        types
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
