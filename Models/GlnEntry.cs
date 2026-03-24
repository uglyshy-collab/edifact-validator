namespace EdifactValidator.Models;

public record GlnEntry
{
    public string Group { get; init; } = "";   // z.B. "Porta Mitte"
    public int    FilNr { get; init; }
    public string Name  { get; init; } = "";
    public string GlnIv { get; init; } = "";   // NAD+IV  Rechnungsempfänger (GLN Ausstellung)
    public string GlnDp { get; init; } = "";   // NAD+DP  Warenempfänger     (GLN Lager)
    public string GlnBy { get; init; } = "";   // NAD+BY  Käufer             (Rechnungsanschrift-GLN)
    public string Ort   { get; init; } = "";

    public string Label => FilNr > 0 ? $"{FilNr} – {Name} ({Ort})" : $"{Name} ({Ort})";
}
