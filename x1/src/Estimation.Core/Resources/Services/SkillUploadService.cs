using Estimation.Core.Resources.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Resources.Services;

public interface ISkillUploadService
{
    Task<byte[]> ExportAllAsync();
    Task<List<SkillUploadRow>> ParseFileAsync(Stream fileStream);
    Task SaveAsync(List<SkillUploadRow> rows);
}

public class SkillUploadService : ISkillUploadService
{
    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;

    public SkillUploadService(IDbContextFactory<EstimationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<byte[]> ExportAllAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var skills = await db.Skills
            .Include(s => s.Levels)
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync();

        var rows = new List<SkillExportRow>();
        foreach (var skill in skills)
        {
            var orderedLevels = skill.Levels.OrderBy(l => l.Value).ThenBy(l => l.Name).ToList();
            if (orderedLevels.Count == 0)
            {
                rows.Add(new SkillExportRow
                {
                    SkillName = skill.Name,
                    LevelName = "",
                    Value = null,
                    LevelDescription = null
                });
            }
            else
            {
                foreach (var level in orderedLevels)
                {
                    rows.Add(new SkillExportRow
                    {
                        SkillName = skill.Name,
                        LevelName = level.Name,
                        Value = level.Value,
                        LevelDescription = level.Description
                    });
                }
            }
        }

        return SkillExcelExportService.GenerateSkillExport(rows);
    }

    public async Task<List<SkillUploadRow>> ParseFileAsync(Stream fileStream)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var existingSkills = await db.Skills
            .Include(s => s.Levels)
            .AsNoTracking()
            .ToListAsync();

        var (headers, dataRows) = ExcelSheetReader.Read(fileStream, "Skills");
        var colMap = ExcelSheetReader.BuildColumnMap(headers);

        var skillGroups = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in dataRows)
        {
            var skillName = ExcelSheetReader.GetCell(row, colMap, "Skill")?.Trim();
            if (string.IsNullOrWhiteSpace(skillName))
            {
                continue;
            }

            var rowData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Skill"] = skillName,
                ["Level"] = ExcelSheetReader.GetCell(row, colMap, "Level")?.Trim() ?? "",
                ["Value"] = ExcelSheetReader.GetCell(row, colMap, "Value")?.Trim() ?? "",
                ["Level Description"] = ExcelSheetReader.GetCell(row, colMap, "Level Description")?.Trim() ?? ""
            };

            if (!skillGroups.ContainsKey(skillName))
            {
                skillGroups[skillName] = new List<Dictionary<string, string>>();
            }
            skillGroups[skillName].Add(rowData);
        }

        var result = new List<SkillUploadRow>();

        foreach (var (skillName, groupRows) in skillGroups)
        {
            var existingSkill = existingSkills.FirstOrDefault(s =>
                s.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase));

            var uploadRow = new SkillUploadRow
            {
                ExistingSkillId = existingSkill?.Id,
                SkillName = skillName,
                IsNewSkill = existingSkill is null
            };

            var uploadedLevels = groupRows
                .Where(r => !string.IsNullOrWhiteSpace(r["Level"]))
                .ToList();

            if (existingSkill is not null)
            {
                var matchedExistingIds = new HashSet<int>();

                foreach (var levelRow in uploadedLevels)
                {
                    var levelName = levelRow["Level"];
                    int? value = int.TryParse(levelRow["Value"], out var v) ? v : null;
                    var levelDesc = string.IsNullOrWhiteSpace(levelRow["Level Description"])
                        ? null
                        : levelRow["Level Description"];

                    var existingLevel = existingSkill.Levels.FirstOrDefault(l =>
                        l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));

                    if (existingLevel is not null)
                    {
                        matchedExistingIds.Add(existingLevel.Id);

                        var isChanged = existingLevel.Value != value
                            || !string.Equals(existingLevel.Description, levelDesc, StringComparison.Ordinal);

                        uploadRow.Levels.Add(new SkillLevelUploadItem
                        {
                            ExistingLevelId = existingLevel.Id,
                            LevelName = levelName,
                            Value = value,
                            Description = levelDesc,
                            IsNew = false,
                            IsChanged = isChanged,
                            OldLevelName = existingLevel.Name,
                            OldValue = existingLevel.Value,
                            OldDescription = existingLevel.Description
                        });
                    }
                    else
                    {
                        uploadRow.Levels.Add(new SkillLevelUploadItem
                        {
                            LevelName = levelName,
                            Value = value,
                            Description = levelDesc,
                            IsNew = true,
                            IsChanged = false
                        });
                    }
                }

                foreach (var existingLevel in existingSkill.Levels)
                {
                    if (!matchedExistingIds.Contains(existingLevel.Id))
                    {
                        uploadRow.RemovedLevels.Add(new SkillLevelUploadItem
                        {
                            ExistingLevelId = existingLevel.Id,
                            LevelName = existingLevel.Name,
                            Value = existingLevel.Value,
                            Description = existingLevel.Description,
                            IsRemoved = true,
                            OldLevelName = existingLevel.Name,
                            OldValue = existingLevel.Value,
                            OldDescription = existingLevel.Description
                        });
                    }
                }
            }
            else
            {
                foreach (var levelRow in uploadedLevels)
                {
                    int? value = int.TryParse(levelRow["Value"], out var v) ? v : null;
                    var levelDesc = string.IsNullOrWhiteSpace(levelRow["Level Description"])
                        ? null
                        : levelRow["Level Description"];

                    uploadRow.Levels.Add(new SkillLevelUploadItem
                    {
                        LevelName = levelRow["Level"],
                        Value = value,
                        Description = levelDesc,
                        IsNew = true
                    });
                }
            }

            result.Add(uploadRow);
        }

        return result;
    }

    public async Task SaveAsync(List<SkillUploadRow> rows)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        foreach (var row in rows.Where(r => r.HasChanges))
        {
            if (row.IsNewSkill)
            {
                var skill = new Skill
                {
                    Name = row.SkillName,
                    Description = null,
                    Created = DateTime.UtcNow
                };

                foreach (var level in row.Levels)
                {
                    skill.Levels.Add(new SkillLevel
                    {
                        Name = level.LevelName,
                        Value = level.Value,
                        Description = level.Description
                    });
                }

                db.Skills.Add(skill);
            }
            else
            {
                var skill = await db.Skills
                    .Include(s => s.Levels)
                    .FirstOrDefaultAsync(s => s.Id == row.ExistingSkillId);

                if (skill is null)
                {
                    continue;
                }

                skill.Updated = DateTime.UtcNow;

                foreach (var removed in row.RemovedLevels)
                {
                    var existing = skill.Levels.FirstOrDefault(l => l.Id == removed.ExistingLevelId);
                    if (existing is not null)
                    {
                        var assignments = await db.HumanResourceSkills
                            .Where(hrs => hrs.SkillLevelId == existing.Id)
                            .ToListAsync();
                        foreach (var a in assignments)
                        {
                            a.SkillLevelId = null;
                        }

                        skill.Levels.Remove(existing);
                    }
                }

                foreach (var level in row.Levels)
                {
                    if (level.IsNew)
                    {
                        skill.Levels.Add(new SkillLevel
                        {
                            Name = level.LevelName,
                            Value = level.Value,
                            Description = level.Description
                        });
                    }
                    else if (level.IsChanged && level.ExistingLevelId.HasValue)
                    {
                        var existing = skill.Levels.FirstOrDefault(l => l.Id == level.ExistingLevelId);
                        if (existing is not null)
                        {
                            existing.Value = level.Value;
                            existing.Description = level.Description;
                        }
                    }
                }
            }
        }

        await db.SaveChangesAsync();
    }
}
