using System.Globalization;
using Estimation.Core.DepartmentPlanning.Services;
using Microsoft.AspNetCore.WebUtilities;

namespace Estimation.Components.DepartmentPlanning;

public sealed record PlanningUrlState(
    bool HasScope,
    bool HasDepartment,
    int? DepartmentId,
    int? PiId,
    PlanningLens? Lens,
    string? ViewKey,
    int? Level,
    bool? IncludeIp,
    IReadOnlyCollection<int> ArtIds);

public static class PlanningUrl
{
    public static PlanningUrlState Read(string uri)
    {
        var questionMark = uri.IndexOf('?');
        var query = QueryHelpers.ParseQuery(questionMark < 0 ? string.Empty : uri[questionMark..].Split('#')[0]);
        string? Value(string key) => query.TryGetValue(key, out var value) ? value.ToString() : null;

        var dept = Value("dept");
        var pi = Value("pi");
        var lens = Value("lens");
        var level = Value("level");
        var ip = Value("ip");
        return new PlanningUrlState(
            dept is not null || pi is not null,
            dept is not null,
            int.TryParse(dept, NumberStyles.Integer, CultureInfo.InvariantCulture, out var departmentId) ? departmentId : null,
            int.TryParse(pi, NumberStyles.Integer, CultureInfo.InvariantCulture, out var piId) ? piId : null,
            string.Equals(lens, Drawdown, StringComparison.OrdinalIgnoreCase) ? PlanningLens.Drawdown
                : string.Equals(lens, Objectives, StringComparison.OrdinalIgnoreCase) ? PlanningLens.Objectives
                : null,
            Value("view"),
            int.TryParse(level, NumberStyles.Integer, CultureInfo.InvariantCulture, out var levelValue) ? Math.Clamp(levelValue, 0, PlanningPivot.MaxStartLevel) : null,
            ip is null ? null : ip == "1",
            (Value("arts") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (int?)null)
                .OfType<int>()
                .ToHashSet());
    }

    public const string AllDepartments = "all";
    public const string Objectives = "objectives";
    public const string Drawdown = "drawdown";

    public static Dictionary<string, object?> Parameters(
        int? departmentId,
        int? piId,
        PlanningLens lens,
        string viewKey,
        int level,
        bool includeIp,
        IReadOnlyCollection<int> artIds) => new()
    {
        ["dept"] = departmentId?.ToString(CultureInfo.InvariantCulture) ?? AllDepartments,
        ["pi"] = piId,
        ["lens"] = lens == PlanningLens.Drawdown ? Drawdown : Objectives,
        ["view"] = viewKey,
        ["level"] = level,
        ["ip"] = includeIp ? 1 : 0,
        ["arts"] = artIds.Count == 0 ? null : string.Join(",", artIds.Order()),
    };
}
