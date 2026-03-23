namespace EdifactValidator.Models;

public record EdifactDelimiters
{
    public char ComponentSeparator { get; init; } = ':';
    public char ElementSeparator   { get; init; } = '+';
    public char DecimalNotation    { get; init; } = '.';
    public char ReleaseCharacter   { get; init; } = '?';
    public char SegmentTerminator  { get; init; } = '\'';

    public static readonly EdifactDelimiters Default = new();

    /// <summary>Parses the 9-char body after the literal "UNA".</summary>
    public static EdifactDelimiters FromUna(string una9) => new()
    {
        ComponentSeparator = una9[0],
        ElementSeparator   = una9[1],
        DecimalNotation    = una9[2],
        ReleaseCharacter   = una9[3],
        SegmentTerminator  = una9[5],
    };
}
