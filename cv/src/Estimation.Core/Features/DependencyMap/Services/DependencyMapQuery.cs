using System.Text;
using System.Text.Json;
using Estimation.Core.Features.Models;

namespace Estimation.Core.Features.DependencyMap.Services;

public sealed record DependencyMapUrlState(
    string? Art,
    string? Pi,
    string? Team,
    string? From,
    string? To,
    bool TableView,
    string? Focus,
    ChainMode Chain,
    bool IncludePiLabelMatches,
    DependencyFilter Filter,
    FeatureCardFilter FeatureFilter);

public static class DependencyMapQuery
{
    private const string TableViewValue = "table";
    private const string WholeChainValue = "whole";
    private const string TrueValue = "true";
    private const string FalseValue = "false";

    private static readonly (DependencyRisk Risk, string Token)[] RiskTokens =
    [
        (DependencyRisk.OutOfOrder, "outoforder"),
        (DependencyRisk.AtRisk, "atrisk"),
        (DependencyRisk.InOrder, "inorder"),
        (DependencyRisk.NotChecked, "notchecked")
    ];

    private static readonly string[] ScopeKeys = ["cp", "pi", "team", "from", "to", "view", "focus", "chain", "piLabels"];

    private static readonly JsonSerializerOptions PreferenceJson = new(JsonSerializerDefaults.Web);

    public static readonly IReadOnlyList<string> FilterKeys =
    [
        "types",
        "typesNot",
        "otherPi",
        "otherArt",
        "notInTool",
        "withinTeam",
        "risk",
        "unlinked",
        "search",
        "program",
        "programNot",
        "epic",
        "epicNot",
        "bo",
        "boNot",
        "status",
        "statusNot",
        "ragStatus",
        "ragStatusNot",
        "labels",
        "labelsNot",
        "reqStatus",
        "reqStatusNot",
        "techApproval",
        "techApprovalNot",
        "funding",
        "fundingNot"
    ];

    public static readonly IReadOnlyList<string> Keys = [.. ScopeKeys, .. FilterKeys];

    public static DependencyMapUrlState Parse(IReadOnlyDictionary<string, string?> query)
    {
        var q = IgnoringCase(query);

        var filter = new DependencyFilter
        {
            LinkTypes = List(q, "types"),
            LinkTypesNot = Flag(q, "typesNot", false),
            ShowOtherPi = Flag(q, "otherPi", true),
            ShowOtherArt = Flag(q, "otherArt", true),
            ShowNotInTool = Flag(q, "notInTool", false),
            ShowWithinTeam = Flag(q, "withinTeam", true),
            Risks = Risks(q),
            ShowUnlinked = Flag(q, "unlinked", false)
        };

        var featureFilter = new FeatureCardFilter
        {
            Search = Text(q, "search"),
            StrategicObjectives = List(q, "program"),
            StrategicObjectiveNot = Flag(q, "programNot", false),
            PortfolioEpics = List(q, "epic"),
            PortfolioEpicNot = Flag(q, "epicNot", false),
            BusinessOutcomes = List(q, "bo"),
            BusinessOutcomeNot = Flag(q, "boNot", false),
            Statuses = List(q, "status"),
            StatusNot = Flag(q, "statusNot", false),
            RagStatuses = List(q, "ragStatus"),
            RagStatusNot = Flag(q, "ragStatusNot", false),
            Labels = List(q, "labels"),
            LabelsNot = Flag(q, "labelsNot", false),
            RequirementStatuses = List(q, "reqStatus"),
            RequirementStatusNot = Flag(q, "reqStatusNot", false),
            TechnicalApprovals = List(q, "techApproval"),
            TechnicalApprovalNot = Flag(q, "techApprovalNot", false),
            FundingStatuses = List(q, "funding"),
            FundingStatusNot = Flag(q, "fundingNot", false)
        };

        return new DependencyMapUrlState(
            Text(q, "cp"),
            Text(q, "pi"),
            Text(q, "team"),
            Text(q, "from"),
            Text(q, "to"),
            string.Equals(Text(q, "view"), TableViewValue, StringComparison.OrdinalIgnoreCase),
            Text(q, "focus"),
            string.Equals(Text(q, "chain"), WholeChainValue, StringComparison.OrdinalIgnoreCase)
                ? ChainMode.WholeChain
                : ChainMode.Direct,
            Flag(q, "piLabels", true),
            filter,
            featureFilter);
    }

    public static Dictionary<string, object?> Write(DependencyMapUrlState state)
    {
        var filter = state.Filter;
        var cards = state.FeatureFilter;

        return new Dictionary<string, object?>
        {
            ["cp"] = Clean(state.Art),
            ["pi"] = Clean(state.Pi),
            ["team"] = Clean(state.Team),
            ["from"] = Clean(state.From),
            ["to"] = Clean(state.To),
            ["view"] = state.TableView ? TableViewValue : null,
            ["focus"] = Clean(state.Focus),
            ["chain"] = state.Chain == ChainMode.WholeChain ? WholeChainValue : null,
            ["piLabels"] = state.IncludePiLabelMatches ? null : FalseValue,
            ["types"] = Join(filter.LinkTypes),
            ["typesNot"] = filter.LinkTypesNot && filter.FiltersLinkTypes ? TrueValue : null,
            ["otherPi"] = filter.ShowOtherPi ? null : FalseValue,
            ["otherArt"] = filter.ShowOtherArt ? null : FalseValue,
            ["notInTool"] = filter.ShowNotInTool ? TrueValue : null,
            ["withinTeam"] = filter.ShowWithinTeam ? null : FalseValue,
            ["risk"] = RiskText(filter.Risks),
            ["unlinked"] = filter.ShowUnlinked ? TrueValue : null,
            ["search"] = Clean(cards.Search),
            ["program"] = Join(cards.StrategicObjectives),
            ["programNot"] = On(cards.StrategicObjectiveNot),
            ["epic"] = Join(cards.PortfolioEpics),
            ["epicNot"] = On(cards.PortfolioEpicNot),
            ["bo"] = Join(cards.BusinessOutcomes),
            ["boNot"] = On(cards.BusinessOutcomeNot),
            ["status"] = Join(cards.Statuses),
            ["statusNot"] = On(cards.StatusNot),
            ["ragStatus"] = Join(cards.RagStatuses),
            ["ragStatusNot"] = On(cards.RagStatusNot),
            ["labels"] = Join(cards.Labels),
            ["labelsNot"] = On(cards.LabelsNot),
            ["reqStatus"] = Join(cards.RequirementStatuses),
            ["reqStatusNot"] = On(cards.RequirementStatusNot),
            ["techApproval"] = Join(cards.TechnicalApprovals),
            ["techApprovalNot"] = On(cards.TechnicalApprovalNot),
            ["funding"] = Join(cards.FundingStatuses),
            ["fundingNot"] = On(cards.FundingStatusNot)
        };
    }

    public static bool HasFilterKeys(IReadOnlyDictionary<string, string?> query)
    {
        var q = IgnoringCase(query);
        return FilterKeys.Any(q.ContainsKey);
    }

    public static bool HasScopeKeys(IReadOnlyDictionary<string, string?> query)
    {
        var q = IgnoringCase(query);
        return Text(q, "cp") is not null || Text(q, "pi") is not null;
    }

    public static bool HasLinkTypeKeys(IReadOnlyDictionary<string, string?> query)
    {
        var q = IgnoringCase(query);
        return Text(q, "types") is not null || Text(q, "typesNot") is not null;
    }

    public static string? WriteLinkTypePreference(DependencyFilter filter) =>
        filter.FiltersLinkTypes
            ? JsonSerializer.Serialize(new LinkTypePreference(Distinct(filter.LinkTypes), filter.LinkTypesNot), PreferenceJson)
            : null;

    public static bool ReadLinkTypePreference(string? stored, DependencyFilter filter)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return false;
        }

        try
        {
            var preference = JsonSerializer.Deserialize<LinkTypePreference>(stored, PreferenceJson);
            var types = Distinct(preference?.Types ?? []);

            if (preference is null || types.Length == 0)
            {
                return false;
            }

            filter.LinkTypes = types;
            filter.LinkTypesNot = preference.Not;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool UsesStoredTeam(IReadOnlyDictionary<string, string?> query) => !HasScopeKeys(query);

    public static bool UsesStoredView(IReadOnlyDictionary<string, string?> query) =>
        !HasScopeKeys(query) && !IgnoringCase(query).ContainsKey("view");

    private static Dictionary<string, string?> IgnoringCase(IReadOnlyDictionary<string, string?> query)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in query)
        {
            result.TryAdd(key, value);
        }

        return result;
    }

    private static string? Text(IReadOnlyDictionary<string, string?> query, string key) =>
        query.TryGetValue(key, out var value) ? Clean(value) : null;

    private static string[] List(IReadOnlyDictionary<string, string?> query, string key) =>
        Distinct(Split(Text(query, key)));

    private static List<string> Split(string? text)
    {
        var values = new List<string>();

        if (text is null)
        {
            return values;
        }

        var value = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '\\' && i + 1 < text.Length && text[i + 1] is ',' or '\\')
            {
                value.Append(text[++i]);
            }
            else if (c == ',')
            {
                values.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(c);
            }
        }

        values.Add(value.ToString());
        return values;
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace(",", "\\,");

    private static bool Flag(IReadOnlyDictionary<string, string?> query, string key, bool fallback) =>
        bool.TryParse(Text(query, key), out var value) ? value : fallback;

    private static DependencyRisk[] Risks(IReadOnlyDictionary<string, string?> query)
    {
        var risks = List(query, "risk")
            .Select(token => RiskTokens.FirstOrDefault(t => string.Equals(t.Token, token, StringComparison.OrdinalIgnoreCase)))
            .Where(t => t.Token is not null)
            .Select(t => t.Risk)
            .Distinct()
            .ToArray();

        return risks.Length == RiskTokens.Length ? [] : risks;
    }

    private static string? RiskText(IReadOnlyCollection<DependencyRisk> risks)
    {
        var selected = risks.ToHashSet();

        if (selected.Count == 0 || RiskTokens.All(t => selected.Contains(t.Risk)))
        {
            return null;
        }

        return string.Join(",", RiskTokens.Where(t => selected.Contains(t.Risk)).Select(t => t.Token));
    }

    private static string? Join(IEnumerable<string?>? values)
    {
        var distinct = Distinct(values ?? []);
        return distinct.Length == 0 ? null : string.Join(",", distinct.Select(Escape));
    }

    private static string[] Distinct(IEnumerable<string?> values) =>
        values
            .Select(Clean)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? On(bool value) => value ? TrueValue : null;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record LinkTypePreference(string?[]? Types, bool Not);
}
