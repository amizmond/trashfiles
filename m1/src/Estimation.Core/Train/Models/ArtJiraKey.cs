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
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Components) || !string.IsNullOrWhiteSpace(Labels);

    [NotMapped]
    public string Description => Describe(JiraKey, JiraListValues.Parse(Components), JiraListValues.Parse(Labels));

    public static string Describe(string jiraKey, IReadOnlyCollection<string> components, IReadOnlyCollection<string> labels)
    {
        var parts = new List<string>();
        if (components.Count > 0)
        {
            parts.Add($"components: {string.Join(", ", components)}");
        }
        if (labels.Count > 0)
        {
            parts.Add($"labels: {string.Join(", ", labels)}");
        }

        return parts.Count == 0 ? jiraKey : $"{jiraKey} [{string.Join("; ", parts)}]";
    }
}
