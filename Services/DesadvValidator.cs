using EdifactValidator.Models;

namespace EdifactValidator.Services;

/// <summary>
/// Validates an EdifactInterchange against the Porta IT-Service DESADV guidelines
/// (Lieferavis EANCOM DESADV D96A).
/// </summary>
public class DesadvValidator : ValidatorBase
{
    public DesadvValidator(GlnStore store) : base(store) { }

    public List<ValidationIssue> Validate(EdifactInterchange ic)
    {
        _issues.Clear();
        ValidateSyntax(ic);
        foreach (var msg in ic.Messages)
            ValidateDesadvMessage(msg, ic);
        return _issues
            .OrderBy(i => i.Severity)
            .ThenBy(i => i.LineNumber)
            .ToList();
    }

    // ── Syntax validation ─────────────────────────────────────────────────────

    private void ValidateSyntax(EdifactInterchange ic) =>
        base.ValidateSyntax(ic, "DESADV", "DESADV_SYN_006", "desadv.syn.006");

    // ── Porta DESADV validation ───────────────────────────────────────────────

    private void ValidateDesadvMessage(EdifactMessage msg, EdifactInterchange ic)
    {
        if (msg.MessageType != "DESADV") return;

        // DESADV_001 — BGM+351 (Lieferavis)
        var bgm = msg.Segments.FirstOrDefault(s => s.Tag == "BGM");
        if (bgm is null)
            Err("BGM", 0, 0, "DE1", "DESADV_001", "desadv.001");
        else if (bgm.El(1) != "351")
            Err("BGM", bgm.SegmentIndex, bgm.LineNumber, "DE1", "DESADV_001", "desadv.001");

        // DESADV_002 — Belegnummer (BGM DE2)
        if (bgm is not null && string.IsNullOrWhiteSpace(bgm.El(2)))
            Err("BGM", bgm.SegmentIndex, bgm.LineNumber, "DE2", "DESADV_002", "desadv.002");

        // DESADV_003 — Belegdatum DTM+137
        var dtm137 = msg.Segments.FirstOrDefault(s => s.Tag == "DTM" && s.Comp(1, 1) == "137");
        if (dtm137 is null)
            Err("DTM", 0, 0, "DE1.C1=137", "DESADV_003", "desadv.003");

        var nads = msg.Segments.Where(s => s.Tag == "NAD").ToDictionary(n => n.El(1), n => n);

        // DESADV_004 — NAD+BY (Käufer)
        CheckNad(nads, "BY", "DESADV_004", "desadv.004");
        // DESADV_005 — NAD+SU (Lieferant)
        CheckNad(nads, "SU", "DESADV_005", "desadv.005");
        // DESADV_006 — NAD+DP (Warenempfänger)
        CheckNad(nads, "DP", "DESADV_006", "desadv.006");

        // DESADV_007 — RFF+VA oder RFF+FC (Steuernummer nach NAD+SU)
        var hasVa = msg.Segments.Any(s => s.Tag == "RFF" && s.Comp(1, 1) == "VA");
        var hasFc = msg.Segments.Any(s => s.Tag == "RFF" && s.Comp(1, 1) == "FC");
        if (!hasVa && !hasFc)
        {
            var su = nads.TryGetValue("SU", out var suSeg) ? suSeg : null;
            Err("RFF", su?.SegmentIndex ?? 0, su?.LineNumber ?? 0, "DE1.C1=VA/FC", "DESADV_007", "desadv.007");
        }

        // DESADV_008 — CPS-Segment (Versandeinheit) vorhanden
        var cps = msg.Segments.FirstOrDefault(s => s.Tag == "CPS");
        if (cps is null)
            Err("CPS", 0, 0, "", "DESADV_008", "desadv.008");

        // DESADV_009 — PAC-Segment (Packstück) vorhanden
        var pac = msg.Segments.FirstOrDefault(s => s.Tag == "PAC");
        if (pac is null)
            Err("PAC", 0, 0, "", "DESADV_009", "desadv.009");

        // DESADV_010–015 — Positionsebene (LIN)
        ValidateLineItems(msg);

        // DESADV_016 — UNS+S
        var uns = msg.Segments.FirstOrDefault(s => s.Tag == "UNS");
        if (uns is null || uns.El(1) != "S")
            Err("UNS", uns?.SegmentIndex ?? 0, uns?.LineNumber ?? 0, "DE1=S", "DESADV_016", "desadv.016");

        // DESADV_WARN_001 — Testkennzeichen (geteilt mit INVOIC/ORDRSP)
        if (ic.Unb is not null && ic.Unb.El(11) == "1")
            Warn("UNB", ic.Unb.SegmentIndex, ic.Unb.LineNumber, "DE11", "WARN_001", "warn.001");

        // DESADV_WARN_003 — GIN+BJ (Trackingnummer) fehlt
        var gin = msg.Segments.FirstOrDefault(s => s.Tag == "GIN" && s.El(1) == "BJ");
        if (gin is null)
            Warn("GIN", 0, 0, "DE1=BJ", "DESADV_WARN_003", "desadv.warn.003");
    }

    // ── Line item validation (DESADV_010–015) ────────────────────────────────

    private void ValidateLineItems(EdifactMessage msg)
    {
        var linGroups = GroupByLeader(msg, "LIN").ToList();

        if (linGroups.Count == 0)
        {
            Err("LIN", 0, 0, "", "DESADV_010", "desadv.010.none");
            return;
        }

        foreach (var (lin, group) in linGroups)
        {

            // DESADV_010 — GTIN (LIN DE3.C1, Qualifier EN)
            var gtin = lin.Comp(3, 1);
            if (string.IsNullOrWhiteSpace(gtin))
                Err("LIN", lin.SegmentIndex, lin.LineNumber, "DE3.C1", "DESADV_010", "desadv.010");

            // DESADV_011 — QTY+12 (ausgelieferte Menge) > 0
            var qty = group.FirstOrDefault(s => s.Tag == "QTY" && s.Comp(1, 1) == "12");
            if (qty is null)
                Err("QTY", lin.SegmentIndex, lin.LineNumber, "DE1.C1=12", "DESADV_011", "desadv.011");
            else if (ParseDecimal(qty.Comp(1, 2)) <= 0)
                Err("QTY", qty.SegmentIndex, qty.LineNumber, "DE1.C2", "DESADV_011b", "desadv.011b");

            // DESADV_012 — DTM+79 (Versanddatum)
            var dtm79 = group.FirstOrDefault(s => s.Tag == "DTM" && s.Comp(1, 1) == "79");
            if (dtm79 is null)
                Err("DTM", lin.SegmentIndex, lin.LineNumber, "DE1.C1=79", "DESADV_012", "desadv.012");

            // DESADV_013 — DTM+76 (Lieferdatum)
            var dtm76 = group.FirstOrDefault(s => s.Tag == "DTM" && s.Comp(1, 1) == "76");
            if (dtm76 is null)
                Err("DTM", lin.SegmentIndex, lin.LineNumber, "DE1.C1=76", "DESADV_013", "desadv.013");

            // DESADV_014 — RFF+DQ (Lieferscheinnummer, SG16)
            var rffDq = group.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "DQ");
            if (rffDq is null || string.IsNullOrWhiteSpace(rffDq.Comp(1, 2)))
                Err("RFF", lin.SegmentIndex, lin.LineNumber, "DE1.C1=DQ", "DESADV_014", "desadv.014");

            // DESADV_015 — RFF+ON (Bestellnummer, SG16) — Warnung wenn fehlt
            var rffOn = group.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "ON");
            if (rffOn is null)
                Warn("RFF", lin.SegmentIndex, lin.LineNumber, "DE1.C1=ON", "DESADV_WARN_002", "desadv.warn.002");

            // DESADV_WARN_001 — PIA+SA (Lieferantenartikel) fehlt
            var piaSa = group.FirstOrDefault(s => s.Tag == "PIA" && s.Comp(2, 2) == "SA");
            if (piaSa is null)
                Warn("PIA", lin.SegmentIndex, lin.LineNumber, "DE2.C2=SA", "DESADV_WARN_001", "desadv.warn.001");
        }
    }

}
