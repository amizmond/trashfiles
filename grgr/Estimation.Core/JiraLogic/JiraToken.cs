using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.JiraLogic;

public class JiraToken
{
    public int Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string UserName { get; set; } = null!;

    [Required]
    [MaxLength(2000)]
    public string AccessToken { get; set; } = null!;

    [Required]
    [MaxLength(2000)]
    public string AccessTokenSecret { get; set; } = null!;

    public DateTime Created { get; set; }

    public DateTime? Updated { get; set; }
}
