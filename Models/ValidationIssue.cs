namespace EdifactValidator.Models;

public enum Severity { Error, Warning, Info }

public record ValidationIssue
{
    public Severity Severity        { get; init; }
    public string   SegmentTag      { get; init; } = string.Empty;
    public int      SegmentIndex    { get; init; }
    public int      LineNumber      { get; init; }
    public string   ElementPosition { get; init; } = string.Empty;
    public string   Code            { get; init; } = string.Empty;
    public string   MessageKey      { get; init; } = string.Empty;
}
