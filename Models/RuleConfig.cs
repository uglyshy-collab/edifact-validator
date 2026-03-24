using EdifactValidator.Models;

namespace EdifactValidator.Models;

public record RuleConfig
{
    public string    Code     { get; init; } = "";
    public bool      Enabled  { get; init; } = true;
    public Severity? Override { get; init; } = null;
}
