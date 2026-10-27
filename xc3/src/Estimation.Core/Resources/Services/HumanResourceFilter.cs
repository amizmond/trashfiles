using System.Linq.Expressions;
using Estimation.Core.Resources.Models;
using Microsoft.EntityFrameworkCore.Query;

namespace Estimation.Core.Resources.Services;

public sealed class HumanResourceFilter
{
    public string? Search { get; init; }
    public IReadOnlyCollection<string>? SkillNames { get; init; }
    public bool AllSkills { get; init; }
    public bool? IsActive { get; init; }
    public IReadOnlyCollection<string>? TeamNames { get; init; }
    public bool TeamNot { get; init; }
    public IReadOnlyCollection<string>? TeamRoleNames { get; init; }
    public IReadOnlyCollection<string>? ArtNames { get; init; }
    public IReadOnlyCollection<string>? DepartmentNames { get; init; }
    public IReadOnlyCollection<string>? EmployeeCategoryNames { get; init; }
    public bool EmployeeCategoryNot { get; init; }
    public IReadOnlyCollection<string>? EmployeeTypeNames { get; init; }
    public bool EmployeeTypeNot { get; init; }
    public IReadOnlyCollection<string>? EmployeeRoleNames { get; init; }
    public bool EmployeeRoleNot { get; init; }
    public IReadOnlyCollection<string>? EmployeeVendorNames { get; init; }
    public bool EmployeeVendorNot { get; init; }
    public IReadOnlyCollection<string>? CorporateGradeNames { get; init; }
    public bool CorporateGradeNot { get; init; }
    public IReadOnlyCollection<string>? CountryNames { get; init; }
    public bool CountryNot { get; init; }
    public IReadOnlyCollection<string>? CityNames { get; init; }
    public bool CityNot { get; init; }
    public IReadOnlyCollection<int>? AllowedArtIds { get; init; }

    public bool HasConditions =>
        !string.IsNullOrWhiteSpace(Search)
        || IsActive.HasValue
        || SkillNames is { Count: > 0 }
        || TeamNames is { Count: > 0 }
        || TeamNot
        || TeamRoleNames is { Count: > 0 }
        || ArtNames is { Count: > 0 }
        || DepartmentNames is { Count: > 0 }
        || EmployeeCategoryNames is { Count: > 0 } || EmployeeCategoryNot
        || EmployeeTypeNames is { Count: > 0 } || EmployeeTypeNot
        || EmployeeRoleNames is { Count: > 0 } || EmployeeRoleNot
        || EmployeeVendorNames is { Count: > 0 } || EmployeeVendorNot
        || CorporateGradeNames is { Count: > 0 } || CorporateGradeNot
        || CountryNames is { Count: > 0 } || CountryNot
        || CityNames is { Count: > 0 } || CityNot;

    public IQueryable<HumanResource> Apply(IQueryable<HumanResource> query, IQueryable<TeamMember> teamMembers) =>
        ApplyConditions(ApplyScope(query), teamMembers);

    public IQueryable<HumanResource> ApplyScope(IQueryable<HumanResource> query)
    {
        if (AllowedArtIds is not { } allowed)
        {
            return query;
        }

        return query.Where(hr => hr.TeamMembers.Any(tm =>
            tm.Team.CapitalProjectTeams.Any(cpt => allowed.Contains(cpt.CapitalProjectId))));
    }

    public IQueryable<HumanResource> ApplyConditions(IQueryable<HumanResource> query, IQueryable<TeamMember> teamMembers)
    {
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(hr =>
                hr.FullName.Contains(term) ||
                hr.EmployeeName.Contains(term) ||
                (hr.EmployeeNumber != null && hr.EmployeeNumber.Contains(term)));
        }

        if (IsActive is { } isActive)
        {
            query = query.Where(hr => hr.IsActive == isActive);
        }

        if (SkillNames is { Count: > 0 })
        {
            var skillNames = SkillNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (AllSkills)
            {
                var required = skillNames.Length;
                query = query.Where(hr => hr.HumanResourceSkills
                    .Where(hrs => skillNames.Contains(hrs.Skill.Name))
                    .Select(hrs => hrs.Skill.Name)
                    .Distinct()
                    .Count() == required);
            }
            else
            {
                query = query.Where(hr => hr.HumanResourceSkills.Any(hrs => skillNames.Contains(hrs.Skill.Name)));
            }
        }

        if (TeamNot)
        {
            if (TeamNames is { Count: > 0 } excludedTeams)
            {
                query = query.Where(hr => !hr.TeamMembers.Any(tm => excludedTeams.Contains(tm.Team.Name)));
            }
            else
            {
                query = query.Where(hr => !hr.TeamMembers.Any());
            }
        }

        var memberships = teamMembers;
        var byMembership = false;

        if (!TeamNot && TeamNames is { Count: > 0 } teamNames)
        {
            memberships = memberships.Where(tm => teamNames.Contains(tm.Team.Name));
            byMembership = true;
        }

        if (TeamRoleNames is { Count: > 0 } roleNames)
        {
            memberships = memberships.Where(tm => tm.TeamRole != null && roleNames.Contains(tm.TeamRole.Name));
            byMembership = true;
        }

        if (ArtNames is { Count: > 0 } artNames)
        {
            memberships = memberships.Where(tm => tm.Team.CapitalProjectTeams.Any(cpt => artNames.Contains(cpt.Art.Name)));
            byMembership = true;
        }

        if (DepartmentNames is { Count: > 0 } departmentNames)
        {
            memberships = memberships.Where(tm => tm.Team.CapitalProjectTeams.Any(cpt =>
                cpt.Art.Department != null && departmentNames.Contains(cpt.Art.Department.Name)));
            byMembership = true;
        }

        if (byMembership)
        {
            query = query.Where(hr => memberships.Any(tm => tm.HumanResourceId == hr.Id));
        }

        query = ByValue(query, hr => hr.EmployeeCategory != null ? hr.EmployeeCategory.Name : null, EmployeeCategoryNames, EmployeeCategoryNot);
        query = ByValue(query, hr => hr.EmployeeType != null ? hr.EmployeeType.Name : null, EmployeeTypeNames, EmployeeTypeNot);
        query = ByValue(query, hr => hr.EmployeeRole != null ? hr.EmployeeRole.Name : null, EmployeeRoleNames, EmployeeRoleNot);
        query = ByValue(query, hr => hr.EmployeeVendor != null ? hr.EmployeeVendor.Name : null, EmployeeVendorNames, EmployeeVendorNot);
        query = ByValue(query, hr => hr.CorporateGrade != null ? hr.CorporateGrade.Name : null, CorporateGradeNames, CorporateGradeNot);
        query = ByValue(query, hr => hr.City != null ? hr.City.Country.Name : null, CountryNames, CountryNot);
        query = ByValue(query, hr => hr.City != null ? hr.City.Name : null, CityNames, CityNot);

        return query;
    }

    private static IQueryable<HumanResource> ByValue(
        IQueryable<HumanResource> query,
        Expression<Func<HumanResource, string?>> value,
        IReadOnlyCollection<string>? names,
        bool not)
    {
        var isNull = Expression.Equal(value.Body, Expression.Constant(null, typeof(string)));
        Expression condition;

        if (names is { Count: > 0 })
        {
            Expression<Func<string?, bool>> listed = v => names.Contains(v!);
            var contains = ReplacingExpressionVisitor.Replace(listed.Parameters[0], value.Body, listed.Body);
            condition = not
                ? Expression.OrElse(isNull, Expression.Not(contains))
                : Expression.AndAlso(Expression.Not(isNull), contains);
        }
        else if (not)
        {
            condition = isNull;
        }
        else
        {
            return query;
        }

        return query.Where(Expression.Lambda<Func<HumanResource, bool>>(condition, value.Parameters));
    }
}
