namespace Estimation.Excel.Models;

[AttributeUsage(AttributeTargets.Property)]
public class ExcelColumnAttribute : Attribute
{
    public string? Name { get; set; }

    public bool AllowMissing { get; set; }

    public ExcelColumnAttribute()
    {
    }

    public ExcelColumnAttribute(string name)
    {
        Name = name;
    }
}
