using System.Text;
using System.Xml;

namespace Estimation.Excel;

internal static class ExcelPrimitives
{
    public const uint HeaderStyleIndex = 1;

    public const uint HeaderFillStyleIndex = 2;

    public const uint DerivedStyleIndex = 3;

    public const uint DateStyleIndex = 4;

    public const uint WrapTextStyleIndex = 5;

    public const uint DateNumberFormatId = 164;

    public const uint FirstDynamicFillId = 4;

    public const uint FirstDynamicFontId = 2;

    public const uint FirstDynamicStyleIndex = 6;

    public static Cell CreateTextCell(string cellReference, string text)
    {
        return new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.String,
            CellValue = new CellValue(XmlSafe(text))
        };
    }

    public static string XmlSafe(string text)
    {
        var index = 0;
        int clean;
        while (index < text.Length && (clean = XmlCharLength(text, index)) > 0)
        {
            index += clean;
        }

        if (index == text.Length)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length).Append(text, 0, index);
        while (index < text.Length)
        {
            var length = XmlCharLength(text, index);
            builder.Append(text, index, length);
            index += Math.Max(length, 1);
        }

        return builder.ToString();
    }

    private static int XmlCharLength(string text, int index)
    {
        if (XmlConvert.IsXmlChar(text[index]))
        {
            return 1;
        }

        return index + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[index + 1], text[index]) ? 2 : 0;
    }

    public static Cell CreateFormulaCell(string cellReference, string formula, string? cachedValue, uint? styleIndex)
    {
        var cell = new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.String,
            CellFormula = new CellFormula(XmlSafe(formula))
        };

        if (cachedValue is not null)
        {
            cell.CellValue = new CellValue(XmlSafe(cachedValue));
        }

        if (styleIndex.HasValue)
        {
            cell.StyleIndex = styleIndex.Value;
        }

        return cell;
    }

    public static Cell CreateNumberCell(string cellReference, int value)
    {
        return new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.Number,
            CellValue = new CellValue(value)
        };
    }

    public static Cell CreateNumberCell(string cellReference, double value)
    {
        return new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.Number,
            CellValue = new CellValue(value)
        };
    }

    public static Cell CreateDateCell(string cellReference, DateTime value)
    {
        return new Cell
        {
            CellReference = cellReference,
            StyleIndex = DateStyleIndex,
            CellValue = new CellValue(value.ToOADate())
        };
    }

    public static Font CreateColorFont(string argb)
    {
        return new Font(new Color { Rgb = argb });
    }

    public static Fill CreateSolidFill(string argb)
    {
        return new Fill(new PatternFill
        {
            PatternType = PatternValues.Solid,
            ForegroundColor = new ForegroundColor { Rgb = argb },
            BackgroundColor = new BackgroundColor { Indexed = 64U }
        });
    }

    public static string NormalizeArgb(string hex)
    {
        var h = (hex ?? "").Trim().TrimStart('#');
        return h.Length switch
        {
            6 => "FF" + h.ToUpperInvariant(),
            8 => h.ToUpperInvariant(),
            _ => "FF000000"
        };
    }

    public static string EscapeFormulaValue(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    public static Stylesheet CreateStylesheet()
    {
        return new Stylesheet(
            new NumberingFormats(
                new NumberingFormat { NumberFormatId = DateNumberFormatId, FormatCode = "yyyy-mm-dd" }
            )
            { Count = 1U },
            new Fonts(
                new Font(),
                new Font(new Bold())
            ),
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill
                {
                    PatternType = PatternValues.Solid,
                    ForegroundColor = new ForegroundColor { Rgb = "FFE8EAF6" },
                    BackgroundColor = new BackgroundColor { Indexed = 64U }
                }),
                new Fill(new PatternFill
                {
                    PatternType = PatternValues.Solid,
                    ForegroundColor = new ForegroundColor { Rgb = "FFF2F2F2" },
                    BackgroundColor = new BackgroundColor { Indexed = 64U }
                })
            ),
            new Borders(
                new Border()
            ),
            new CellFormats(
                new CellFormat(),
                new CellFormat { FontId = 1, ApplyFont = true },
                new CellFormat
                {
                    FontId = 1,
                    FillId = 2,
                    ApplyFont = true,
                    ApplyFill = true
                },
                new CellFormat
                {
                    FillId = 3,
                    ApplyFill = true
                },
                new CellFormat
                {
                    NumberFormatId = DateNumberFormatId,
                    ApplyNumberFormat = true
                },
                new CellFormat
                {
                    Alignment = new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top },
                    ApplyAlignment = true
                }
            )
        );
    }

    public static string GetColumnReference(int zeroBasedIndex)
    {
        var result = "";
        var index = zeroBasedIndex;
        while (index >= 0)
        {
            result = (char)('A' + index % 26) + result;
            index = index / 26 - 1;
        }
        return result;
    }
}
