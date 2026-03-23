namespace EdifactValidator.Models;

public enum EdifactVersion { Unknown, D96A, D01B }

public class EdifactMessage
{
    public EdifactSegment?        Unh      { get; set; }
    public EdifactSegment?        Unt      { get; set; }
    public List<EdifactSegment>   Segments { get; init; } = new();

    public string ReferenceNumber => Unh?.El(1) ?? string.Empty;
    public string MessageType     => Unh?.Comp(2, 1) ?? string.Empty;
    public string VersionString   => Unh?.Comp(2, 3) ?? string.Empty;
    public EdifactVersion DetectedVersion { get; set; } = EdifactVersion.Unknown;
}
