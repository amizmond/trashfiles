using Estimation.Core.Models;

namespace Estimation.Services;

public static class TeamMemberUploadPreviewState
{
    public static List<TeamMemberUploadRow>? PendingUpload { get; set; }
    public static int TeamId { get; set; }
    public static string? TeamName { get; set; }

    public static void Clear()
    {
        PendingUpload = null;
        TeamId = 0;
        TeamName = null;
    }
}
