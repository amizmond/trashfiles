using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace DynamicExcel.Exclusions;

public class ExcelStyleManager : IExcelStyleManager
{
    private readonly string _headerFillColor = "FF4F81BD";

    private readonly string _fontColor = "FFFFFFFF";

    public ExcelStyleIndices InitializeStyles(WorkbookPart workbookPart)
    {
        var stylesPart = EnsureStylesPart(workbookPart);
        var stylesheet = stylesPart.Stylesheet;

        EnsureBaseStyles(stylesheet);

        var fillId = GetOrCreateFill(stylesheet.Fills!);
        var fontId = GetOrCreateFont(stylesheet.Fonts!);
        var borderId = GetOrCreateBorder(stylesheet.Borders!);

        var indices = new ExcelStyleIndices
        {
            HeaderStyle = GetOrCreateCellFormat(stylesheet.CellFormats!, fontId, fillId, borderId, true, true, true),
            BorderOnlyStyle = GetOrCreateCellFormat(stylesheet.CellFormats!, 0, 0, borderId, false, false, true),
            BorderNumberStyle = GetOrCreateCellFormat(stylesheet.CellFormats!, 0, 0, borderId, false, false, true, 1),
        };

        stylesPart.Stylesheet.Save();
        return indices;
    }

    private WorkbookStylesPart EnsureStylesPart(WorkbookPart workbookPart)
    {
        if (workbookPart.WorkbookStylesPart == null)
        {
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateDefaultStylesheet();
            return stylesPart;
        }
        return workbookPart.WorkbookStylesPart;
    }

    private void EnsureBaseStyles(Stylesheet stylesheet)
    {
        stylesheet.Fonts ??= new Fonts { Count = 0 };
        stylesheet.Fills ??= new Fills { Count = 0 };
        stylesheet.Borders ??= new Borders { Count = 0 };
        stylesheet.CellFormats ??= new CellFormats { Count = 0 };

        if (stylesheet.Fills.Count != null && stylesheet.Fills.Count == 0)
        {
            stylesheet.Fills.AppendChild(new Fill(new PatternFill { PatternType = PatternValues.None }));
            stylesheet.Fills.AppendChild(new Fill(new PatternFill { PatternType = PatternValues.Gray125 }));
            stylesheet.Fills.Count = 2;
        }

        if (stylesheet.Fonts.Count != null && stylesheet.Fonts.Count == 0)
        {
            stylesheet.Fonts.AppendChild(CreateDefaultFont());
            stylesheet.Fonts.Count = 1;
        }

        if (stylesheet.Borders.Count != null && stylesheet.Borders.Count == 0)
        {
            stylesheet.Borders.AppendChild(CreateDefaultBorder());
            stylesheet.Borders.Count = 1;
        }

        if (stylesheet.CellFormats.Count != null && stylesheet.CellFormats.Count == 0)
        {
            stylesheet.CellFormats.AppendChild(CreateDefaultCellFormat());
            stylesheet.CellFormats.Count = 1;
        }
    }

    private uint GetOrCreateFill(Fills fills)
    {
        if (fills.Count != null)
        {
            for (uint i = 0; i < fills.Count; i++)
            {
                var fill = fills.ElementAt((int)i) as Fill;
                if (fill?.PatternFill?.ForegroundColor?.Rgb?.Value == _headerFillColor)
                {
                    return i;
                }
            }
        }

        var newFill = new Fill(new PatternFill
        {
            PatternType = PatternValues.Solid,
            ForegroundColor = new ForegroundColor { Rgb = _headerFillColor },
        });

        fills.AppendChild(newFill);
        fills.Count = (uint)fills.Count();

        return fills.Count.Value - 1;
    }

    private uint GetOrCreateFont(Fonts fonts)
    {
        if (fonts.Count != null)
        {
            for (uint i = 0; i < fonts.Count; i++)
            {
                var font = fonts.ElementAt((int)i) as Font;
                if (font?.Bold != null && font.Color?.Rgb?.Value == _fontColor)
                {
                    return i;
                }
            }
        }

        var newFont = new Font(
            new Bold(),
            new FontSize { Val = 11 },
            new Color { Rgb = _fontColor }
        );

        fonts.AppendChild(newFont);
        fonts.Count = (uint)fonts.Count();
        return fonts.Count.Value - 1;
    }

    private uint GetOrCreateBorder(Borders borders)
    {
        if (borders.Count != null)
        {
            for (uint i = 1; i < borders.Count; i++)
            {
                var border = borders.ElementAt((int)i) as Border;
                if (border?.LeftBorder?.Style?.Value == BorderStyleValues.Thin)
                {
                    return i;
                }
            }
        }

        var newBorder = new Border(
            new LeftBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new RightBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new TopBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new BottomBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new DiagonalBorder()
        );

        borders.AppendChild(newBorder);
        borders.Count = (uint)borders.Count();
        return borders.Count.Value - 1;
    }

    private uint GetOrCreateCellFormat(CellFormats formats, uint fontId, uint fillId, uint borderId, bool applyFont, bool applyFill, bool applyBorder, uint? numberFormatId = null)
    {
        if (formats.Count != null)
        {
            for (uint i = 0; i < formats.Count; i++)
            {
                var format = formats.ElementAt((int)i) as CellFormat;
                if (MatchesCellFormat(format, fontId, fillId, borderId, applyFont, applyFill, applyBorder, numberFormatId))
                {
                    return i;
                }

            }
        }

        var newFormat = new CellFormat
        {
            NumberFormatId = numberFormatId ?? 0,
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            FormatId = 0,
            ApplyFont = applyFont,
            ApplyFill = applyFill,
            ApplyBorder = applyBorder,
            ApplyNumberFormat = numberFormatId.HasValue,
        };

        formats.AppendChild(newFormat);
        formats.Count = (uint)formats.Count();
        return formats.Count.Value - 1;
    }

    private bool MatchesCellFormat(CellFormat? format, uint fontId, uint fillId, uint borderId, bool applyFont, bool applyFill, bool applyBorder, uint? numberFormatId)
    {
        if (format == null)
        {
            return false;
        }
        return format.FontId! == fontId &&
               format.FillId! == fillId &&
               format.BorderId! == borderId &&
               format.ApplyFont?.Value == applyFont &&
               format.ApplyFill?.Value == applyFill &&
               format.ApplyBorder?.Value == applyBorder &&
               (!numberFormatId.HasValue || format.NumberFormatId! == numberFormatId);
    }

    private Stylesheet CreateDefaultStylesheet()
    {
        return new Stylesheet
        {
            Fonts = new Fonts(CreateDefaultFont()) { Count = 1 },
            Fills = new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 })
            )
            { Count = 2 },
            Borders = new Borders(CreateDefaultBorder()) { Count = 1 },
            CellFormats = new CellFormats(CreateDefaultCellFormat()) { Count = 1 },
            CellStyles = new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 },
            DifferentialFormats = new DifferentialFormats { Count = 0 },
            TableStyles = new TableStyles { Count = 0, DefaultTableStyle = "TableStyleMedium2", DefaultPivotStyle = "PivotStyleLight16" },
        };
    }

    private Font CreateDefaultFont() => new(new FontSize { Val = 11 }, new Color { Theme = 1 });

    private Border CreateDefaultBorder() => new(
        new LeftBorder(),
        new RightBorder(),
        new TopBorder(),
        new BottomBorder(),
        new DiagonalBorder()
    );

    private CellFormat CreateDefaultCellFormat() => new()
    {
        NumberFormatId = 0,
        FontId = 0,
        FillId = 0,
        BorderId = 0,
        FormatId = 0
    };
}
