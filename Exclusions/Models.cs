namespace DynamicExcel.Exclusions;

public class SummaryInfo
{
    public string? Regulator { get; set; }

    public string? EagleContextId { get; set; }

    public string? AxisCcrBatchId { get; set; }

    public string? AxisParentBatchId { get; set; }

    public string? AxisBatchId { get; set; }

    public string? ReviewedBy { get; set; }
}

public class SummaryResult
{
    public string? Group { get; set; }

    public long Count { get; set; }
}

public class Difference
{
    public int Missing { get; set; }

    public int Extra { get; set; }
}

public class ExclusionReasons
{
    public string? Reason { get; set; }

    public string? ExclusionId { get; set; }

    public long Count { get; set; }
}
