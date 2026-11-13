namespace Estimation.HrReporting.Models;

public class HeadcountUpload
{
    public const int FileNameMaxLength = 260;
    public const int CommentMaxLength = 500;
    public const int UploadedByMaxLength = 256;

    public int Id { get; set; }

    public DateTime DataDate { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? Comment { get; set; }

    public DateTime UploadedAt { get; set; }

    public string? UploadedBy { get; set; }

    public int RowCount { get; set; }

    public decimal TotalHc { get; set; }

    public decimal TotalFte { get; set; }

    public List<HeadcountRow> Rows { get; set; } = [];
}
