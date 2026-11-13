namespace Estimation.HrReporting.Services;

public sealed record HeadcountListSnapshot(
    int UploadId,
    HeadcountFilter Filter,
    string? SortKey,
    bool SortDescending,
    int Page,
    int PageSize);

public sealed class HeadcountListState
{
    public HeadcountListSnapshot? Saved { get; set; }

    public bool ReturnPending { get; set; }

    public HeadcountListSnapshot? TakeReturn(int? uploadId)
    {
        if (!ReturnPending)
        {
            return null;
        }

        ReturnPending = false;
        return Saved is { } saved && saved.UploadId == uploadId ? saved : null;
    }
}
