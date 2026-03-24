namespace EdifactValidator.Models;

public class ValidationStats
{
    public int TotalRuns      { get; set; }
    public int TotalValid     { get; set; }
    public int TestFileCount  { get; set; }
    public Dictionary<string, int> RuleFrequency   { get; set; } = new();
    public Dictionary<string, int> RunsByDate       { get; set; } = new();
    public Dictionary<string, int> SellerFrequency  { get; set; } = new();
    public List<ValidationRun>     RecentRuns        { get; set; } = new();
}

public class ValidationRun
{
    public string Timestamp    { get; set; } = "";
    public int    ErrorCount   { get; set; }
    public int    WarningCount { get; set; }
    public bool   IsValid      { get; set; }
    public bool   IsTest       { get; set; }
    public string SellerName   { get; set; } = "";
}
