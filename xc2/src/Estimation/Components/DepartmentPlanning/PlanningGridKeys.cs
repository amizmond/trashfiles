using Microsoft.AspNetCore.Components.Web;

namespace Estimation.Components.DepartmentPlanning;

public static class PlanningGridKeys
{
    public static bool Handles(KeyboardEventArgs e) =>
        !e.AltKey && !e.CtrlKey && !e.MetaKey
        && !(e.ShiftKey && e.Key is "ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight" or "Home" or "End");

    public static int PositionOf(IReadOnlyList<string> visibleIds, string? selectedId)
    {
        var id = selectedId;
        while (id is not null)
        {
            for (var i = 0; i < visibleIds.Count; i++)
            {
                if (visibleIds[i] == id)
                {
                    return i;
                }
            }

            var slash = id.LastIndexOf('/');
            id = slash > 0 ? id[..slash] : null;
        }

        return -1;
    }
}
