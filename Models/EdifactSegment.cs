namespace EdifactValidator.Models;

/// <summary>
/// A fully parsed EDIFACT segment. Elements[e][c] gives component c of element e (both 0-based).
/// Elements[0][0] is always the segment tag.
/// </summary>
public class EdifactSegment
{
    public string Tag          { get; init; } = string.Empty;
    public int SegmentIndex    { get; init; }
    public int LineNumber      { get; init; }

    /// <summary>
    /// Pre-split on all levels: Elements[elementIndex][componentIndex].
    /// Index 0 = tag element, 1 = DE1, 2 = DE2, etc.
    /// </summary>
    public string[][] Elements { get; init; } = [];

    /// <summary>Returns element e component 0 (1-based element), or empty string.</summary>
    public string El(int e) =>
        e < Elements.Length && Elements[e].Length > 0 ? Elements[e][0] : string.Empty;

    /// <summary>Returns component c of element e (both 1-based), or empty string.</summary>
    public string Comp(int e, int c) =>
        e < Elements.Length && c - 1 < Elements[e].Length ? Elements[e][c - 1] : string.Empty;
}
