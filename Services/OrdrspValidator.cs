using EdifactValidator.Models;

namespace EdifactValidator.Services;

/// <summary>
/// Validates an EdifactInterchange against the Porta IT-Service ORDRSP guidelines
/// (Leitfaden zum EDI-ORDRSP Betrieb, 06/2023).
/// </summary>
public class OrdrspValidator : ValidatorBase
{
    public OrdrspValidator(GlnStore store) : base(store) { }

    public List<ValidationIssue> Validate(EdifactInterchange ic)
    {
        _issues.Clear();
        ValidateSyntax(ic);
        foreach (var msg in ic.Messages)
            ValidateOrdrspMessage(msg, ic);
        return _issues
            .OrderBy(i => i.Severity)
            .ThenBy(i => i.LineNumber)
            .ToList();
    }

    // ── Syntax validation (SYN_xxx) ──────────────────────────────────────────

    private void ValidateSyntax(EdifactInterchange ic) =>
        base.ValidateSyntax(ic, "ORDRSP", "ORDRSP_SYN_006", "ordrsp.syn.006");

    // ── Porta ORDRSP validation ───────────────────────────────────────────────

    private void ValidateOrdrspMessage(EdifactMessage msg, EdifactInterchange ic)
    {
        if (msg.MessageType != "ORDRSP") return;

        // ORDRSP_001 — BGM+231 (Auftragsbestätigung), Belegnummer vorhanden
        var bgm = msg.Segments.FirstOrDefault(s => s.Tag == "BGM");
        if (bgm is null)
            Err("BGM", 0, 0, "DE1", "ORDRSP_001", "ordrsp.001");
        else if (bgm.El(1) != "231")
            Err("BGM", bgm.SegmentIndex, bgm.LineNumber, "DE1", "ORDRSP_001", "ordrsp.001");

        if (bgm is not null && string.IsNullOrWhiteSpace(bgm.El(2)))
            Err("BGM", bgm.SegmentIndex, bgm.LineNumber, "DE2", "ORDRSP_002", "ordrsp.002");

        // ORDRSP_003 — Belegdatum DTM+137
        var dtm137 = FindDtm(msg, "137");
        if (dtm137 is null)
            Err("DTM", 0, 0, "DE1.C1=137", "ORDRSP_003", "ordrsp.003");

        // ORDRSP_004 — Bestell-Nr. RFF+ON
        var rffOn = msg.Segments.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "ON");
        if (rffOn is null || string.IsNullOrWhiteSpace(rffOn.Comp(1, 2)))
            Err("RFF", rffOn?.SegmentIndex ?? 0, rffOn?.LineNumber ?? 0, "DE1.C2", "ORDRSP_004", "ordrsp.004");

        var nads = msg.Segments.Where(s => s.Tag == "NAD").ToList();
        var qualifiers = nads.ToDictionary(n => n.El(1), n => n);

        // ORDRSP_005 — NAD+BY (Käufer)
        CheckNad(qualifiers, "BY", "ORDRSP_005", "ordrsp.005");
        // ORDRSP_006 — NAD+SU (Lieferant)
        CheckNad(qualifiers, "SU", "ORDRSP_006", "ordrsp.006");
        // ORDRSP_007 — NAD+DP (Warenempfänger)
        CheckNad(qualifiers, "DP", "ORDRSP_007", "ordrsp.007");

        // ORDRSP_008 — RFF+VA oder RFF+FC (Steuernummer) nach NAD+SU
        var hasVa = msg.Segments.Any(s => s.Tag == "RFF" && s.Comp(1, 1) == "VA");
        var hasFc = msg.Segments.Any(s => s.Tag == "RFF" && s.Comp(1, 1) == "FC");
        if (!hasVa && !hasFc)
        {
            var su = qualifiers.TryGetValue("SU", out var suSeg) ? suSeg : null;
            Err("RFF", su?.SegmentIndex ?? 0, su?.LineNumber ?? 0, "DE1.C1=VA/FC", "ORDRSP_008", "ordrsp.008");
        }

        // ORDRSP_009 — MwSt.-Satz TAX+7+VAT
        var tax = msg.Segments.FirstOrDefault(s => s.Tag == "TAX" && s.El(2) == "VAT");
        if (tax is null)
            Err("TAX", 0, 0, "DE2=VAT", "ORDRSP_009", "ordrsp.009");

        // ORDRSP_010 — Währung CUX+2
        var cux = msg.Segments.FirstOrDefault(s => s.Tag == "CUX");
        if (cux is null || cux.Comp(1, 1) != "2")
            Err("CUX", cux?.SegmentIndex ?? 0, cux?.LineNumber ?? 0, "DE1.C1=2", "ORDRSP_010", "ordrsp.010");

        // ORDRSP_011–017 — Positionsebene
        ValidateLineItems(msg);

        // ORDRSP_018 — UNS+S
        var uns = msg.Segments.FirstOrDefault(s => s.Tag == "UNS");
        if (uns is null || uns.El(1) != "S")
            Err("UNS", uns?.SegmentIndex ?? 0, uns?.LineNumber ?? 0, "DE1=S", "ORDRSP_018", "ordrsp.018");

        // ── Warnungen ────────────────────────────────────────────────────────

        // WARN_001 — Testkennzeichen (geteilt mit INVOIC)
        if (ic.Unb is not null && ic.Unb.El(11) == "1")
            Warn("UNB", ic.Unb.SegmentIndex, ic.Unb.LineNumber, "DE11", "WARN_001", "warn.001");
    }

    // ── Line item validation (ORDRSP_011–017) ────────────────────────────────

    private void ValidateLineItems(EdifactMessage msg)
    {
        var linGroups = GroupByLeader(msg, "LIN").ToList();

        if (linGroups.Count == 0)
        {
            Err("LIN", 0, 0, "", "ORDRSP_011", "ordrsp.011.none");
            return;
        }

        foreach (var (lin, group) in linGroups)
        {

            // ORDRSP_011 — GTIN (LIN DE3.C1)
            var gtin = lin.Comp(3, 1);
            if (string.IsNullOrWhiteSpace(gtin))
                Err("LIN", lin.SegmentIndex, lin.LineNumber, "DE3.C1", "ORDRSP_011", "ordrsp.011");

            // ORDRSP_012 — PIA+SA (Lieferanten-Artikel-Nr.)
            var piaSa = group.FirstOrDefault(s => s.Tag == "PIA" && s.Comp(2, 2) == "SA");
            if (piaSa is null)
                Err("PIA", lin.SegmentIndex, lin.LineNumber, "DE2.C2=SA", "ORDRSP_012", "ordrsp.012");

            // ORDRSP_013 — IMD Beschreibung
            var imd = group.FirstOrDefault(s => s.Tag == "IMD");
            if (imd is null)
                Err("IMD", lin.SegmentIndex, lin.LineNumber, "", "ORDRSP_013", "ordrsp.013");

            // ORDRSP_014 — QTY+21 (bestellte Menge) > 0
            var qty = group.FirstOrDefault(s => s.Tag == "QTY" && s.Comp(1, 1) == "21");
            if (qty is null)
                Err("QTY", lin.SegmentIndex, lin.LineNumber, "DE1.C1=21", "ORDRSP_014", "ordrsp.014");
            else if (ParseDecimal(qty.Comp(1, 2)) <= 0)
                Err("QTY", qty.SegmentIndex, qty.LineNumber, "DE1.C2", "ORDRSP_014b", "ordrsp.014b");

            // ORDRSP_015 — PRI+AAB (Bruttoeinzelpreis)
            var priAab = group.FirstOrDefault(s => s.Tag == "PRI" && s.Comp(1, 1) == "AAB");
            if (priAab is null)
                Err("PRI", lin.SegmentIndex, lin.LineNumber, "DE1.C1=AAB", "ORDRSP_015", "ordrsp.015");

            // ORDRSP_016 — PRI+AAA (Nettoeinzelpreis, Pflicht seit 06/2023)
            var priAaa = group.FirstOrDefault(s => s.Tag == "PRI" && s.Comp(1, 1) == "AAA");
            if (priAaa is null)
                Err("PRI", lin.SegmentIndex, lin.LineNumber, "DE1.C1=AAA", "ORDRSP_016", "ordrsp.016");

            // ORDRSP_017 — MOA+203 Positionsbetrag (wenn vorhanden, darf nicht negativ sein)
            var moa203 = group.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "203");
            if (moa203 is not null && ParseDecimal(moa203.Comp(1, 2)) < 0)
                Err("MOA", moa203.SegmentIndex, moa203.LineNumber, "DE1.C2", "ORDRSP_017", "ordrsp.017");

            // ORDRSP_WARN_001 — PIA+BP (Kunden-interne Artikel-Nr.) fehlt
            var piaBp = group.FirstOrDefault(s => s.Tag == "PIA" && s.Comp(2, 2) == "BP");
            if (piaBp is null)
                Warn("PIA", lin.SegmentIndex, lin.LineNumber, "DE2.C2=BP", "ORDRSP_WARN_001", "ordrsp.warn.001");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static EdifactSegment? FindDtm(EdifactMessage msg, string qualifier) =>
        msg.Segments.FirstOrDefault(s => s.Tag == "DTM" && s.Comp(1, 1) == qualifier);

}
