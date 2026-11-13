namespace Estimation.HrReporting.Services;

public sealed class HeadcountUploadPreviewState
{
    public HeadcountParseResult? Pending { get; set; }

    public void Clear() => Pending = null;
}
