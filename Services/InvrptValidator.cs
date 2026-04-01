using EdifactValidator.Models;

namespace EdifactValidator.Services;

/// <summary>
/// Validates an EdifactInterchange against the Porta IT-Service INVRPT guidelines
/// (Inventory Report EANCOM INVRPT D96A).
/// </summary>
public class InvrptValidator : ValidatorBase
{
    public InvrptValidator(GlnStore store) : base(store) { }

    public List<ValidationIssue> Validate(EdifactInterchange ic)
    {
        _issues.Clear();
        ValidateSyntax(ic);
        foreach (var msg in ic.Messages)
            ValidateInvrptMessage(msg, ic);
        return _issues
            .OrderBy(i => i.Severity)
            .ThenBy(i => i.LineNumber)
            .ToList();
    }

    // ── Syntax validation ─────────────────────────────────────────────────────

    private void ValidateSyntax(EdifactInterchange ic) =>
        base.ValidateSyntax(ic, "INVRPT", "INVRPT_SYN_006", "invrpt.syn.006");

    // ── Porta INVRPT validation ───────────────────────────────────────────────

    private void ValidateInvrptMessage(EdifactMessage msg, EdifactInterchange ic)
    {
        if (msg.MessageType != "INVRPT") return;

        // INVRPT_001 — BGM C002.1001=35 (Inventory Report)
        var bgm = msg.Segments.FirstOrDefault(s => s.Tag == "BGM");
        if (bgm is null)
            Err("BGM", 0, 0, "C002.1001=35", "INVRPT_001", "invrpt.001");
        else if (bgm.Comp(1, 1) != "35")
            Err("BGM", bgm.SegmentIndex, bgm.LineNumber, "C002.1001", "INVRPT_001", "invrpt.001");

        // INVRPT_002 — Belegnummer (BGM DE2)
        if (bgm is not null && string.IsNullOrWhiteSpace(bgm.El(2)))
            Err("BGM", bgm.SegmentIndex, bgm.LineNumber, "DE2", "INVRPT_002", "invrpt.002");

        // INVRPT_003 — Belegdatum DTM+137
        var dtm137 = msg.Segments.FirstOrDefault(s => s.Tag == "DTM" && s.Comp(1, 1) == "137");
        if (dtm137 is null)
            Err("DTM", 0, 0, "DE1.C1=137", "INVRPT_003", "invrpt.003");

        var nads = msg.Segments.Where(s => s.Tag == "NAD").ToDictionary(n => n.El(1), n => n);

        // INVRPT_004 — NAD+BY (Käufer)
        CheckNad(nads, "BY", "INVRPT_004", "invrpt.004");
        // INVRPT_005 — NAD+SU (Lieferant)
        CheckNad(nads, "SU", "INVRPT_005", "invrpt.005");

        // INVRPT_006–010 — Positionsebene (LIN/SG9)
        ValidateLineItems(msg);

        // WARN_001 — Testkennzeichen
        if (ic.Unb is not null && ic.Unb.El(11) == "1")
            Warn("UNB", ic.Unb.SegmentIndex, ic.Unb.LineNumber, "DE11", "WARN_001", "warn.001");
    }

    // ── Line item validation (INVRPT_006–010) ────────────────────────────────

    private void ValidateLineItems(EdifactMessage msg)
    {
        var linSegs = msg.Segments.Where(s => s.Tag == "LIN").ToList();

        if (!linSegs.Any())
        {
            Err("LIN", 0, 0, "", "INVRPT_006", "invrpt.006");
            return;
        }

        foreach (var lin in linSegs)
        {
            var linIdx     = msg.Segments.IndexOf(lin);
            var nextLin    = msg.Segments.Skip(linIdx + 1).FirstOrDefault(s => s.Tag == "LIN");
            var nextLinIdx = nextLin is not null ? msg.Segments.IndexOf(nextLin) : msg.Segments.Count;
            var group      = msg.Segments.Skip(linIdx).Take(nextLinIdx - linIdx).ToList();

            // INVRPT_007 — GTIN (LIN C212, Qualifier EN)
            var gtin      = lin.Comp(3, 1);
            var qualifier = lin.Comp(3, 2);
            if (string.IsNullOrWhiteSpace(gtin) || qualifier != "EN")
                Err("LIN", lin.SegmentIndex, lin.LineNumber, "C212.7140/7143=EN", "INVRPT_007", "invrpt.007");

            // INVRPT_008 — QTY+145 (aktueller Lagerbestand) vorhanden
            var qty = group.FirstOrDefault(s => s.Tag == "QTY" && s.Comp(1, 1) == "145");
            if (qty is null)
                Err("QTY", lin.SegmentIndex, lin.LineNumber, "DE1.C1=145", "INVRPT_008", "invrpt.008");
            else if (ParseDecimal(qty.Comp(1, 2)) < 0)
                Err("QTY", qty.SegmentIndex, qty.LineNumber, "DE1.C2", "INVRPT_008b", "invrpt.008b");

            // INVRPT_010 — Verfügbarkeit (STS/INV 9011) muss 71 oder 72 sein
            var sts = group.FirstOrDefault(s => s.Tag == "STS" || s.Tag == "INV");
            var availCode = sts?.El(2) ?? string.Empty;
            if (sts is not null)
            {
                if (availCode != "71" && availCode != "72")
                    Err(sts.Tag, sts.SegmentIndex, sts.LineNumber, "DE2.C1=71/72", "INVRPT_010", "invrpt.010");
            }
            else
                Err("STS", lin.SegmentIndex, lin.LineNumber, "DE2.C1=71/72", "INVRPT_010", "invrpt.010");

            // INVRPT_009 — DTM+169 (Lieferzeit) nur bei verfügbaren Artikeln (STS=71) erforderlich
            var dtm169 = group.FirstOrDefault(s => s.Tag == "DTM" && s.Comp(1, 1) == "169");
            if (dtm169 is null && availCode == "71")
                Err("DTM", lin.SegmentIndex, lin.LineNumber, "DE1.C1=169", "INVRPT_009", "invrpt.009");

            // INVRPT_WARN_002 — PIA+SA (Lieferantenartikel-Nr.) fehlt
            var piaSa = group.FirstOrDefault(s => s.Tag == "PIA" && s.Comp(2, 2) == "SA");
            if (piaSa is null)
                Warn("PIA", lin.SegmentIndex, lin.LineNumber, "DE2.C2=SA", "INVRPT_WARN_002", "invrpt.warn.002");

            // INVRPT_WARN_003 — IMD (Beschreibung) fehlt
            var imd = group.FirstOrDefault(s => s.Tag == "IMD");
            if (imd is null)
                Warn("IMD", lin.SegmentIndex, lin.LineNumber, "", "INVRPT_WARN_003", "invrpt.warn.003");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

}
