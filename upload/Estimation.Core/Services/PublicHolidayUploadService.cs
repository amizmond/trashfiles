using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Services;

public interface IPublicHolidayUploadService
{
    Task<byte[]> GenerateTemplateAsync();
    Task<List<PublicHolidayUploadRow>> ParseFileAsync(Stream fileStream);
    Task<int> SaveAsync(List<PublicHolidayUploadRow> rows);
}

public class PublicHolidayUploadService : IPublicHolidayUploadService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    public PublicHolidayUploadService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<byte[]> GenerateTemplateAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var countryNames = await db.Countries
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync();
        var cityNamesList = await db.Cities
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .Distinct()
            .ToListAsync();

        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Public Holidays"
            });

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = BuildStylesheet();
            stylesPart.Stylesheet.Save();

            var header = new Row { RowIndex = 1 };
            header.Append(MakeTextCell("A1", "Date", styleIndex: 1));
            header.Append(MakeTextCell("B1", "Name", styleIndex: 1));
            header.Append(MakeTextCell("C1", "Country", styleIndex: 1));
            header.Append(MakeTextCell("D1", "City", styleIndex: 1));
            header.Append(MakeTextCell("E1", "Description", styleIndex: 1));
            sheetData.Append(header);

            var columns = new Columns();
            // Style 2 applies the UK date format (dd/mm/yyyy) to the Date column's data cells.
            columns.Append(new Column { Min = 1, Max = 1, Width = 14, CustomWidth = true, Style = 2 });
            columns.Append(new Column { Min = 2, Max = 2, Width = 30, CustomWidth = true });
            columns.Append(new Column { Min = 3, Max = 3, Width = 24, CustomWidth = true });
            columns.Append(new Column { Min = 4, Max = 4, Width = 24, CustomWidth = true });
            columns.Append(new Column { Min = 5, Max = 5, Width = 40, CustomWidth = true });
            worksheetPart.Worksheet.InsertBefore(columns, sheetData);

            var dataValidations = new DataValidations();
            AppendListValidation(dataValidations, countryNames, "C2:C1000", "Invalid Country", "Pick a country from the list.");
            AppendListValidation(dataValidations, cityNamesList, "D2:D1000", "Invalid City", "Pick a city from the list, or leave blank for a country-wide holiday.");
            if ((dataValidations.Count?.Value ?? 0u) > 0)
            {
                worksheetPart.Worksheet.Append(dataValidations);
            }

            workbookPart.Workbook.Save();
        }

        return ms.ToArray();
    }

    // Adds a dropdown (list) validation for a column range when the value list is short enough
    // for Excel's inline-list limit. Long lists are skipped (the column stays free-text).
    private static void AppendListValidation(
        DataValidations validations, List<string> values, string range, string errorTitle, string error)
    {
        if (values.Count == 0)
        {
            return;
        }
        var formula = string.Join(",", values.Select(n => n.Replace("\"", "\"\"")));
        if (formula.Length >= 250)
        {
            return;
        }
        var validation = new DataValidation
        {
            Type = DataValidationValues.List,
            AllowBlank = true,
            ShowDropDown = false,
            ShowErrorMessage = true,
            ErrorTitle = new StringValue(errorTitle),
            Error = new StringValue(error),
            SequenceOfReferences = new ListValue<StringValue>(new[] { new StringValue(range) })
        };
        validation.Append(new Formula1($"\"{formula}\""));
        validations.Append(validation);
        validations.Count = (validations.Count?.Value ?? 0u) + 1u;
    }

    // Custom number-format id (>=164 is the reserved range for user-defined formats).
    private const uint UkDateNumberFormatId = 164u;

    private static Stylesheet BuildStylesheet()
    {
        return new Stylesheet(
            // numFmts must precede fonts in the stylesheet element order.
            new NumberingFormats(
                new NumberingFormat
                {
                    NumberFormatId = UkDateNumberFormatId,
                    FormatCode = "dd/mm/yyyy"
                }
            ) { Count = 1 },
            new Fonts(
                new Font(),
                new Font(new Bold())
            ) { Count = 2 },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 })
            ) { Count = 2 },
            new Borders(new Border()) { Count = 1 },
            new CellFormats(
                new CellFormat(),                                  // 0: default
                new CellFormat { FontId = 1, ApplyFont = true },   // 1: bold header
                new CellFormat                                     // 2: UK date column
                {
                    NumberFormatId = UkDateNumberFormatId,
                    ApplyNumberFormat = true
                }
            ) { Count = 3 }
        );
    }

    private static Cell MakeTextCell(string reference, string text, uint? styleIndex = null)
    {
        var cell = new Cell
        {
            CellReference = reference,
            DataType = CellValues.String,
            CellValue = new CellValue(text)
        };
        if (styleIndex.HasValue)
        {
            cell.StyleIndex = styleIndex.Value;
        }
        return cell;
    }

    public async Task<List<PublicHolidayUploadRow>> ParseFileAsync(Stream fileStream)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var countries = await db.Countries.AsNoTracking().ToListAsync();
        var cities = await db.Cities.Include(c => c.Country).AsNoTracking().ToListAsync();
        var existing = await db.PublicHolidays.AsNoTracking().ToListAsync();

        var (headers, dataRows) = ReadExcel(fileStream);

        var col = MapColumns(headers);
        if (col.Date < 0 || col.Name < 0 || col.Country < 0)
        {
            throw new InvalidOperationException(
                "Excel must have header columns: 'Date', 'Name', 'Country' (and optional 'City', 'Description').");
        }

        var result = new List<PublicHolidayUploadRow>();
        // First row number seen per unique key, to flag in-file duplicates that would
        // violate IX_PublicHolidays_CountryId_CityId_Date on insert.
        var seenKeys = new Dictionary<(int? CountryId, int? CityId, DateTime Date), int>();
        var rowNo = 1;
        foreach (var row in dataRows)
        {
            rowNo++;
            var rawDate = row.ElementAtOrDefault(col.Date)?.Trim();
            var name = row.ElementAtOrDefault(col.Name)?.Trim();
            var countryName = row.ElementAtOrDefault(col.Country)?.Trim();
            var cityName = col.City >= 0 ? row.ElementAtOrDefault(col.City)?.Trim() : null;
            var description = col.Description >= 0 ? row.ElementAtOrDefault(col.Description)?.Trim() : null;

            if (string.IsNullOrWhiteSpace(rawDate) && string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(countryName) && string.IsNullOrWhiteSpace(cityName))
            {
                continue;
            }

            var item = new PublicHolidayUploadRow
            {
                RowNumber = rowNo,
                RawDate = rawDate,
                Name = name,
                Description = description,
                CountryName = countryName,
                CityName = cityName,
            };

            item.ParsedDate = TryParseDate(rawDate);
            if (item.ParsedDate is null)
            {
                item.Error = $"Could not parse date '{rawDate}'.";
            }
            else if (string.IsNullOrWhiteSpace(name))
            {
                item.Error = "Name is required.";
            }
            else if (!string.IsNullOrWhiteSpace(cityName))
            {
                // City-scoped holiday. Disambiguate by country when one is given.
                var matches = cities
                    .Where(c => string.Equals(c.Name, cityName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (!string.IsNullOrWhiteSpace(countryName))
                {
                    matches = matches
                        .Where(c => string.Equals(c.Country?.Name, countryName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (matches.Count == 0)
                {
                    item.Error = $"City '{cityName}' not found.";
                }
                else if (matches.Count > 1)
                {
                    item.Error = $"City '{cityName}' is ambiguous; add a Country to disambiguate.";
                }
                else
                {
                    var city = matches[0];
                    item.MatchedCityId = city.Id;
                    item.CountryName = city.Country?.Name ?? countryName;
                    var match = existing.FirstOrDefault(p =>
                        p.CityId == city.Id && p.Date.Date == item.ParsedDate!.Value.Date);
                    item.ExistingId = match?.Id;
                }
            }
            else if (string.IsNullOrWhiteSpace(countryName))
            {
                item.Error = "Country (or City) is required.";
            }
            else
            {
                var country = countries.FirstOrDefault(c =>
                    string.Equals(c.Name, countryName, StringComparison.OrdinalIgnoreCase));
                if (country is null)
                {
                    item.Error = $"Country '{countryName}' not found.";
                }
                else
                {
                    item.MatchedCountryId = country.Id;
                    var match = existing.FirstOrDefault(p =>
                        p.CountryId == country.Id && p.CityId == null && p.Date.Date == item.ParsedDate!.Value.Date);
                    item.ExistingId = match?.Id;
                }
            }

            if (item.Error is null && item.ParsedDate is not null
                && (item.MatchedCountryId is not null || item.MatchedCityId is not null))
            {
                (int? CountryId, int? CityId, DateTime Date) key = item.IsCityScoped
                    ? (null, item.MatchedCityId, item.ParsedDate.Value.Date)
                    : (item.MatchedCountryId, null, item.ParsedDate.Value.Date);
                if (seenKeys.TryGetValue(key, out var firstRowNo))
                {
                    item.Error = $"Duplicate of row {firstRowNo} (same country/city and date).";
                }
                else
                {
                    seenKeys[key] = rowNo;
                }
            }

            result.Add(item);
        }

        return result;
    }

    public async Task<int> SaveAsync(List<PublicHolidayUploadRow> rows)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var saved = 0;
        // Keys already written in this batch, so a duplicate row in the input
        // (or a re-submitted preview) cannot insert the same holiday twice.
        var batchKeys = new HashSet<(int? CountryId, int? CityId, DateTime Date)>();
        foreach (var r in rows.Where(x => x.IsValid))
        {
            var date = r.ParsedDate!.Value.Date;
            int? countryId = r.IsCityScoped ? null : r.MatchedCountryId!.Value;
            int? cityId = r.IsCityScoped ? r.MatchedCityId!.Value : null;
            if (!batchKeys.Add((countryId, cityId, date)))
            {
                continue;
            }

            PublicHoliday? entity = null;
            if (r.ExistingId is not null)
            {
                entity = await db.PublicHolidays.FirstOrDefaultAsync(p => p.Id == r.ExistingId.Value);
            }
            // The preview may be stale: the holiday may have been created (or the matched
            // one deleted) since the file was parsed. Re-resolve by the unique key
            // (CountryId, CityId, Date) before deciding between insert and update.
            entity ??= await db.PublicHolidays.FirstOrDefaultAsync(p =>
                p.CountryId == countryId && p.CityId == cityId && p.Date == date);

            if (entity is null)
            {
                db.PublicHolidays.Add(new PublicHoliday
                {
                    Name = r.Name!,
                    Description = r.Description,
                    Date = date,
                    CountryId = countryId,
                    CityId = cityId,
                });
            }
            else
            {
                entity.Name = r.Name!;
                entity.Description = r.Description;
                entity.Date = date;
                entity.CountryId = countryId;
                entity.CityId = cityId;
            }
            saved++;
        }
        await db.SaveChangesAsync();
        return saved;
    }

    private static DateTime? TryParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial))
        {
            try
            {
                return DateTime.FromOADate(serial).Date;
            }
            catch
            {
                return null;
            }
        }
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d1))
        {
            return d1.Date;
        }
        if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var d2))
        {
            return d2.Date;
        }
        return null;
    }

    private static (int Date, int Name, int Country, int City, int Description) MapColumns(List<string> headers)
    {
        int Find(params string[] names) =>
            headers.FindIndex(h => names.Any(n => string.Equals(h, n, StringComparison.OrdinalIgnoreCase)));
        return (Find("Date"), Find("Name", "Holiday"), Find("Country"), Find("City"), Find("Description"));
    }

    private static (List<string> Headers, List<List<string>> Rows) ReadExcel(Stream fileStream)
    {
        using var doc = SpreadsheetDocument.Open(fileStream, false);
        var workbookPart = doc.WorkbookPart!;
        var sheet = workbookPart.Workbook.GetFirstChild<Sheets>()!.Elements<Sheet>().First();
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);

        var sharedStrings = new Dictionary<string, string>();
        var ssPart = workbookPart.SharedStringTablePart;
        if (ssPart != null)
        {
            var items = ssPart.SharedStringTable.Elements<SharedStringItem>().ToList();
            for (var i = 0; i < items.Count; i++)
            {
                sharedStrings[i.ToString()] = items[i].Text?.Text ?? items[i].InnerText ?? string.Empty;
            }
        }

        var rows = worksheetPart.Worksheet.GetFirstChild<SheetData>()!.Elements<Row>().ToList();
        if (rows.Count == 0)
        {
            return (new List<string>(), new List<List<string>>());
        }

        var headers = ReadRowValues(rows[0], sharedStrings);
        var dataRows = rows.Skip(1)
            .Select(r => ReadRowValues(r, sharedStrings))
            .Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v)))
            .ToList();
        return (headers, dataRows);
    }

    private static List<string> ReadRowValues(Row row, Dictionary<string, string> sharedStrings)
    {
        var values = new List<string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var colIndex = GetColumnIndex(cell.CellReference!);
            while (values.Count < colIndex)
            {
                values.Add(string.Empty);
            }
            values.Add(GetCellValue(cell, sharedStrings));
        }
        return values;
    }

    private static string GetCellValue(Cell cell, Dictionary<string, string> sharedStrings)
    {
        if (cell.CellValue == null && cell.InlineString == null)
        {
            return string.Empty;
        }
        if (cell.DataType?.Value == CellValues.SharedString && cell.CellValue != null)
        {
            return sharedStrings.TryGetValue(cell.CellValue.Text, out var ss) ? ss : string.Empty;
        }
        if (cell.DataType?.Value == CellValues.InlineString && cell.InlineString != null)
        {
            return cell.InlineString.Text?.Text ?? cell.InlineString.InnerText ?? string.Empty;
        }
        return cell.CellValue?.Text?.Trim() ?? string.Empty;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var col = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }
            col = col * 26 + (ch - 'A' + 1);
        }
        return col - 1;
    }
}
