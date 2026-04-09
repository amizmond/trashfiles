namespace Estimation.Excel.Models;

public record ExcelColumnInfo(string PropertyName, string ExpectedColumnName, Type PropertyType, bool IsRequired);
