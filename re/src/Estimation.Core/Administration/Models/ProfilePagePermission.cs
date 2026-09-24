using Estimation.Core.Train.Models;

namespace Estimation.Core.Administration.Models;

public class ProfilePagePermission
{
    public int Id { get; set; }

    public int ProfileId { get; set; }

    public int AppPageId { get; set; }

    public int? CapitalProjectId { get; set; }

    public AccessLevel AccessLevel { get; set; }

    public virtual Profile Profile { get; set; } = null!;

    public virtual AppPage AppPage { get; set; } = null!;

    public virtual Art? Art { get; set; }
}
