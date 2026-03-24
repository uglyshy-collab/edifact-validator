namespace EdifactValidator.Models;

public class ValidationRecord
{
    public long   Id           { get; set; }
    public string Timestamp    { get; set; } = "";
    public string FileName     { get; set; } = "";
    public string SellerName   { get; set; } = "";
    public string SellerGln    { get; set; } = "";
    public bool   IsTest       { get; set; }
    public bool   IsValid      { get; set; }
    public int    ErrorCount   { get; set; }
    public int    WarningCount { get; set; }
    public List<IssueRecord> Issues { get; set; } = new();
}

public class IssueRecord
{
    public string Severity        { get; set; } = "";
    public string Code            { get; set; } = "";
    public string SegmentTag      { get; set; } = "";
    public int    LineNumber      { get; set; }
    public string ElementPosition { get; set; } = "";
    public string MessageKey      { get; set; } = "";
}
