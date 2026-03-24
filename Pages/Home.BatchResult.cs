using EdifactValidator.Models;

namespace edifact_validator.Pages;

/// <summary>Result entry for batch validation mode.</summary>
internal sealed class BatchResult
{
    public BatchResult(string fileName, string messageType, List<ValidationIssue> issues, EdifactInterchange interchange)
    {
        FileName    = fileName;
        MessageType = messageType;
        Issues      = issues;
        Interchange = interchange;
    }

    public string                  FileName    { get; }
    public string                  MessageType { get; }
    public List<ValidationIssue>   Issues      { get; }
    public EdifactInterchange      Interchange { get; }
    public int  ErrorCount => Issues.Count(i => i.Severity == Severity.Error);
    public int  WarnCount  => Issues.Count(i => i.Severity == Severity.Warning);
    public bool IsValid    => ErrorCount == 0;
}
