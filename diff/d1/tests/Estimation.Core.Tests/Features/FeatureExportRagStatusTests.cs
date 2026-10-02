using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Excel;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureExportRagStatusTests
{
    private const string RagStatusHeader = "Rag Status";

    private static (List<string> Headers, List<List<string>> Rows) Export(params FeatureExportRow[] rows)
    {
        var selection = new FeatureUploadColumnSelection
        {
            Columns = new HashSet<FeatureUploadColumn>
            {
                FeatureUploadColumn.JiraId,
                FeatureUploadColumn.RagStatus,
                FeatureUploadColumn.RagExplain,
            }
        };

        var bytes = FeatureExcelExportService.GenerateFeatureExport(selection, rows.ToList(), new FeatureExportLookups());
        using var stream = new MemoryStream(bytes);
        return ExcelSheetReader.Read(stream, "Features");
    }

    [Fact]
    public void The_rag_status_is_exported_right_before_its_explanation()
    {
        var (headers, rows) = Export(new FeatureExportRow { JiraId = "RAG-1", RagStatus = "Amber", RagExplain = "Waiting on a vendor" });

        var map = ExcelSheetReader.BuildColumnMap(headers);
        Assert.Equal(map["Rag Explain"] - 1, map[RagStatusHeader]);
        Assert.Equal("Amber", ExcelSheetReader.GetCell(rows[0], map, RagStatusHeader));
    }

    [Fact]
    public void A_feature_without_a_rag_status_exports_an_empty_cell()
    {
        var (headers, rows) = Export(new FeatureExportRow { JiraId = "RAG-1" });

        Assert.True(string.IsNullOrEmpty(ExcelSheetReader.GetCell(rows[0], ExcelSheetReader.BuildColumnMap(headers), RagStatusHeader)));
    }

    [Fact]
    public void The_rag_status_can_be_uploaded_back()
    {
        Assert.DoesNotContain(FeatureUploadColumn.RagStatus, FeatureExcelExportService.ExportOnlyColumns);
    }
}
