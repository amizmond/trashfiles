using Estimation.Core.Features.DependencyMap.Services;
using Estimation.Core.Features.Models;
using Xunit;

namespace Estimation.Core.Tests.Features.DependencyMap;

public class DependencyMapQueryTests
{
    private static DependencyMapUrlState DefaultState() =>
        new(null, null, null, null, null, false, null, ChainMode.Direct, true, new DependencyFilter(), new FeatureCardFilter());

    private static Dictionary<string, string?> AsQuery(Dictionary<string, object?> written) =>
        written
            .Where(p => p.Value is not null)
            .ToDictionary(p => p.Key, p => (string?)p.Value!.ToString());

    private static DependencyMapUrlState Parse(params (string Key, string? Value)[] pairs) =>
        DependencyMapQuery.Parse(pairs.ToDictionary(p => p.Key, p => p.Value));

    [Fact]
    public void Defaults_write_every_key_as_null()
    {
        var written = DependencyMapQuery.Write(DefaultState());

        Assert.Equal(DependencyMapQuery.Keys.Order(StringComparer.Ordinal), written.Keys.Order(StringComparer.Ordinal));
        Assert.All(written, p => Assert.Null(p.Value));
    }

    [Fact]
    public void Keys_are_the_scope_keys_then_the_filter_keys()
    {
        Assert.Equal(
            ["cp", "pi", "team", "from", "to", "view", "focus", "chain", "piLabels"],
            DependencyMapQuery.Keys.Take(9));
        Assert.Equal(DependencyMapQuery.FilterKeys, DependencyMapQuery.Keys.Skip(9));
        Assert.Equal(DependencyMapQuery.Keys.Count, DependencyMapQuery.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Round_trips_every_setting_and_the_feature_filter()
    {
        var state = new DependencyMapUrlState(
            "Payments",
            "PI 26.1",
            "Axis",
            "Regal",
            "(Not in tool)",
            true,
            "PAY-7",
            ChainMode.WholeChain,
            false,
            new DependencyFilter
            {
                LinkTypes = ["Blocks", "(unnamed)"],
                LinkTypesNot = true,
                ShowOtherPi = false,
                ShowOtherArt = false,
                ShowNotInTool = true,
                ShowWithinTeam = false,
                Risks = [DependencyRisk.AtRisk, DependencyRisk.OutOfOrder],
                ShowUnlinked = true
            },
            new FeatureCardFilter
            {
                Search = "retry",
                StrategicObjectives = ["SO-1"],
                StrategicObjectiveNot = true,
                PortfolioEpics = ["PE-1", "PE-2"],
                PortfolioEpicNot = true,
                BusinessOutcomes = ["BO-7"],
                BusinessOutcomeNot = true,
                Statuses = ["In Progress", "Funnel"],
                StatusNot = true,
                RagStatuses = ["Red"],
                RagStatusNot = true,
                Labels = ["pay", "q1"],
                LabelsNot = true,
                RequirementStatuses = ["Ready"],
                RequirementStatusNot = true,
                TechnicalApprovals = ["Approved"],
                TechnicalApprovalNot = true,
                FundingStatuses = ["Funded"],
                FundingStatusNot = true
            });

        var parsed = DependencyMapQuery.Parse(AsQuery(DependencyMapQuery.Write(state)));

        Assert.Equal("Payments", parsed.Art);
        Assert.Equal("PI 26.1", parsed.Pi);
        Assert.Equal("Axis", parsed.Team);
        Assert.Equal("Regal", parsed.From);
        Assert.Equal("(Not in tool)", parsed.To);
        Assert.True(parsed.TableView);
        Assert.Equal("PAY-7", parsed.Focus);
        Assert.Equal(ChainMode.WholeChain, parsed.Chain);
        Assert.False(parsed.IncludePiLabelMatches);

        Assert.Equal(["Blocks", "(unnamed)"], parsed.Filter.LinkTypes);
        Assert.True(parsed.Filter.LinkTypesNot);
        Assert.False(parsed.Filter.ShowOtherPi);
        Assert.False(parsed.Filter.ShowOtherArt);
        Assert.True(parsed.Filter.ShowNotInTool);
        Assert.False(parsed.Filter.ShowWithinTeam);
        Assert.Equal([DependencyRisk.OutOfOrder, DependencyRisk.AtRisk], parsed.Filter.Risks);
        Assert.True(parsed.Filter.ShowUnlinked);
        Assert.Equal(state.Filter.ActiveCount, parsed.Filter.ActiveCount);

        var cards = parsed.FeatureFilter;
        Assert.Equal("retry", cards.Search);
        Assert.Equal(["SO-1"], cards.StrategicObjectives);
        Assert.True(cards.StrategicObjectiveNot);
        Assert.Equal(["PE-1", "PE-2"], cards.PortfolioEpics);
        Assert.True(cards.PortfolioEpicNot);
        Assert.Equal(["BO-7"], cards.BusinessOutcomes);
        Assert.True(cards.BusinessOutcomeNot);
        Assert.Equal(["In Progress", "Funnel"], cards.Statuses);
        Assert.True(cards.StatusNot);
        Assert.Equal(["Red"], cards.RagStatuses);
        Assert.True(cards.RagStatusNot);
        Assert.Equal(["pay", "q1"], cards.Labels);
        Assert.True(cards.LabelsNot);
        Assert.Equal(["Ready"], cards.RequirementStatuses);
        Assert.True(cards.RequirementStatusNot);
        Assert.Equal(["Approved"], cards.TechnicalApprovals);
        Assert.True(cards.TechnicalApprovalNot);
        Assert.Equal(["Funded"], cards.FundingStatuses);
        Assert.True(cards.FundingStatusNot);
        Assert.Equal(state.FeatureFilter.ActiveCount, cards.ActiveCount);
    }

    [Fact]
    public void Non_default_settings_are_written_as_text()
    {
        var state = DefaultState() with { TableView = true, Chain = ChainMode.WholeChain, IncludePiLabelMatches = false };
        state.Filter.ShowOtherPi = false;
        state.Filter.ShowNotInTool = true;
        state.Filter.Risks = [DependencyRisk.NotChecked, DependencyRisk.OutOfOrder];
        state.FeatureFilter.BusinessOutcomeNot = true;

        var written = DependencyMapQuery.Write(state);

        Assert.Equal("table", written["view"]);
        Assert.Equal("whole", written["chain"]);
        Assert.Equal("false", written["piLabels"]);
        Assert.Equal("false", written["otherPi"]);
        Assert.Equal("true", written["notInTool"]);
        Assert.Equal("outoforder,notchecked", written["risk"]);
        Assert.Equal("true", written["boNot"]);
        Assert.Null(written["bo"]);
        Assert.Null(written["otherArt"]);
    }

    [Fact]
    public void Feature_filter_uses_feature_list_key_names()
    {
        var parsed = Parse(
            ("bo", "BO-1,BO-2"),
            ("boNot", "true"),
            ("epic", "PE-1"),
            ("program", "SO-1"),
            ("status", "In Progress"),
            ("ragStatus", "Amber"),
            ("labels", "pay"),
            ("reqStatus", "Ready"),
            ("techApproval", "Approved"),
            ("funding", "Funded"));

        var cards = parsed.FeatureFilter;
        Assert.Equal(["BO-1", "BO-2"], cards.BusinessOutcomes);
        Assert.True(cards.BusinessOutcomeNot);
        Assert.Equal(["PE-1"], cards.PortfolioEpics);
        Assert.Equal(["SO-1"], cards.StrategicObjectives);
        Assert.Equal(["In Progress"], cards.Statuses);
        Assert.Equal(["Amber"], cards.RagStatuses);
        Assert.Equal(["pay"], cards.Labels);
        Assert.Equal(["Ready"], cards.RequirementStatuses);
        Assert.Equal(["Approved"], cards.TechnicalApprovals);
        Assert.Equal(["Funded"], cards.FundingStatuses);
        Assert.False(cards.PortfolioEpicNot);
        Assert.Null(cards.Search);
    }

    [Fact]
    public void Hidden_link_types_round_trip_with_types_not()
    {
        var state = DefaultState();
        state.Filter.LinkTypes = ["Cloners", "Duplicate"];
        state.Filter.LinkTypesNot = true;

        var written = DependencyMapQuery.Write(state);
        var parsed = DependencyMapQuery.Parse(AsQuery(written));

        Assert.Equal("Cloners,Duplicate", written["types"]);
        Assert.Equal("true", written["typesNot"]);
        Assert.Equal(["Cloners", "Duplicate"], parsed.Filter.LinkTypes);
        Assert.True(parsed.Filter.LinkTypesNot);
        Assert.False(parsed.Filter.ShowsLinkType("cloners"));
        Assert.True(parsed.Filter.ShowsLinkType("Blocks"));
    }

    [Fact]
    public void Types_not_is_written_only_with_link_types()
    {
        var state = DefaultState();
        state.Filter.LinkTypesNot = true;

        var written = DependencyMapQuery.Write(state);

        Assert.Null(written["types"]);
        Assert.Null(written["typesNot"]);
    }

    [Theory]
    [InlineData("types", "Cloners", true)]
    [InlineData("typesNot", "true", true)]
    [InlineData("TYPES", "Blocks", true)]
    [InlineData("types", " ", false)]
    [InlineData("otherPi", "false", false)]
    public void Link_type_keys_count_only_with_a_value(string key, string value, bool expected)
    {
        Assert.Equal(expected, DependencyMapQuery.HasLinkTypeKeys(new Dictionary<string, string?> { [key] = value }));
    }

    [Fact]
    public void Link_type_preference_round_trips()
    {
        var filter = new DependencyFilter { LinkTypes = ["Cloners", " cloners ", "Relates"], LinkTypesNot = true };
        var restored = new DependencyFilter();

        var stored = DependencyMapQuery.WriteLinkTypePreference(filter);

        Assert.True(DependencyMapQuery.ReadLinkTypePreference(stored, restored));
        Assert.Equal(["Cloners", "Relates"], restored.LinkTypes);
        Assert.True(restored.LinkTypesNot);
    }

    [Fact]
    public void No_link_type_filter_stores_nothing()
    {
        Assert.Null(DependencyMapQuery.WriteLinkTypePreference(new DependencyFilter { LinkTypesNot = true }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"types\":[],\"not\":true}")]
    [InlineData("{\"types\":[\" \"]}")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void Unusable_link_type_preferences_leave_the_filter_alone(string? stored)
    {
        var filter = new DependencyFilter { LinkTypes = ["Blocks"] };

        Assert.False(DependencyMapQuery.ReadLinkTypePreference(stored, filter));
        Assert.Equal(["Blocks"], filter.LinkTypes);
        Assert.False(filter.LinkTypesNot);
    }

    [Fact]
    public void Not_flags_are_kept_without_values()
    {
        var state = DefaultState();
        state.FeatureFilter.StrategicObjectiveNot = true;

        var written = DependencyMapQuery.Write(state);
        var parsed = DependencyMapQuery.Parse(AsQuery(written));

        Assert.Null(written["program"]);
        Assert.Equal("true", written["programNot"]);
        Assert.Empty(parsed.FeatureFilter.StrategicObjectives);
        Assert.True(parsed.FeatureFilter.StrategicObjectiveNot);
        Assert.Equal(1, parsed.FeatureFilter.ActiveCount);
    }

    [Fact]
    public void Unknown_risk_tokens_and_bad_booleans_are_ignored()
    {
        var parsed = Parse(
            ("risk", "OutOfOrder,late,atrisk,"),
            ("otherPi", "maybe"),
            ("notInTool", "yes"),
            ("withinTeam", "0"),
            ("unlinked", ""),
            ("piLabels", "no"),
            ("view", "grid"),
            ("chain", "all"),
            ("boNot", "1"));

        Assert.Equal([DependencyRisk.OutOfOrder, DependencyRisk.AtRisk], parsed.Filter.Risks);
        Assert.True(parsed.Filter.ShowOtherPi);
        Assert.False(parsed.Filter.ShowNotInTool);
        Assert.True(parsed.Filter.ShowWithinTeam);
        Assert.False(parsed.Filter.ShowUnlinked);
        Assert.True(parsed.IncludePiLabelMatches);
        Assert.False(parsed.TableView);
        Assert.Equal(ChainMode.Direct, parsed.Chain);
        Assert.False(parsed.FeatureFilter.BusinessOutcomeNot);
    }

    [Fact]
    public void Booleans_and_tokens_ignore_case_and_spaces()
    {
        var parsed = Parse(("otherArt", " FALSE "), ("unlinked", "True"), ("view", "Table"), ("chain", "WHOLE"));

        Assert.False(parsed.Filter.ShowOtherArt);
        Assert.True(parsed.Filter.ShowUnlinked);
        Assert.True(parsed.TableView);
        Assert.Equal(ChainMode.WholeChain, parsed.Chain);
    }

    [Fact]
    public void All_four_risks_mean_no_risk_filter()
    {
        var parsed = Parse(("risk", "notchecked,inorder,atrisk,outoforder"));
        var state = DefaultState();
        state.Filter.Risks = [DependencyRisk.OutOfOrder, DependencyRisk.AtRisk, DependencyRisk.InOrder, DependencyRisk.NotChecked];

        Assert.Empty(parsed.Filter.Risks);
        Assert.Null(DependencyMapQuery.Write(state)["risk"]);
    }

    [Fact]
    public void Comma_lists_are_trimmed_and_deduplicated()
    {
        var parsed = Parse(("types", " Blocks , blocks,,Relates "), ("labels", "pay, PAY ,q1"), ("search", "  retry  "));
        var state = DefaultState();
        state.Filter.LinkTypes = [" Blocks", "blocks", "Relates ", " "];
        state.FeatureFilter.Statuses = ["Done", " done ", "In Progress"];
        state.FeatureFilter.Search = "   ";

        var written = DependencyMapQuery.Write(state);

        Assert.Equal(["Blocks", "Relates"], parsed.Filter.LinkTypes);
        Assert.Equal(["pay", "q1"], parsed.FeatureFilter.Labels);
        Assert.Equal("retry", parsed.FeatureFilter.Search);
        Assert.Equal("Blocks,Relates", written["types"]);
        Assert.Equal("Done,In Progress", written["status"]);
        Assert.Null(written["search"]);
    }

    [Fact]
    public void Scope_values_are_trimmed_and_blank_values_are_dropped()
    {
        var parsed = Parse(("cp", " Payments "), ("pi", "  "), ("team", "Axis"), ("focus", " PAY-7"));
        var written = DependencyMapQuery.Write(DefaultState() with { Art = "  Payments ", Pi = " ", Focus = "" });

        Assert.Equal("Payments", parsed.Art);
        Assert.Null(parsed.Pi);
        Assert.Equal("Axis", parsed.Team);
        Assert.Equal("PAY-7", parsed.Focus);
        Assert.Null(parsed.From);
        Assert.Equal("Payments", written["cp"]);
        Assert.Null(written["pi"]);
        Assert.Null(written["focus"]);
    }

    [Fact]
    public void Query_keys_are_read_ignoring_case()
    {
        var query = new Dictionary<string, string?> { ["OTHERPI"] = "false", ["Cp"] = "Payments", ["boNOT"] = "true" };

        var parsed = DependencyMapQuery.Parse(query);

        Assert.False(parsed.Filter.ShowOtherPi);
        Assert.Equal("Payments", parsed.Art);
        Assert.True(parsed.FeatureFilter.BusinessOutcomeNot);
        Assert.True(DependencyMapQuery.HasFilterKeys(query));
    }

    [Fact]
    public void Missing_keys_give_the_defaults()
    {
        var parsed = DependencyMapQuery.Parse(new Dictionary<string, string?>());

        Assert.Null(parsed.Art);
        Assert.False(parsed.TableView);
        Assert.Equal(ChainMode.Direct, parsed.Chain);
        Assert.True(parsed.IncludePiLabelMatches);
        Assert.Equal(0, parsed.Filter.ActiveCount);
        Assert.Equal(0, parsed.FeatureFilter.ActiveCount);
    }

    [Fact]
    public void List_values_with_commas_and_backslashes_round_trip_for_every_list_key()
    {
        string[] values = ["Unfunded, deferred", @"C:\temp", @"a\,b", "plain", @"ends\"];
        var state = DefaultState();
        state.Filter.LinkTypes = values;
        state.FeatureFilter.StrategicObjectives = values;
        state.FeatureFilter.PortfolioEpics = values;
        state.FeatureFilter.BusinessOutcomes = values;
        state.FeatureFilter.Statuses = values;
        state.FeatureFilter.RagStatuses = values;
        state.FeatureFilter.Labels = values;
        state.FeatureFilter.RequirementStatuses = values;
        state.FeatureFilter.TechnicalApprovals = values;
        state.FeatureFilter.FundingStatuses = values;

        var written = DependencyMapQuery.Write(state);
        var parsed = DependencyMapQuery.Parse(AsQuery(written));

        Assert.Equal(@"Unfunded\, deferred,C:\\temp,a\\\,b,plain,ends\\", written["funding"]);
        Assert.Equal(values, parsed.Filter.LinkTypes);
        Assert.Equal(values, parsed.FeatureFilter.StrategicObjectives);
        Assert.Equal(values, parsed.FeatureFilter.PortfolioEpics);
        Assert.Equal(values, parsed.FeatureFilter.BusinessOutcomes);
        Assert.Equal(values, parsed.FeatureFilter.Statuses);
        Assert.Equal(values, parsed.FeatureFilter.RagStatuses);
        Assert.Equal(values, parsed.FeatureFilter.Labels);
        Assert.Equal(values, parsed.FeatureFilter.RequirementStatuses);
        Assert.Equal(values, parsed.FeatureFilter.TechnicalApprovals);
        Assert.Equal(values, parsed.FeatureFilter.FundingStatuses);
    }

    [Fact]
    public void Lists_without_escapes_and_lone_backslashes_read_as_before()
    {
        var parsed = Parse(("labels", @"pay,q1\x,\"), ("funding", "Unfunded, deferred"));

        Assert.Equal(["pay", @"q1\x", @"\"], parsed.FeatureFilter.Labels);
        Assert.Equal(["Unfunded", "deferred"], parsed.FeatureFilter.FundingStatuses);
    }

    [Fact]
    public void A_url_with_a_scope_keeps_no_team_and_the_board_view()
    {
        var withScope = new Dictionary<string, string?> { ["cp"] = "Payments", ["pi"] = "PI 26.1" };
        var artOnly = new Dictionary<string, string?> { ["CP"] = "Payments" };
        var piOnly = new Dictionary<string, string?> { ["pi"] = "PI 26.1" };

        Assert.True(DependencyMapQuery.HasScopeKeys(withScope));
        Assert.True(DependencyMapQuery.HasScopeKeys(artOnly));
        Assert.True(DependencyMapQuery.HasScopeKeys(piOnly));
        Assert.False(DependencyMapQuery.UsesStoredTeam(withScope));
        Assert.False(DependencyMapQuery.UsesStoredView(withScope));
        Assert.False(DependencyMapQuery.UsesStoredTeam(artOnly));
        Assert.False(DependencyMapQuery.UsesStoredView(piOnly));
        Assert.False(DependencyMapQuery.Parse(withScope).TableView);
        Assert.Null(DependencyMapQuery.Parse(withScope).Team);
    }

    [Fact]
    public void A_url_without_a_scope_falls_back_to_the_stored_team_and_view()
    {
        var empty = new Dictionary<string, string?>();
        var blankScope = new Dictionary<string, string?> { ["cp"] = " ", ["pi"] = "" };
        var withView = new Dictionary<string, string?> { ["view"] = "table", ["team"] = "Axis" };

        Assert.False(DependencyMapQuery.HasScopeKeys(empty));
        Assert.False(DependencyMapQuery.HasScopeKeys(blankScope));
        Assert.True(DependencyMapQuery.UsesStoredTeam(empty));
        Assert.True(DependencyMapQuery.UsesStoredView(empty));
        Assert.True(DependencyMapQuery.UsesStoredView(blankScope));
        Assert.True(DependencyMapQuery.UsesStoredTeam(withView));
        Assert.False(DependencyMapQuery.UsesStoredView(withView));
    }

    [Fact]
    public void Has_filter_keys_ignores_scope_team_selection_focus_and_view_keys()
    {
        var scopeOnly = new Dictionary<string, string?>
        {
            ["cp"] = "Payments",
            ["pi"] = "PI 26.1",
            ["team"] = "Axis",
            ["from"] = "Axis",
            ["to"] = "Regal",
            ["view"] = "table",
            ["focus"] = "PAY-1",
            ["chain"] = "whole",
            ["piLabels"] = "false",
            ["returnUrl"] = "/x"
        };

        Assert.False(DependencyMapQuery.HasFilterKeys(scopeOnly));
        Assert.DoesNotContain("cp", DependencyMapQuery.FilterKeys);
        Assert.DoesNotContain("piLabels", DependencyMapQuery.FilterKeys);

        foreach (var key in DependencyMapQuery.FilterKeys)
        {
            var query = new Dictionary<string, string?>(scopeOnly) { [key] = "true" };
            Assert.True(DependencyMapQuery.HasFilterKeys(query), key);
        }
    }
}
