using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Estimation.Excel;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class TechnicalApprovalDefaultTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly FeatureService _features;

    public TechnicalApprovalDefaultTests()
    {
        _features = new FeatureService(_db, new StubAuditUser());
    }

    private sealed class StubAuditUser : IAuditUserProvider
    {
        public string? GetCurrentUserName() => "tester";
    }

    private Task SeedLookupsAsync() =>
        _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new CapitalProject { Id = 50, Name = "Atlas", JiraKey = "ATL" });
            db.TechnicalApprovals.AddRange(
                new TechnicalApproval { Id = TechnicalApproval.ApprovedId, Name = "Approved", SortOrder = 10 },
                new TechnicalApproval { Id = TechnicalApproval.RequiredApproveId, Name = "Required approve", SortOrder = 20 },
                new TechnicalApproval { Id = TechnicalApproval.NotApplicableId, Name = "Not applicable", SortOrder = 30 });
        });

    [Fact]
    public void A_new_feature_starts_out_as_not_applicable()
    {
        Assert.Equal(TechnicalApproval.NotApplicableId, new Feature().TechnicalApprovalId);
    }

    [Fact]
    public async Task Updating_a_feature_without_a_technical_approval_falls_back_to_the_default()
    {
        await SeedLookupsAsync();
        await _db.SeedAsync(db => db.Features.Add(new Feature
        {
            Id = 900,
            ProjectKey = "ATL",
            Name = "Existing",
            Summary = "Existing summary",
            TechnicalApprovalId = TechnicalApproval.ApprovedId
        }));

        await _features.UpdateAsync(new Feature { Id = 900, Name = "Existing", TechnicalApprovalId = null });

        var stored = await _db.ReadAsync(db => db.Features.FindAsync(900).AsTask());
        Assert.Equal(TechnicalApproval.NotApplicableId, stored!.TechnicalApprovalId);
    }

    [Fact]
    public async Task An_explicit_technical_approval_survives_the_update()
    {
        await SeedLookupsAsync();
        await _db.SeedAsync(db => db.Features.Add(new Feature
        {
            Id = 901,
            ProjectKey = "ATL",
            Name = "Existing",
            Summary = "Existing summary"
        }));

        await _features.UpdateAsync(new Feature
        {
            Id = 901,
            Name = "Existing",
            TechnicalApprovalId = TechnicalApproval.RequiredApproveId
        });

        var stored = await _db.ReadAsync(db => db.Features.FindAsync(901).AsTask());
        Assert.Equal(TechnicalApproval.RequiredApproveId, stored!.TechnicalApprovalId);
    }

    [Fact]
    public async Task A_blank_technical_approval_cell_in_an_upload_means_not_applicable()
    {
        await SeedLookupsAsync();

        var sheet = new ExcelWorkbookBuilder().AddSheet("Features");
        sheet.WriteHeader(new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Design Approval" });
        var row = sheet.AddRow();
        row.Text("ATL", skipIfEmpty: true);
        row.Text("ATL-1", skipIfEmpty: true);
        row.Text("Some feature", skipIfEmpty: true);
        row.Text("Some summary", skipIfEmpty: true);
        row.Text(null, skipIfEmpty: true);

        var upload = new FeatureUploadService(_db, new StubCommentService());
        var parsed = await upload.ParseFileAsync(
            new MemoryStream(sheet.Workbook.ToArray()),
            FeatureUploadColumnSelection.All());

        var parsedRow = Assert.Single(parsed.Rows);
        Assert.Equal(TechnicalApproval.NotApplicableId, parsedRow.TechnicalApprovalId);
        Assert.Equal("Not applicable", parsedRow.TechnicalApproval);
        Assert.Empty(parsedRow.ValidationErrors);
    }

    private sealed class StubCommentService : IFeatureCommentService
    {
        public Task<List<FeatureCommentVm>> GetForFeatureAsync(int featureId) =>
            Task.FromResult(new List<FeatureCommentVm>());

        public Task<Dictionary<int, int>> GetCountsAsync(IReadOnlyCollection<int> featureIds) =>
            Task.FromResult(new Dictionary<int, int>());

        public Task<Dictionary<int, string>> GetUnitedAsync(IReadOnlyCollection<int> featureIds, TimeZoneInfo? timeZone = null) =>
            Task.FromResult(new Dictionary<int, string>());

        public Task<FeatureCommentVm> AddAsync(int featureId, string text, string? author = null) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(int commentId, string? requestedBy = null) => Task.FromResult(false);

        public Task<FeatureCommentVm?> SetDoneAsync(int commentId, bool isDone, string? user = null) =>
            Task.FromResult<FeatureCommentVm?>(null);
    }
}
