using Estimation.Core.Resources.Models;

namespace Estimation.Services.Shared;

public static class DepartmentArts
{
    public static Dictionary<string, string> ByArtName(IEnumerable<Department> departments) =>
        departments
            .SelectMany(d => d.CapitalProjects.Select(art => (Art: art.Name, Department: d.Name)))
            .GroupBy(x => x.Art, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Department, StringComparer.OrdinalIgnoreCase);

    public static List<string> InDepartments(
        IEnumerable<string> artNames,
        IReadOnlyDictionary<string, string> departmentByArt,
        IReadOnlySet<string> departments) =>
        departments.Count == 0
            ? artNames.ToList()
            : artNames
                .Where(art => departmentByArt.TryGetValue(art, out var department) && departments.Contains(department))
                .ToList();
}
