namespace EdifactValidator.Models;

public class EdifactInterchange
{
    public EdifactDelimiters      Delimiters   { get; init; } = EdifactDelimiters.Default;
    public EdifactSegment?        Unb          { get; set; }
    public EdifactSegment?        Unz          { get; set; }
    public List<EdifactMessage>   Messages     { get; init; } = new();
    public List<EdifactSegment>   AllSegments  { get; init; } = new();

    public int DeclaredMessageCount =>
        int.TryParse(Unz?.El(1), out var n) ? n : -1;
}
