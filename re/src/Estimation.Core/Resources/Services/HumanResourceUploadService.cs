using Estimation.Core.Resources.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Resources.Services;

public class HumanResourceExportFilter
{
    public string? Search { get; set; }
    public IReadOnlyCollection<int>? SkillIds { get; set; }
    public bool? IsActive { get; set; }
    public IReadOnlyCollection<string>? TeamNames { get; set; }
    public bool TeamNot { get; set; }
    public IReadOnlyCollection<string>? TeamRoleNames { get; set; }
    public IReadOnlyCollection<string>? ArtNames { get; set; }
    public IReadOnlyCollection<string>? DepartmentNames { get; set; }
    public IReadOnlyCollection<string>? CountryNames { get; set; }
    public IReadOnlyCollection<string>? CityNames { get; set; }
    public IReadOnlyCollection<string>? EmployeeCategoryNames { get; set; }
}

public interface IHumanResourceUploadService
{
    Task<byte[]> ExportFilteredAsync(HrUploadColumnSelection selection, HumanResourceExportFilter filter);

    Task<HashSet<HrUploadColumn>> DetectColumnsAsync(Stream fileStream);

    Task<List<HumanResourceUploadRow>> ParseFileAsync(Stream fileStream, HrUploadColumnSelection selection);

    Task SaveAsync(List<HumanResourceUploadRow> rows);
}

public class HumanResourceUploadService : IHumanResourceUploadService
{
    private const string SheetName = "Resources";

    private static readonly Dictionary<HrUploadColumn, string> Headers = new()
    {
        [HrUploadColumn.EmployeeNumber] = "BRID",
        [HrUploadColumn.EmployeeName] = "Employee Name",
        [HrUploadColumn.FullName] = "Full Name",
        [HrUploadColumn.Active] = "Active",
        [HrUploadColumn.Team] = "Team",
        [HrUploadColumn.TeamRole] = "Team Role",
        [HrUploadColumn.City] = "City",
        [HrUploadColumn.Category] = "Category",
        [HrUploadColumn.EmployeeType] = "Employee Type",
        [HrUploadColumn.EmployeeRole] = "Employee Role",
        [HrUploadColumn.EmployeeVendor] = "Employee Vendor",
        [HrUploadColumn.CorporateGrade] = "Corporate Grade",
        [HrUploadColumn.AnnualLeaveDays] = "Annual Leave Days",
    };

    private static readonly Dictionary<string, HrUploadColumn> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EmployeeNumber"] = HrUploadColumn.EmployeeNumber,
    };

    private static readonly HrUploadColumn[] ColumnOrder =
    {
        HrUploadColumn.EmployeeNumber,
        HrUploadColumn.EmployeeName,
        HrUploadColumn.FullName,
        HrUploadColumn.Active,
        HrUploadColumn.Team,
        HrUploadColumn.TeamRole,
        HrUploadColumn.City,
        HrUploadColumn.Category,
        HrUploadColumn.EmployeeType,
        HrUploadColumn.EmployeeRole,
        HrUploadColumn.EmployeeVendor,
        HrUploadColumn.CorporateGrade,
        HrUploadColumn.AnnualLeaveDays,
    };

    private const string ActiveYes = "Active";
    private const string ActiveNo = "Inactive";

    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;

    public HumanResourceUploadService(IDbContextFactory<EstimationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<byte[]> ExportFilteredAsync(HrUploadColumnSelection selection, HumanResourceExportFilter filter)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var skills = await db.Skills
            .Include(s => s.Levels)
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync();

        var query = ApplyFilter(db.HumanResources.AsQueryable(), filter);

        var humanResources = await query
            .Include(hr => hr.TeamMembers).ThenInclude(tm => tm.Team)
            .Include(hr => hr.TeamMembers).ThenInclude(tm => tm.TeamRole)
            .Include(hr => hr.HumanResourceSkills).ThenInclude(hrs => hrs.SkillLevel)
            .Include(hr => hr.City).ThenInclude(c => c!.Country)
            .Include(hr => hr.EmployeeCategory)
            .Include(hr => hr.EmployeeType)
            .Include(hr => hr.EmployeeRole)
            .Include(hr => hr.EmployeeVendor)
            .Include(hr => hr.CorporateGrade)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(hr => hr.EmployeeName)
            .ToListAsync();

        var teamNames = await OrderedNamesAsync(db.Teams.Select(t => t.Name));
        var teamRoleNames = await OrderedNamesAsync(db.TeamRoles.Select(r => r.Name));
        var cityNames = await OrderedNamesAsync(db.Cities.Select(c => c.Name));
        var categoryNames = await OrderedNamesAsync(db.EmployeeCategories.Select(c => c.Name));
        var employeeTypeNames = await OrderedNamesAsync(db.EmployeeTypes.Select(c => c.Name));
        var employeeRoleNames = await OrderedNamesAsync(db.EmployeeRoles.Select(c => c.Name));
        var employeeVendorNames = await OrderedNamesAsync(db.EmployeeVendors.Select(c => c.Name));
        var corporateGradeNames = await OrderedNamesAsync(db.CorporateGrades.Select(c => c.Name));

        var columns = new List<ColumnarExportColumn>();
        var emitters = new List<Func<HumanResource, string?>>();

        foreach (var column in ColumnOrder)
        {
            if (!selection.Includes(column))
            {
                continue;
            }

            switch (column)
            {
                case HrUploadColumn.EmployeeNumber:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 20 });
                    emitters.Add(hr => hr.EmployeeNumber);
                    break;
                case HrUploadColumn.EmployeeName:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 30 });
                    emitters.Add(hr => hr.EmployeeName);
                    break;
                case HrUploadColumn.FullName:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 30 });
                    emitters.Add(hr => hr.FullName);
                    break;
                case HrUploadColumn.Active:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 12, DropdownValues = new() { ActiveYes, ActiveNo } });
                    emitters.Add(hr => hr.IsActive ? ActiveYes : ActiveNo);
                    break;
                case HrUploadColumn.Team:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 25, DropdownValues = teamNames });
                    emitters.Add(hr => string.Join(", ", hr.TeamMembers.Select(tm => tm.Team.Name).OrderBy(n => n)));
                    break;
                case HrUploadColumn.TeamRole:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 20, DropdownValues = teamRoleNames });
                    emitters.Add(hr => string.Join(", ", hr.TeamMembers
                        .Where(tm => tm.TeamRole != null)
                        .Select(tm => tm.TeamRole!.Name)
                        .Distinct()
                        .OrderBy(n => n)));
                    break;
                case HrUploadColumn.City:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 18, DropdownValues = cityNames });
                    emitters.Add(hr => hr.City?.Name);
                    break;
                case HrUploadColumn.Category:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 18, DropdownValues = categoryNames });
                    emitters.Add(hr => hr.EmployeeCategory?.Name);
                    break;
                case HrUploadColumn.EmployeeType:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 18, DropdownValues = employeeTypeNames });
                    emitters.Add(hr => hr.EmployeeType?.Name);
                    break;
                case HrUploadColumn.EmployeeRole:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 18, DropdownValues = employeeRoleNames });
                    emitters.Add(hr => hr.EmployeeRole?.Name);
                    break;
                case HrUploadColumn.EmployeeVendor:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 18, DropdownValues = employeeVendorNames });
                    emitters.Add(hr => hr.EmployeeVendor?.Name);
                    break;
                case HrUploadColumn.CorporateGrade:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 18, DropdownValues = corporateGradeNames });
                    emitters.Add(hr => hr.CorporateGrade?.Name);
                    break;
                case HrUploadColumn.AnnualLeaveDays:
                    columns.Add(new ColumnarExportColumn { Header = Headers[column], Width = 16 });
                    emitters.Add(hr => hr.AnnualLeaveDays?.ToString());
                    break;
            }
        }

        var includeSkills = selection.Includes(HrUploadColumn.Skills);
        if (includeSkills)
        {
            foreach (var skill in skills)
            {
                columns.Add(new ColumnarExportColumn
                {
                    Header = skill.Name,
                    Width = 18,
                    DropdownValues = skill.Levels.OrderBy(l => l.Value).ThenBy(l => l.Name).Select(l => l.Name).ToList()
                });
            }
        }

        var rows = new List<List<string?>>();
        foreach (var hr in humanResources)
        {
            var cells = new List<string?>();
            foreach (var emit in emitters)
            {
                cells.Add(emit(hr));
            }

            if (includeSkills)
            {
                foreach (var skill in skills)
                {
                    cells.Add(hr.HumanResourceSkills
                        .FirstOrDefault(hrs => hrs.SkillId == skill.Id)?.SkillLevel?.Name);
                }
            }

            rows.Add(cells);
        }

        return ColumnarExcelExportService.GenerateColumnarExport(SheetName, columns, rows);
    }

    public async Task<HashSet<HrUploadColumn>> DetectColumnsAsync(Stream fileStream)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var skills = await db.Skills.AsNoTracking().ToListAsync();

        var (headers, _) = ExcelSheetReader.Read(fileStream, SheetName, "Teams");
        return HeadersToPresentColumns(headers, skills);
    }

    public async Task<List<HumanResourceUploadRow>> ParseFileAsync(Stream fileStream, HrUploadColumnSelection selection)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var skills = await db.Skills
            .Include(s => s.Levels)
            .AsNoTracking()
            .ToListAsync();

        var existingHrs = await db.HumanResources
            .Include(hr => hr.HumanResourceSkills).ThenInclude(hrs => hrs.SkillLevel)
            .Include(hr => hr.TeamMembers).ThenInclude(tm => tm.Team)
            .Include(hr => hr.TeamMembers).ThenInclude(tm => tm.TeamRole)
            .Include(hr => hr.City).ThenInclude(c => c!.Country)
            .Include(hr => hr.EmployeeCategory)
            .Include(hr => hr.EmployeeType)
            .Include(hr => hr.EmployeeRole)
            .Include(hr => hr.EmployeeVendor)
            .Include(hr => hr.CorporateGrade)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        var cityNames = await NameSetAsync(db.Cities.Select(c => c.Name));
        var categoryNames = await NameSetAsync(db.EmployeeCategories.Select(c => c.Name));
        var employeeTypeNames = await NameSetAsync(db.EmployeeTypes.Select(c => c.Name));
        var employeeRoleNames = await NameSetAsync(db.EmployeeRoles.Select(c => c.Name));
        var employeeVendorNames = await NameSetAsync(db.EmployeeVendors.Select(c => c.Name));
        var corporateGradeNames = await NameSetAsync(db.CorporateGrades.Select(c => c.Name));

        var (headers, dataRows) = ExcelSheetReader.Read(fileStream, SheetName, "Teams");
        var colMap = ExcelSheetReader.BuildColumnMap(headers);

        var present = HeadersToPresentColumns(headers, skills);

        bool Apply(HrUploadColumn column) => present.Contains(column) && selection.Includes(column);

        var skillColumns = new List<(int ColumnIndex, Skill Skill)>();
        if (Apply(HrUploadColumn.Skills))
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var skill = skills.FirstOrDefault(s => s.Name.Equals(headers[i].Trim(), StringComparison.OrdinalIgnoreCase));
                if (skill != null)
                {
                    skillColumns.Add((i, skill));
                }
            }
        }

        var result = new List<HumanResourceUploadRow>();

        foreach (var row in dataRows)
        {
            var fullName = GetColumnCell(row, colMap, HrUploadColumn.FullName)?.Trim();
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            var employeeNumber = GetColumnCell(row, colMap, HrUploadColumn.EmployeeNumber)?.Trim();

            HumanResource? existingHr = null;
            if (!string.IsNullOrWhiteSpace(employeeNumber))
            {
                existingHr = existingHrs.FirstOrDefault(h =>
                    !string.IsNullOrWhiteSpace(h.EmployeeNumber) &&
                    h.EmployeeNumber.Equals(employeeNumber, StringComparison.OrdinalIgnoreCase));
            }
            existingHr ??= existingHrs.FirstOrDefault(h =>
                h.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase));

            var uploadRow = new HumanResourceUploadRow
            {
                ExistingHrId = existingHr?.Id,
                FullName = fullName,
                EmployeeNumber = employeeNumber,
                IsNew = existingHr is null,
                SkillsIncluded = Apply(HrUploadColumn.Skills),
                CurrentFullName = existingHr?.FullName,
                CurrentEmployeeNumber = existingHr?.EmployeeNumber,
            };

            if (Apply(HrUploadColumn.EmployeeName))
            {
                uploadRow.EmployeeName = GetColumnCell(row, colMap, HrUploadColumn.EmployeeName)?.Trim();
            }

            if (existingHr is not null)
            {
                uploadRow.CurrentEmployeeName = existingHr.EmployeeName;
                uploadRow.CurrentTeamName = existingHr.TeamMembers.Select(tm => tm.Team.Name).OrderBy(n => n).FirstOrDefault();
                uploadRow.CurrentTeamRoleName = string.Join(", ", existingHr.TeamMembers
                    .Where(tm => tm.TeamRole != null)
                    .Select(tm => tm.TeamRole!.Name)
                    .Distinct()
                    .OrderBy(n => n)) is { Length: > 0 } roles ? roles : null;
                uploadRow.CurrentActive = existingHr.IsActive;
                uploadRow.CurrentCityName = existingHr.City?.Name;
                uploadRow.CurrentCategoryName = existingHr.EmployeeCategory?.Name;
                uploadRow.CurrentEmployeeTypeName = existingHr.EmployeeType?.Name;
                uploadRow.CurrentEmployeeRoleName = existingHr.EmployeeRole?.Name;
                uploadRow.CurrentEmployeeVendorName = existingHr.EmployeeVendor?.Name;
                uploadRow.CurrentCorporateGradeName = existingHr.CorporateGrade?.Name;
                uploadRow.CurrentAnnualLeaveDays = existingHr.AnnualLeaveDays;

                uploadRow.FullNameChanged = !string.Equals(uploadRow.CurrentFullName, fullName, StringComparison.OrdinalIgnoreCase);

                if (Apply(HrUploadColumn.EmployeeNumber) && !string.IsNullOrWhiteSpace(employeeNumber))
                {
                    uploadRow.EmployeeNumberChanged = !string.Equals(uploadRow.CurrentEmployeeNumber, employeeNumber, StringComparison.OrdinalIgnoreCase);
                }

                if (Apply(HrUploadColumn.EmployeeName) && !string.IsNullOrWhiteSpace(uploadRow.EmployeeName))
                {
                    uploadRow.EmployeeNameChanged = !string.Equals(uploadRow.CurrentEmployeeName, uploadRow.EmployeeName, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (Apply(HrUploadColumn.Active))
            {
                var raw = GetColumnCell(row, colMap, HrUploadColumn.Active)?.Trim();
                uploadRow.UploadedActive = ParseActive(raw);
                if (existingHr is not null && uploadRow.UploadedActive.HasValue)
                {
                    uploadRow.ActiveChanged = uploadRow.UploadedActive.Value != existingHr.IsActive;
                }
            }

            if (Apply(HrUploadColumn.Team))
            {
                var teamName = GetColumnCell(row, colMap, HrUploadColumn.Team)?.Trim();
                uploadRow.UploadedTeamName = teamName;
                if (existingHr is not null)
                {
                    uploadRow.TeamChanged = !string.IsNullOrWhiteSpace(teamName)
                        && !string.Equals(uploadRow.CurrentTeamName, teamName, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (Apply(HrUploadColumn.TeamRole))
            {
                var value = GetColumnCell(row, colMap, HrUploadColumn.TeamRole)?.Trim();
                uploadRow.UploadedTeamRoleName = NormalizeLookup(value, null);
                if (existingHr is not null)
                {
                    uploadRow.TeamRoleChanged = ScalarChanged(uploadRow.CurrentTeamRoleName, value, null);
                }
            }

            if (Apply(HrUploadColumn.City))
            {
                var value = GetColumnCell(row, colMap, HrUploadColumn.City)?.Trim();
                uploadRow.UploadedCityName = NormalizeLookup(value, cityNames);
                if (existingHr is not null)
                {
                    uploadRow.CityChanged = ScalarChanged(uploadRow.CurrentCityName, value, cityNames);
                }
            }

            if (Apply(HrUploadColumn.Category))
            {
                var value = GetColumnCell(row, colMap, HrUploadColumn.Category)?.Trim();
                uploadRow.UploadedCategoryName = NormalizeLookup(value, categoryNames);
                if (existingHr is not null)
                {
                    uploadRow.CategoryChanged = ScalarChanged(uploadRow.CurrentCategoryName, value, categoryNames);
                }
            }

            if (Apply(HrUploadColumn.EmployeeType))
            {
                var value = GetColumnCell(row, colMap, HrUploadColumn.EmployeeType)?.Trim();
                uploadRow.UploadedEmployeeTypeName = NormalizeLookup(value, employeeTypeNames);
                if (existingHr is not null)
                {
                    uploadRow.EmployeeTypeChanged = ScalarChanged(uploadRow.CurrentEmployeeTypeName, value, employeeTypeNames);
                }
            }

            if (Apply(HrUploadColumn.EmployeeRole))
            {
                var value = GetColumnCell(row, colMap, HrUploadColumn.EmployeeRole)?.Trim();
                uploadRow.UploadedEmployeeRoleName = NormalizeLookup(value, employeeRoleNames);
                if (existingHr is not null)
                {
                    uploadRow.EmployeeRoleChanged = ScalarChanged(uploadRow.CurrentEmployeeRoleName, value, employeeRoleNames);
                }
            }

            if (Apply(HrUploadColumn.EmployeeVendor))
            {
                var value = GetColumnCell(row, colMap, HrUploadColumn.EmployeeVendor)?.Trim();
                uploadRow.UploadedEmployeeVendorName = NormalizeLookup(value, employeeVendorNames);
                if (existingHr is not null)
                {
                    uploadRow.EmployeeVendorChanged = ScalarChanged(uploadRow.CurrentEmployeeVendorName, value, employeeVendorNames);
                }
            }

            if (Apply(HrUploadColumn.CorporateGrade))
            {
                var value = GetColumnCell(row, colMap, HrUploadColumn.CorporateGrade)?.Trim();
                uploadRow.UploadedCorporateGradeName = NormalizeLookup(value, corporateGradeNames);
                if (existingHr is not null)
                {
                    uploadRow.CorporateGradeChanged = ScalarChanged(uploadRow.CurrentCorporateGradeName, value, corporateGradeNames);
                }
            }

            if (Apply(HrUploadColumn.AnnualLeaveDays))
            {
                var value = GetColumnCell(row, colMap, HrUploadColumn.AnnualLeaveDays)?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    uploadRow.UploadedAnnualLeaveDays = null;
                    if (existingHr is not null)
                    {
                        uploadRow.AnnualLeaveDaysChanged = existingHr.AnnualLeaveDays.HasValue;
                    }
                }
                else if (int.TryParse(value, out var days))
                {
                    uploadRow.UploadedAnnualLeaveDays = days;
                    if (existingHr is not null)
                    {
                        uploadRow.AnnualLeaveDaysChanged = existingHr.AnnualLeaveDays != days;
                    }
                }
            }

            foreach (var (colIdx, skill) in skillColumns)
            {
                var cellValue = colIdx < row.Count ? row[colIdx]?.Trim() : null;
                var isEmpty = string.IsNullOrWhiteSpace(cellValue);

                var existingAssignment = existingHr?.HumanResourceSkills
                    .FirstOrDefault(hrs => hrs.SkillId == skill.Id);
                var oldLevelName = existingAssignment?.SkillLevel?.Name;

                if (isEmpty)
                {
                    if (existingAssignment != null)
                    {
                        uploadRow.Skills.Add(new HrSkillUploadItem
                        {
                            SkillId = skill.Id,
                            SkillName = skill.Name,
                            NewLevelName = null,
                            NewLevelId = null,
                            OldLevelName = oldLevelName,
                            IsChanged = true,
                            IsRemoved = true
                        });
                    }
                }
                else
                {
                    var matchedLevel = skill.Levels.FirstOrDefault(l =>
                        l.Name.Equals(cellValue, StringComparison.OrdinalIgnoreCase));

                    var newLevelId = matchedLevel?.Id;
                    var newLevelName = matchedLevel?.Name ?? cellValue;

                    var isChanged = existingAssignment is null
                        || existingAssignment.SkillLevelId != newLevelId;

                    uploadRow.Skills.Add(new HrSkillUploadItem
                    {
                        SkillId = skill.Id,
                        SkillName = skill.Name,
                        NewLevelName = newLevelName,
                        NewLevelId = newLevelId,
                        OldLevelName = oldLevelName,
                        IsChanged = isChanged,
                        IsRemoved = false
                    });
                }
            }

            result.Add(uploadRow);
        }

        return result;
    }

    public async Task SaveAsync(List<HumanResourceUploadRow> rows)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var allTeams = await db.Teams.ToListAsync();
        var allTeamRoles = await db.TeamRoles.ToListAsync();
        var allCities = await db.Cities.ToListAsync();
        var allCategories = await db.EmployeeCategories.ToListAsync();
        var allEmployeeTypes = await db.EmployeeTypes.ToListAsync();
        var allEmployeeRoles = await db.EmployeeRoles.ToListAsync();
        var allEmployeeVendors = await db.EmployeeVendors.ToListAsync();
        var allCorporateGrades = await db.CorporateGrades.ToListAsync();
        var existingHrs = await db.HumanResources
            .Include(h => h.HumanResourceSkills)
            .ToListAsync();

        foreach (var row in rows.Where(r => r.HasChanges))
        {
            HumanResource hr;

            if (row.IsNew)
            {
                hr = new HumanResource
                {
                    EmployeeName = string.IsNullOrWhiteSpace(row.EmployeeName) ? row.FullName : row.EmployeeName,
                    FullName = row.FullName,
                    EmployeeNumber = row.EmployeeNumber,
                    IsActive = row.UploadedActive ?? true
                };

                hr.CityId = LookupId(allCities, row.UploadedCityName, c => c.Name, c => c.Id);
                hr.EmployeeCategoryId = LookupId(allCategories, row.UploadedCategoryName, c => c.Name, c => c.Id);
                hr.EmployeeTypeId = LookupId(allEmployeeTypes, row.UploadedEmployeeTypeName, c => c.Name, c => c.Id);
                hr.EmployeeRoleId = LookupId(allEmployeeRoles, row.UploadedEmployeeRoleName, c => c.Name, c => c.Id);
                hr.EmployeeVendorId = LookupId(allEmployeeVendors, row.UploadedEmployeeVendorName, c => c.Name, c => c.Id);
                hr.CorporateGradeId = LookupId(allCorporateGrades, row.UploadedCorporateGradeName, c => c.Name, c => c.Id);
                hr.AnnualLeaveDays = row.UploadedAnnualLeaveDays;

                db.HumanResources.Add(hr);
                await db.SaveChangesAsync();

                if (!string.IsNullOrWhiteSpace(row.UploadedTeamName))
                {
                    var team = allTeams.FirstOrDefault(t => t.Name.Equals(row.UploadedTeamName, StringComparison.OrdinalIgnoreCase));
                    if (team is not null)
                    {
                        db.TeamMembers.Add(new TeamMember
                        {
                            TeamId = team.Id,
                            HumanResourceId = hr.Id,
                            TeamRoleId = string.IsNullOrWhiteSpace(row.UploadedTeamRoleName)
                                ? null
                                : LookupId(allTeamRoles, row.UploadedTeamRoleName, r => r.Name, r => r.Id)
                        });
                    }
                }
            }
            else
            {
                hr = existingHrs.First(h => h.Id == row.ExistingHrId);

                if (row.FullNameChanged && !string.IsNullOrWhiteSpace(row.FullName))
                {
                    hr.FullName = row.FullName;
                }
                if (row.EmployeeNumberChanged && !string.IsNullOrWhiteSpace(row.EmployeeNumber))
                {
                    hr.EmployeeNumber = row.EmployeeNumber;
                }
                if (row.EmployeeNameChanged && !string.IsNullOrWhiteSpace(row.EmployeeName))
                {
                    hr.EmployeeName = row.EmployeeName;
                }
                if (row.ActiveChanged && row.UploadedActive.HasValue)
                {
                    hr.IsActive = row.UploadedActive.Value;
                }

                TeamMember? addedMembership = null;
                if (row.TeamChanged && !string.IsNullOrWhiteSpace(row.UploadedTeamName))
                {
                    var team = allTeams.FirstOrDefault(t => t.Name.Equals(row.UploadedTeamName, StringComparison.OrdinalIgnoreCase));
                    if (team is not null)
                    {
                        var isMember = await db.TeamMembers.AnyAsync(tm => tm.TeamId == team.Id && tm.HumanResourceId == hr.Id);
                        if (!isMember)
                        {
                            addedMembership = new TeamMember { TeamId = team.Id, HumanResourceId = hr.Id };
                            db.TeamMembers.Add(addedMembership);
                        }
                    }
                }

                if (row.TeamRoleChanged)
                {
                    var roleId = LookupId(allTeamRoles, row.UploadedTeamRoleName, r => r.Name, r => r.Id);
                    var memberships = await db.TeamMembers
                        .Where(tm => tm.HumanResourceId == hr.Id)
                        .ToListAsync();
                    foreach (var membership in memberships)
                    {
                        membership.TeamRoleId = roleId;
                    }

                    if (addedMembership is not null)
                    {
                        addedMembership.TeamRoleId = roleId;
                    }
                }
                if (row.CityChanged)
                {
                    hr.CityId = LookupId(allCities, row.UploadedCityName, c => c.Name, c => c.Id);
                }
                if (row.CategoryChanged)
                {
                    hr.EmployeeCategoryId = LookupId(allCategories, row.UploadedCategoryName, c => c.Name, c => c.Id);
                }
                if (row.EmployeeTypeChanged)
                {
                    hr.EmployeeTypeId = LookupId(allEmployeeTypes, row.UploadedEmployeeTypeName, c => c.Name, c => c.Id);
                }
                if (row.EmployeeRoleChanged)
                {
                    hr.EmployeeRoleId = LookupId(allEmployeeRoles, row.UploadedEmployeeRoleName, c => c.Name, c => c.Id);
                }
                if (row.EmployeeVendorChanged)
                {
                    hr.EmployeeVendorId = LookupId(allEmployeeVendors, row.UploadedEmployeeVendorName, c => c.Name, c => c.Id);
                }
                if (row.CorporateGradeChanged)
                {
                    hr.CorporateGradeId = LookupId(allCorporateGrades, row.UploadedCorporateGradeName, c => c.Name, c => c.Id);
                }
                if (row.AnnualLeaveDaysChanged)
                {
                    hr.AnnualLeaveDays = row.UploadedAnnualLeaveDays;
                }
            }

            foreach (var skillItem in row.Skills)
            {
                if (skillItem.IsRemoved)
                {
                    var existing = hr.HumanResourceSkills.FirstOrDefault(hrs => hrs.SkillId == skillItem.SkillId);
                    if (existing != null)
                    {
                        db.HumanResourceSkills.Remove(existing);
                        hr.HumanResourceSkills.Remove(existing);
                    }
                }
                else
                {
                    var existing = hr.HumanResourceSkills.FirstOrDefault(hrs => hrs.SkillId == skillItem.SkillId);
                    if (existing != null)
                    {
                        existing.SkillLevelId = skillItem.NewLevelId;
                    }
                    else
                    {
                        var newHrSkill = new HumanResourceSkill
                        {
                            HumanResourceId = hr.Id,
                            SkillId = skillItem.SkillId,
                            SkillLevelId = skillItem.NewLevelId
                        };
                        db.HumanResourceSkills.Add(newHrSkill);
                        hr.HumanResourceSkills.Add(newHrSkill);
                    }
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private static IQueryable<HumanResource> ApplyFilter(IQueryable<HumanResource> query, HumanResourceExportFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(hr =>
                hr.FullName.Contains(term) ||
                hr.EmployeeName.Contains(term) ||
                (hr.EmployeeNumber != null && hr.EmployeeNumber.Contains(term)));
        }

        if (filter.IsActive.HasValue)
        {
            query = query.Where(hr => hr.IsActive == filter.IsActive.Value);
        }

        if (filter.SkillIds is { Count: > 0 })
        {
            query = query.Where(hr => hr.HumanResourceSkills.Any(hrs => filter.SkillIds.Contains(hrs.SkillId)));
        }

        if (filter.TeamNot)
        {
            if (filter.TeamNames is { Count: > 0 })
            {
                query = query.Where(hr => !hr.TeamMembers.Any(tm => filter.TeamNames.Contains(tm.Team.Name)));
            }
            else
            {
                query = query.Where(hr => !hr.TeamMembers.Any());
            }
        }
        else if (filter.TeamNames is { Count: > 0 })
        {
            query = query.Where(hr => hr.TeamMembers.Any(tm => filter.TeamNames.Contains(tm.Team.Name)));
        }

        if (filter.TeamRoleNames is { Count: > 0 })
        {
            query = query.Where(hr => hr.TeamMembers.Any(tm => tm.TeamRole != null && filter.TeamRoleNames.Contains(tm.TeamRole.Name)));
        }

        if (filter.ArtNames is { Count: > 0 })
        {
            query = query.Where(hr => hr.TeamMembers.Any(tm =>
                tm.Team.CapitalProjectTeams.Any(cpt => filter.ArtNames.Contains(cpt.Art.Name))));
        }

        if (filter.DepartmentNames is { Count: > 0 })
        {
            query = query.Where(hr => hr.TeamMembers.Any(tm =>
                tm.Team.CapitalProjectTeams.Any(cpt =>
                    cpt.Art.Department != null && filter.DepartmentNames.Contains(cpt.Art.Department.Name))));
        }

        if (filter.CountryNames is { Count: > 0 })
        {
            query = query.Where(hr => hr.City != null && filter.CountryNames.Contains(hr.City.Country.Name));
        }

        if (filter.CityNames is { Count: > 0 })
        {
            query = query.Where(hr => hr.City != null && filter.CityNames.Contains(hr.City.Name));
        }

        if (filter.EmployeeCategoryNames is { Count: > 0 })
        {
            query = query.Where(hr => hr.EmployeeCategory != null && filter.EmployeeCategoryNames.Contains(hr.EmployeeCategory.Name));
        }

        return query;
    }

    private static async Task<List<string>> OrderedNamesAsync(IQueryable<string> query)
    {
        return await query.AsNoTracking().Distinct().OrderBy(n => n).ToListAsync();
    }

    private static async Task<HashSet<string>> NameSetAsync(IQueryable<string> query)
    {
        return new HashSet<string>(await query.AsNoTracking().ToListAsync(), StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetColumnCell(List<string> row, Dictionary<string, int> colMap, HrUploadColumn column)
    {
        var value = ExcelSheetReader.GetCell(row, colMap, Headers[column]);
        if (value is not null)
        {
            return value;
        }
        foreach (var alias in HeaderAliases)
        {
            if (alias.Value == column)
            {
                value = ExcelSheetReader.GetCell(row, colMap, alias.Key);
                if (value is not null)
                {
                    return value;
                }
            }
        }
        return null;
    }

    private static int? LookupId<T>(IEnumerable<T> items, string? name, Func<T, string> nameSelector, Func<T, int> idSelector)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        var match = items.FirstOrDefault(i => nameSelector(i).Equals(name, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : idSelector(match);
    }

    private static string? NormalizeLookup(string? value, HashSet<string>? known)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (known is not null && !known.Contains(value))
        {
            return null;
        }
        return value;
    }

    private static bool ScalarChanged(string? current, string? uploaded, HashSet<string>? known)
    {
        if (string.IsNullOrWhiteSpace(uploaded))
        {
            return !string.IsNullOrWhiteSpace(current);
        }
        if (known is not null && !known.Contains(uploaded))
        {
            return false;
        }
        return !string.Equals(current, uploaded, StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ParseActive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var v = value.Trim();
        if (v.Equals(ActiveYes, StringComparison.OrdinalIgnoreCase) || v.Equals("Yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("True", StringComparison.OrdinalIgnoreCase) || v == "1")
        {
            return true;
        }
        if (v.Equals(ActiveNo, StringComparison.OrdinalIgnoreCase) || v.Equals("No", StringComparison.OrdinalIgnoreCase)
            || v.Equals("False", StringComparison.OrdinalIgnoreCase) || v == "0")
        {
            return false;
        }
        return null;
    }

    private static HashSet<HrUploadColumn> HeadersToPresentColumns(List<string> headers, List<Skill> skills)
    {
        var skillNameSet = new HashSet<string>(skills.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        var headerToColumn = Headers.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

        var present = new HashSet<HrUploadColumn>();
        foreach (var raw in headers)
        {
            var header = raw.Trim();
            if (header.Length == 0)
            {
                continue;
            }
            if (headerToColumn.TryGetValue(header, out var column)
                || HeaderAliases.TryGetValue(header, out column))
            {
                present.Add(column);
            }
            else if (skillNameSet.Contains(header))
            {
                present.Add(HrUploadColumn.Skills);
            }
        }
        return present;
    }

}
