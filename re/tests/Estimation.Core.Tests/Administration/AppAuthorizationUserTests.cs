using Estimation.Core.Administration.Models;
using Estimation.Core.Administration.Services;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Administration;

public class AppAuthorizationUserTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly AppAuthorizationService _service;

    public AppAuthorizationUserTests()
    {
        _service = new AppAuthorizationService(_db);
    }

    private static AppUser User(
        int id,
        string windowsUserName,
        bool approved = true,
        bool admin = false,
        bool requested = false,
        DateTime? requestedAt = null) => new()
    {
        Id = id,
        WindowsUserName = windowsUserName,
        IsApproved = approved,
        IsAdmin = admin,
        IsAccessRequested = requested,
        RequestedAt = requestedAt,
        CreatedAt = new DateTime(2026, 1, 1)
    };

    private Task<AppUser?> LoadAsync(string windowsUserName) =>
        _db.ReadAsync(db => db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.WindowsUserName == windowsUserName));

    [Fact]
    public async Task The_current_user_is_found_by_windows_login()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada")));

        var user = await _service.GetCurrentUserAsync("DOMAIN\\ada");

        Assert.NotNull(user);
        Assert.Equal(1, user!.Id);
    }

    [Fact]
    public async Task An_unknown_login_has_no_current_user()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada")));

        Assert.Null(await _service.GetCurrentUserAsync("DOMAIN\\stranger"));
    }

    [Fact]
    public async Task A_user_awaiting_approval_is_still_returned_as_the_current_user()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada", approved: false, requested: true)));

        Assert.NotNull(await _service.GetCurrentUserAsync("DOMAIN\\ada"));
    }

    [Fact]
    public async Task Only_an_approved_user_is_authorized()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\approved"));
            db.AppUsers.Add(User(2, "DOMAIN\\pending", approved: false));
        });

        Assert.True(await _service.IsAuthorizedAsync("DOMAIN\\approved"));
        Assert.False(await _service.IsAuthorizedAsync("DOMAIN\\pending"));
        Assert.False(await _service.IsAuthorizedAsync("DOMAIN\\stranger"));
    }

    [Fact]
    public async Task Only_an_approved_admin_counts_as_an_admin()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\admin", admin: true));
            db.AppUsers.Add(User(2, "DOMAIN\\user"));
            db.AppUsers.Add(User(3, "DOMAIN\\revoked", approved: false, admin: true));
        });

        Assert.True(await _service.IsAdminAsync("DOMAIN\\admin"));
        Assert.False(await _service.IsAdminAsync("DOMAIN\\user"));
        Assert.False(await _service.IsAdminAsync("DOMAIN\\revoked"));
    }

    [Fact]
    public async Task A_first_time_visitor_creates_an_access_request()
    {
        await _service.RequestAccessAsync("DOMAIN\\ada", "Ada Lovelace", "alovelace", "E12345", "ada@example.test");

        var user = await LoadAsync("DOMAIN\\ada");

        Assert.NotNull(user);
        Assert.True(user!.IsAccessRequested);
        Assert.False(user.IsApproved);
        Assert.NotNull(user.RequestedAt);
        Assert.Equal("Ada Lovelace", user.DisplayName);
        Assert.Equal("alovelace", user.SamAccountName);
        Assert.Equal("E12345", user.EmployeeId);
        Assert.Equal("ada@example.test", user.EmailAddress);
    }

    [Fact]
    public async Task A_returning_unapproved_visitor_has_their_request_reinstated()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada", approved: false)));

        await _service.RequestAccessAsync("DOMAIN\\ada");

        var user = await LoadAsync("DOMAIN\\ada");

        Assert.True(user!.IsAccessRequested);
        Assert.NotNull(user.RequestedAt);
    }

    [Fact]
    public async Task An_already_approved_user_is_not_pushed_back_into_the_request_queue()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada")));

        await _service.RequestAccessAsync("DOMAIN\\ada");

        var user = await LoadAsync("DOMAIN\\ada");

        Assert.False(user!.IsAccessRequested);
        Assert.True(user.IsApproved);
    }

    [Fact]
    public async Task A_pending_request_keeps_its_original_timestamp()
    {
        var originallyRequested = new DateTime(2026, 2, 1);
        await _db.SeedAsync(db => db.AppUsers.Add(
            User(1, "DOMAIN\\ada", approved: false, requested: true, requestedAt: originallyRequested)));

        await _service.RequestAccessAsync("DOMAIN\\ada");

        Assert.Equal(originallyRequested, (await LoadAsync("DOMAIN\\ada"))!.RequestedAt);
    }

    [Fact]
    public async Task Requesting_access_again_refreshes_the_directory_details()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada", approved: false)));

        await _service.RequestAccessAsync("DOMAIN\\ada", "Ada Lovelace", employeeId: "E12345");

        var user = await LoadAsync("DOMAIN\\ada");

        Assert.Equal("Ada Lovelace", user!.DisplayName);
        Assert.Equal("E12345", user.EmployeeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_directory_details_never_overwrite_what_is_already_known(string? blank)
    {
        await _db.SeedAsync(db =>
        {
            var user = User(1, "DOMAIN\\ada", approved: false);
            user.DisplayName = "Ada Lovelace";
            user.SamAccountName = "alovelace";
            user.EmployeeId = "E12345";
            user.EmailAddress = "ada@example.test";
            db.AppUsers.Add(user);
        });

        await _service.RequestAccessAsync("DOMAIN\\ada", blank, blank, blank, blank);

        var reloaded = await LoadAsync("DOMAIN\\ada");

        Assert.Equal("Ada Lovelace", reloaded!.DisplayName);
        Assert.Equal("alovelace", reloaded.SamAccountName);
        Assert.Equal("E12345", reloaded.EmployeeId);
        Assert.Equal("ada@example.test", reloaded.EmailAddress);
    }

    [Fact]
    public async Task Pending_requests_are_those_asked_for_but_not_yet_granted()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\waiting", approved: false, requested: true, requestedAt: new DateTime(2026, 3, 1)));
            db.AppUsers.Add(User(2, "DOMAIN\\granted", requested: false));
            db.AppUsers.Add(User(3, "DOMAIN\\never-asked", approved: false));
        });

        var pending = await _service.GetPendingRequestsAsync();

        Assert.Equal(new[] { "DOMAIN\\waiting" }, pending.Select(r => r.User.WindowsUserName));
        Assert.True(await _service.HasPendingRequestsAsync());
    }

    [Fact]
    public async Task Pending_requests_are_listed_oldest_first()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\second", approved: false, requested: true, requestedAt: new DateTime(2026, 3, 2)));
            db.AppUsers.Add(User(2, "DOMAIN\\first", approved: false, requested: true, requestedAt: new DateTime(2026, 3, 1)));
        });

        var pending = await _service.GetPendingRequestsAsync();

        Assert.Equal(new[] { "DOMAIN\\first", "DOMAIN\\second" }, pending.Select(r => r.User.WindowsUserName));
    }

    [Fact]
    public async Task With_nothing_outstanding_there_are_no_pending_requests()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\granted")));

        Assert.False(await _service.HasPendingRequestsAsync());
        Assert.Empty(await _service.GetPendingRequestsAsync());
    }

    [Fact]
    public async Task All_users_are_listed_alphabetically_by_login()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\zoe"));
            db.AppUsers.Add(User(2, "DOMAIN\\ada"));
            db.AppUsers.Add(User(3, "DOMAIN\\mia"));
        });

        var users = await _service.GetAllUsersAsync();

        Assert.Equal(new[] { "DOMAIN\\ada", "DOMAIN\\mia", "DOMAIN\\zoe" }, users.Select(u => u.WindowsUserName));
    }

    [Fact]
    public async Task A_user_can_be_fetched_by_id()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(7, "DOMAIN\\ada")));

        Assert.Equal("DOMAIN\\ada", (await _service.GetUserByIdAsync(7))!.WindowsUserName);
        Assert.Null(await _service.GetUserByIdAsync(999));
    }

    [Fact]
    public async Task Pages_are_listed_in_their_configured_order()
    {
        await _db.SeedAsync(db =>
        {
            db.AppPages.Add(new AppPage { Id = 1, Key = "b", DisplayName = "B", SortOrder = 20 });
            db.AppPages.Add(new AppPage { Id = 2, Key = "a", DisplayName = "A", SortOrder = 10 });
        });

        Assert.Equal(new[] { "a", "b" }, (await _service.GetAllPagesAsync()).Select(p => p.Key));
    }

    [Fact]
    public async Task The_profiles_assigned_to_a_user_can_be_listed()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\ada"));
            db.Profiles.Add(new Profile { Id = 10, Name = "Planner" });
            db.Profiles.Add(new Profile { Id = 11, Name = "Viewer" });
            db.AppUserProfiles.Add(new AppUserProfile { AppUserId = 1, ProfileId = 10 });
            db.AppUserProfiles.Add(new AppUserProfile { AppUserId = 1, ProfileId = 11 });
        });

        Assert.Equal(new[] { 10, 11 }, (await _service.GetUserProfileIdsAsync(1)).OrderBy(i => i));
    }

    [Fact]
    public async Task Approving_a_user_grants_access_and_records_who_did_it()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\ada", approved: false, requested: true));
            db.Profiles.Add(new Profile { Id = 10, Name = "Planner" });
        });

        await _service.ApproveUserAsync(1, isAdmin: false, new List<int> { 10 }, "DOMAIN\\admin");

        var user = await LoadAsync("DOMAIN\\ada");

        Assert.True(user!.IsApproved);
        Assert.False(user.IsAccessRequested);
        Assert.False(user.IsAdmin);
        Assert.NotNull(user.ApprovedAt);
        Assert.Equal("DOMAIN\\admin", user.ApprovedBy);
        Assert.Equal(new[] { 10 }, await _service.GetUserProfileIdsAsync(1));
    }

    [Fact]
    public async Task Approving_a_user_as_an_admin_sets_the_admin_flag()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada", approved: false)));

        await _service.ApproveUserAsync(1, isAdmin: true, new List<int>(), "DOMAIN\\admin");

        Assert.True((await LoadAsync("DOMAIN\\ada"))!.IsAdmin);
    }

    [Fact]
    public async Task Approving_an_unknown_user_does_nothing()
    {
        await _service.ApproveUserAsync(999, isAdmin: true, new List<int> { 10 }, "DOMAIN\\admin");

        Assert.Empty(await _service.GetAllUsersAsync());
    }

    [Fact]
    public async Task A_repeated_profile_id_is_only_assigned_once()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\ada", approved: false));
            db.Profiles.Add(new Profile { Id = 10, Name = "Planner" });
        });

        await _service.ApproveUserAsync(1, isAdmin: false, new List<int> { 10, 10, 10 }, "DOMAIN\\admin");

        Assert.Equal(new[] { 10 }, await _service.GetUserProfileIdsAsync(1));
    }

    [Fact]
    public async Task Updating_profiles_replaces_the_previous_set()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\ada"));
            db.Profiles.Add(new Profile { Id = 10, Name = "Planner" });
            db.Profiles.Add(new Profile { Id = 11, Name = "Viewer" });
            db.AppUserProfiles.Add(new AppUserProfile { AppUserId = 1, ProfileId = 10 });
        });

        await _service.UpdateUserProfilesAsync(1, isAdmin: false, new List<int> { 11 });

        Assert.Equal(new[] { 11 }, await _service.GetUserProfileIdsAsync(1));
    }

    [Fact]
    public async Task Reassigning_the_profile_a_user_already_has_is_not_a_conflict()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\ada"));
            db.Profiles.Add(new Profile { Id = 10, Name = "Planner" });
            db.AppUserProfiles.Add(new AppUserProfile { AppUserId = 1, ProfileId = 10 });
        });

        await _service.UpdateUserProfilesAsync(1, isAdmin: false, new List<int> { 10 });

        Assert.Equal(new[] { 10 }, await _service.GetUserProfileIdsAsync(1));
    }

    [Fact]
    public async Task Approving_a_user_who_keeps_one_profile_and_gains_another_is_not_a_conflict()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\ada", approved: false));
            db.Profiles.Add(new Profile { Id = 10, Name = "Planner" });
            db.Profiles.Add(new Profile { Id = 11, Name = "Viewer" });
            db.AppUserProfiles.Add(new AppUserProfile { AppUserId = 1, ProfileId = 10 });
        });

        await _service.ApproveUserAsync(1, isAdmin: false, new List<int> { 10, 11 }, "DOMAIN\\admin");

        Assert.Equal(new[] { 10, 11 }, (await _service.GetUserProfileIdsAsync(1)).OrderBy(i => i));
    }

    [Fact]
    public async Task Updating_profiles_can_clear_them_all()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\ada"));
            db.Profiles.Add(new Profile { Id = 10, Name = "Planner" });
            db.AppUserProfiles.Add(new AppUserProfile { AppUserId = 1, ProfileId = 10 });
        });

        await _service.UpdateUserProfilesAsync(1, isAdmin: false, new List<int>());

        Assert.Empty(await _service.GetUserProfileIdsAsync(1));
    }

    [Fact]
    public async Task Updating_profiles_can_promote_a_user_to_admin()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada")));

        await _service.UpdateUserProfilesAsync(1, isAdmin: true, new List<int>());

        Assert.True((await LoadAsync("DOMAIN\\ada"))!.IsAdmin);
    }

    [Fact]
    public async Task Updating_an_unknown_user_does_nothing()
    {
        await _service.UpdateUserProfilesAsync(999, isAdmin: true, new List<int>());

        Assert.Empty(await _service.GetAllUsersAsync());
    }

    [Fact]
    public async Task An_administrator_can_add_a_user_directly_as_approved()
    {
        await _db.SeedAsync(db => db.Profiles.Add(new Profile { Id = 10, Name = "Planner" }));

        await _service.AddUserAsync(
            "DOMAIN\\ada", "Ada Lovelace", isAdmin: false, new List<int> { 10 },
            "DOMAIN\\admin", "alovelace", "E12345", "ada@example.test");

        var user = await LoadAsync("DOMAIN\\ada");

        Assert.True(user!.IsApproved);
        Assert.False(user.IsAccessRequested);
        Assert.Equal("DOMAIN\\admin", user.ApprovedBy);
        Assert.Equal("Ada Lovelace", user.DisplayName);
        Assert.Equal("alovelace", user.SamAccountName);
        Assert.Equal("E12345", user.EmployeeId);
        Assert.Equal(new[] { 10 }, await _service.GetUserProfileIdsAsync(user.Id));
    }

    [Fact]
    public async Task Adding_a_user_who_already_exists_changes_nothing()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada", approved: false)));

        await _service.AddUserAsync("DOMAIN\\ada", "Renamed", isAdmin: true, new List<int>(), "DOMAIN\\admin");

        var user = await LoadAsync("DOMAIN\\ada");

        Assert.False(user!.IsApproved);
        Assert.False(user.IsAdmin);
        Assert.Null(user.DisplayName);
        Assert.Single(await _service.GetAllUsersAsync());
    }

    [Fact]
    public async Task Directory_details_can_be_refreshed_on_an_existing_user()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada")));

        var updated = await _service.SetAdInfoAsync(1, "Ada Lovelace", "alovelace", "E12345", "ada@example.test");

        var user = await LoadAsync("DOMAIN\\ada");

        Assert.True(updated);
        Assert.Equal("Ada Lovelace", user!.DisplayName);
        Assert.Equal("E12345", user.EmployeeId);
    }

    [Fact]
    public async Task Refreshing_directory_details_for_an_unknown_user_reports_failure()
    {
        Assert.False(await _service.SetAdInfoAsync(999, "Ada", "ada", "E1", "a@b.test"));
    }

    [Fact]
    public async Task Revoking_a_user_removes_both_access_and_admin_rights()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada", admin: true)));

        await _service.RevokeUserAsync(1);

        var user = await LoadAsync("DOMAIN\\ada");

        Assert.False(user!.IsApproved);
        Assert.False(user.IsAdmin);
    }

    [Fact]
    public async Task Revoking_an_unknown_user_does_nothing()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada")));

        await _service.RevokeUserAsync(999);

        Assert.True((await LoadAsync("DOMAIN\\ada"))!.IsApproved);
    }

    [Fact]
    public async Task A_user_can_be_deleted()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada")));

        await _service.DeleteUserAsync(1);

        Assert.Empty(await _service.GetAllUsersAsync());
    }

    [Fact]
    public async Task Deleting_an_unknown_user_does_nothing()
    {
        await _db.SeedAsync(db => db.AppUsers.Add(User(1, "DOMAIN\\ada")));

        await _service.DeleteUserAsync(999);

        Assert.Single(await _service.GetAllUsersAsync());
    }

    [Fact]
    public async Task A_user_row_lists_the_profiles_assigned_to_the_user()
    {
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(User(1, "DOMAIN\\ada"));
            db.Profiles.Add(new Profile { Id = 10, Name = "Viewer" });
            db.Profiles.Add(new Profile { Id = 11, Name = "Planner" });
            db.AppUserProfiles.Add(new AppUserProfile { AppUserId = 1, ProfileId = 10 });
            db.AppUserProfiles.Add(new AppUserProfile { AppUserId = 1, ProfileId = 11 });
        });

        var row = Assert.Single(await _service.GetUserRowsAsync());

        Assert.Equal(new[] { "Planner", "Viewer" }, row.Profiles);
    }

    [Fact]
    public async Task A_user_row_resolves_teams_roles_and_trains_from_the_employee_number()
    {
        await SeedMembershipAsync();

        var row = Assert.Single(await _service.GetUserRowsAsync());

        Assert.Equal(new[] { "Falcons", "Hawks" }, row.Teams);
        Assert.Equal(new[] { "Developer", "Scrum Master" }, row.TeamRoles);
        Assert.Equal(new[] { "Atlas" }, row.Arts);
    }

    [Fact]
    public async Task A_user_row_matches_the_employee_number_despite_a_different_prefix()
    {
        await SeedMembershipAsync(appUserEmployeeId: "XY12345");

        var row = Assert.Single(await _service.GetUserRowsAsync());

        Assert.Equal(new[] { "Falcons", "Hawks" }, row.Teams);
    }

    [Fact]
    public async Task A_user_row_falls_back_to_the_login_when_there_is_no_employee_id()
    {
        await SeedMembershipAsync(appUserEmployeeId: null, samAccountName: "12345");

        var row = Assert.Single(await _service.GetUserRowsAsync());

        Assert.Equal(new[] { "Falcons", "Hawks" }, row.Teams);
    }

    [Fact]
    public async Task A_user_row_with_no_matching_employee_has_no_teams()
    {
        await SeedMembershipAsync(appUserEmployeeId: "E99999");

        var row = Assert.Single(await _service.GetUserRowsAsync());

        Assert.Empty(row.Teams);
        Assert.Empty(row.TeamRoles);
        Assert.Empty(row.Arts);
    }

    [Fact]
    public async Task A_user_row_with_no_identifier_at_all_has_no_teams()
    {
        await SeedMembershipAsync(appUserEmployeeId: null, samAccountName: null);

        var row = Assert.Single(await _service.GetUserRowsAsync());

        Assert.Empty(row.Teams);
    }

    private async Task SeedMembershipAsync(string? appUserEmployeeId = "E12345", string? samAccountName = "alovelace")
    {
        await _db.SeedAsync(db =>
        {
            var user = User(1, "DOMAIN\\ada");
            user.EmployeeId = appUserEmployeeId;
            user.SamAccountName = samAccountName;
            db.AppUsers.Add(user);

            db.HumanResources.Add(new HumanResource
            {
                Id = 5,
                EmployeeNumber = "AB12345",
                EmployeeName = "Ada",
                FullName = "Ada Lovelace"
            });

            db.CapitalProjects.Add(new Art { Id = 50, Name = "Atlas" });
            db.Teams.Add(new Team { Id = 20, Name = "Falcons" });
            db.Teams.Add(new Team { Id = 21, Name = "Hawks" });
            db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = 50, TeamId = 20 });

            db.TeamRoles.Add(new TeamRole { Id = 30, Name = "Developer" });
            db.TeamRoles.Add(new TeamRole { Id = 31, Name = "Scrum Master" });

            db.TeamMembers.Add(new TeamMember { TeamId = 20, HumanResourceId = 5, TeamRoleId = 30 });
            db.TeamMembers.Add(new TeamMember { TeamId = 21, HumanResourceId = 5, TeamRoleId = 31 });
        });
    }
}
