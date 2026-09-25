using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Estimation.Core.Train.Services;

namespace Estimation.Core.Train.Models;

public class ArtJiraKey
{
    public const int MaxKeyLength = 10;

    public const int MaxFilterLength = 4000;

    public int Id { get; set; }

    public int CapitalProjectId { get; set; }

    public virtual Art Art { get; set; } = null!;

    [Required]
    [MaxLength(MaxKeyLength)]
    public string JiraKey { get; set; } = null!;

    [MaxLength(MaxFilterLength)]
    public string? Components { get; set; }

    [MaxLength(MaxFilterLength)]
    public string? Labels { get; set; }

    [NotMapped]
    public string Description => Describe(JiraKey, JiraListValues.Parse(Components), JiraListValues.Parse(Labels));

    public static string Describe(string jiraKey, IReadOnlyCollection<string> components, IReadOnlyCollection<string> labels)
    {
        var filters = DescribeFilters(components, labels);
        return filters.Length == 0 ? jiraKey : $"{jiraKey} [{filters}]";
    }

    public static string DescribeFilters(IEnumerable<string> components, IEnumerable<string> labels)
    {
        var parts = new List<string>();
        var componentFilter = JiraValueFilter.Parse(components);
        if (componentFilter.IsActive)
        {
            parts.Add($"components: {componentFilter.Describe()}");
        }
        var labelFilter = JiraValueFilter.Parse(labels);
        if (labelFilter.IsActive)
        {
            parts.Add($"labels: {labelFilter.Describe()}");
        }

        return string.Join("; ", parts);
    }
}
