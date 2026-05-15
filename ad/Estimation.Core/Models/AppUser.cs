using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.Models;

public class AppUser
{
    public int Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string WindowsUserName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? DisplayName { get; set; }

    [MaxLength(256)]
    public string? SamAccountName { get; set; }

    [MaxLength(50)]
    public string? EmployeeId { get; set; }

    public bool IsAdmin { get; set; }

    public bool IsApproved { get; set; }

    public bool IsAccessRequested { get; set; }

    public DateTime? RequestedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    [MaxLength(256)]
    public string? ApprovedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual IList<AppUserPagePermission> PagePermissions { get; set; } = [];
}
