using EdifactValidator.Models;

namespace EdifactValidator.Services;

/// <summary>
/// Validates an EdifactInterchange against the Porta IT-Service INVOIC guidelines
/// (Leitfaden zum EDI-INVOIC Betrieb, 2023).
/// </summary>
public class PortaValidator : ValidatorBase
{
    public PortaValidator(GlnStore store) : base(store) { }

    public List<ValidationIssue> Validate(EdifactInterchange ic)
    {
        _issues.Clear();
        ValidateSyntax(ic);
        foreach (var msg in ic.Messages)
            ValidateInvoicMessage(msg, ic);
        return _issues
            .OrderBy(i => i.Severity)
            .ThenBy(i => i.LineNumber)
            .ToList();
    }

    // ── Syntax validation (SYN_xxx) ──────────────────────────────────────────

    private void ValidateSyntax(EdifactInterchange ic) =>
        base.ValidateSyntax(ic, "INVOIC", "SYN_006", "syn.006");

    // ── Porta INVOIC validation ──────────────────────────────────────────────

    private void ValidateInvoicMessage(EdifactMessage msg, EdifactInterchange ic)
    {
        if (msg.MessageType != "INVOIC") return;

        msg.DetectedVersion = msg.VersionString switch
        {
            "96A" => EdifactVersion.D96A,
            "01B" => EdifactVersion.D01B,
            _     => EdifactVersion.Unknown,
        };

        // PORTA_001 — GLN Datenabsender in UNB (genau 13 Stellen)
        var unb = ic.Unb;
        if (unb is null)
        {
            Err("UNB", 0, 0, "DE2.C1", "PORTA_001", "porta.001");
        }
        else
        {
            var gln = unb.Comp(2, 1);
            if (string.IsNullOrWhiteSpace(gln))
                Err("UNB", unb.SegmentIndex, unb.LineNumber, "DE2.C1", "PORTA_001", "porta.001");
            else if (gln.Length != 13 || !gln.All(char.IsDigit))
                Err("UNB", unb.SegmentIndex, unb.LineNumber, "DE2.C1", "PORTA_001b", "porta.001b");
        }

        var nads = msg.Segments.Where(s => s.Tag == "NAD").ToList();
        var qualifiers = nads.ToDictionary(n => n.El(1), n => n);

        // PORTA_002 — NAD+BY
        CheckNad(qualifiers, "BY", "PORTA_002", "porta.002");
        // PORTA_003 — NAD+IV
        CheckNad(qualifiers, "IV", "PORTA_003", "porta.003");
        // PORTA_004 — NAD+SU
        CheckNad(qualifiers, "SU", "PORTA_004", "porta.004");

        // PORTA_005 — RFF+VA oder RFF+FC (mind. eine, nach NAD+SU)
        var hasVa = msg.Segments.Any(s => s.Tag == "RFF" && s.Comp(1, 1) == "VA");
        var hasFc = msg.Segments.Any(s => s.Tag == "RFF" && s.Comp(1, 1) == "FC");
        if (!hasVa && !hasFc)
        {
            var su = qualifiers.TryGetValue("SU", out var suSeg) ? suSeg : null;
            Err("RFF", su?.SegmentIndex ?? 0, su?.LineNumber ?? 0, "DE1.C1=VA/FC", "PORTA_005", "porta.005");
        }

        // PORTA_006 — BGM+380 (keine Gutschriften)
        var bgm = msg.Segments.FirstOrDefault(s => s.Tag == "BGM");
        if (bgm is null)
            Err("BGM", 0, 0, "DE1", "PORTA_006", "porta.006");
        else if (bgm.El(1) != "380")
            Err("BGM", bgm.SegmentIndex, bgm.LineNumber, "DE1", "PORTA_006", "porta.006");

        // PORTA_007 — Belegnummer vorhanden
        if (bgm is not null && string.IsNullOrWhiteSpace(bgm.El(2)))
            Err("BGM", bgm.SegmentIndex, bgm.LineNumber, "DE2", "PORTA_007", "porta.007");

        // PORTA_008 — Belegdatum DTM+137
        var dtm137 = FindDtm(msg, "137");
        if (dtm137 is null)
            Err("DTM", 0, 0, "DE1.C1=137", "PORTA_008", "porta.008");

        // PORTA_009 — Lieferdatum DTM+35
        var dtm35 = FindDtm(msg, "35");
        if (dtm35 is null)
            Err("DTM", 0, 0, "DE1.C1=35", "PORTA_009", "porta.009");

        // PORTA_010 — Bestell-Nr. RFF+ON
        var rffOn = msg.Segments.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "ON");
        if (rffOn is null || string.IsNullOrWhiteSpace(rffOn.Comp(1, 2)))
            Err("RFF", rffOn?.SegmentIndex ?? 0, rffOn?.LineNumber ?? 0, "DE1.C2", "PORTA_010", "porta.010");

        // PORTA_011 + PORTA_012 — Lieferschein-Nr. RFF+DQ vorhanden + max 10 Stellen
        var rffDq = msg.Segments.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "DQ");
        if (rffDq is null || string.IsNullOrWhiteSpace(rffDq.Comp(1, 2)))
            Err("RFF", rffDq?.SegmentIndex ?? 0, rffDq?.LineNumber ?? 0, "DE1.C2", "PORTA_011", "porta.011");
        else if (rffDq.Comp(1, 2).Length > 10)
            Err("RFF", rffDq.SegmentIndex, rffDq.LineNumber, "DE1.C2", "PORTA_012", "porta.012");

        // PORTA_013 — Lieferschein-Datum DTM+171 nach RFF+DQ
        if (rffDq is not null)
        {
            var dqIdx  = msg.Segments.IndexOf(rffDq);
            var dtm171 = msg.Segments
                .Skip(dqIdx + 1)
                .TakeWhile(s => s.Tag is "DTM" or "RFF")
                .FirstOrDefault(s => s.Tag == "DTM" && s.Comp(1, 1) == "171");
            if (dtm171 is null)
                Err("DTM", rffDq.SegmentIndex, rffDq.LineNumber, "DE1.C1=171", "PORTA_013", "porta.013");
        }

        // PORTA_014 — Kunden-Nr. RFF+API
        var rffApi = msg.Segments.FirstOrDefault(s => s.Tag == "RFF" && s.Comp(1, 1) == "API");
        if (rffApi is null || string.IsNullOrWhiteSpace(rffApi.Comp(1, 2)))
            Err("RFF", rffApi?.SegmentIndex ?? 0, rffApi?.LineNumber ?? 0, "DE1.C2", "PORTA_014", "porta.014");

        // PORTA_015 — MwSt-Satz TAX+7+VAT
        var tax = msg.Segments.FirstOrDefault(s => s.Tag == "TAX" && s.El(2) == "VAT");
        if (tax is null)
            Err("TAX", 0, 0, "DE2=VAT", "PORTA_015", "porta.015");

        // PORTA_016 — Währung CUX+2
        var cux = msg.Segments.FirstOrDefault(s => s.Tag == "CUX");
        if (cux is null || cux.Comp(1, 1) != "2")
            Err("CUX", cux?.SegmentIndex ?? 0, cux?.LineNumber ?? 0, "DE1.C1=2", "PORTA_016", "porta.016");

        // PORTA_017–021 — Positionsebene
        ValidateLineItems(msg);

        // PORTA_022 — UNS+S
        var uns = msg.Segments.FirstOrDefault(s => s.Tag == "UNS");
        if (uns is null || uns.El(1) != "S")
            Err("UNS", uns?.SegmentIndex ?? 0, uns?.LineNumber ?? 0, "DE1=S", "PORTA_022", "porta.022");

        // PORTA_023–026 — Summenfelder
        CheckMoa(msg, "77",  "PORTA_023", "porta.023");
        CheckMoa(msg, "79",  "PORTA_024", "porta.024");
        CheckMoa(msg, "125", "PORTA_025", "porta.025");
        CheckMoa(msg, "124", "PORTA_026", "porta.026");

        // PORTA_027 — Null-Rechnung (only when MOA+77 is present and value ≤ 0)
        var moa77Seg = msg.Segments.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "77");
        if (moa77Seg is not null && ParseDecimal(moa77Seg.Comp(1, 2)) <= 0)
            Err("MOA", moa77Seg.SegmentIndex, moa77Seg.LineNumber, "DE1.C1=77", "PORTA_027", "porta.027");

        // PORTA_028 — Rechnungsdatum nicht vor Lieferdatum
        if (dtm137 is not null && dtm35 is not null)
        {
            var invDate  = ParseEdifactDate(dtm137.Comp(1, 2), dtm137.Comp(1, 3));
            var delDate  = ParseEdifactDate(dtm35.Comp(1, 2), dtm35.Comp(1, 3));
            if (invDate.HasValue && delDate.HasValue && invDate.Value < delDate.Value)
                Err("DTM", dtm137.SegmentIndex, dtm137.LineNumber, "DE1.C1=137", "PORTA_028", "porta.028");
        }

        // PORTA_029–031 — ALC Gruppen
        ValidateAlcGroups(msg);

        // ── Warnungen ───────────────────────────────────────────────────────

        // WARN_001 — Testkennzeichen
        if (ic.Unb is not null && ic.Unb.El(11) == "1")
            Warn("UNB", ic.Unb.SegmentIndex, ic.Unb.LineNumber, "DE11", "WARN_001", "warn.001");

        // WARN_002 — Skonto (PAT+22)
        var pat = msg.Segments.FirstOrDefault(s => s.Tag == "PAT" && s.El(1) == "22");
        if (pat is not null)
            Warn("PAT", pat.SegmentIndex, pat.LineNumber, "DE1=22", "WARN_002", "warn.002");

        // WARN_003 — Kunden-interne Artikel-Nr. (PIA+1+...BP) fehlt
        var hasKundenNr = msg.Segments.Any(s => s.Tag == "PIA" && s.Comp(2, 2) == "BP");
        if (!hasKundenNr && msg.Segments.Any(s => s.Tag == "LIN"))
            Warn("PIA", 0, 0, "DE2.C2=BP", "WARN_003", "warn.003");

        // WARN_004 — FTX+AAK Textschlüssel prüfen
        foreach (var ftx in msg.Segments.Where(s => s.Tag == "FTX" && s.El(1) == "AAK"))
        {
            var code = ftx.El(3);
            if (code is not "ST1" and not "ST2" and not "ST3" && !string.IsNullOrWhiteSpace(code))
                Warn("FTX", ftx.SegmentIndex, ftx.LineNumber, "DE3", "WARN_004", "warn.004");
        }
    }

    // ── Line item validation (PORTA_017–021) ─────────────────────────────────

    private void ValidateLineItems(EdifactMessage msg)
    {
        var linSegs = msg.Segments.Where(s => s.Tag == "LIN").ToList();

        if (!linSegs.Any())
        {
            Err("LIN", 0, 0, "", "PORTA_017", "porta.017.none");
            return;
        }

        foreach (var lin in linSegs)
        {
            var linIdx = msg.Segments.IndexOf(lin);
            var nextLin = msg.Segments.Skip(linIdx + 1).FirstOrDefault(s => s.Tag == "LIN");
            var nextLinIdx = nextLin is not null ? msg.Segments.IndexOf(nextLin) : msg.Segments.Count;
            var group = msg.Segments.Skip(linIdx).Take(nextLinIdx - linIdx).ToList();

            // PORTA_017 — GTIN vorhanden (LIN DE3 component 1)
            var gtin = lin.Comp(3, 1);
            if (string.IsNullOrWhiteSpace(gtin))
                Err("LIN", lin.SegmentIndex, lin.LineNumber, "DE3.C1", "PORTA_017", "porta.017");

            // PORTA_018 — IMD Beschreibung vorhanden
            var imd = group.FirstOrDefault(s => s.Tag == "IMD");
            if (imd is null)
                Err("IMD", lin.SegmentIndex, lin.LineNumber, "", "PORTA_018", "porta.018");

            // PORTA_019 — QTY+47 > 0
            var qty = group.FirstOrDefault(s => s.Tag == "QTY" && s.Comp(1, 1) == "47");
            if (qty is null)
                Err("QTY", lin.SegmentIndex, lin.LineNumber, "DE1.C1=47", "PORTA_019", "porta.019");
            else if (ParseDecimal(qty.Comp(1, 2)) <= 0)
                Err("QTY", qty.SegmentIndex, qty.LineNumber, "DE1.C2", "PORTA_019b", "porta.019b");

            // PORTA_020 — MOA+203 nicht negativ
            var moa203 = group.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == "203");
            if (moa203 is null)
                Err("MOA", lin.SegmentIndex, lin.LineNumber, "DE1.C1=203", "PORTA_020", "porta.020");
            else if (ParseDecimal(moa203.Comp(1, 2)) < 0)
                Err("MOA", moa203.SegmentIndex, moa203.LineNumber, "DE1.C2", "PORTA_020b", "porta.020b");

            // PORTA_021 — PRI+AAB vorhanden
            var pri = group.FirstOrDefault(s => s.Tag == "PRI");
            if (pri is null)
                Err("PRI", lin.SegmentIndex, lin.LineNumber, "DE1.C1=AAB", "PORTA_021", "porta.021");
            else if (pri.Comp(1, 1) != "AAB")
                Err("PRI", pri.SegmentIndex, pri.LineNumber, "DE1.C1", "PORTA_021b", "porta.021b");
        }
    }

    // ── ALC group validation (PORTA_029–031) ─────────────────────────────────

    private void ValidateAlcGroups(EdifactMessage msg)
    {
        var alcs = msg.Segments.Where(s => s.Tag == "ALC").ToList();
        foreach (var alc in alcs)
        {
            var alIdx   = msg.Segments.IndexOf(alc);
            var nextAlc = msg.Segments.Skip(alIdx + 1)
                .FirstOrDefault(s => s.Tag is "ALC" or "LIN" or "UNS" or "UNT");
            var endIdx  = nextAlc is not null ? msg.Segments.IndexOf(nextAlc) : msg.Segments.Count;
            var group   = msg.Segments.Skip(alIdx + 1).Take(endIdx - alIdx - 1).ToList();

            var hasMoa8 = group.Any(s => s.Tag == "MOA" && s.Comp(1, 1) == "8");
            var hasPcd3 = group.Any(s => s.Tag == "PCD" && s.Comp(1, 1) == "3");

            if (!hasMoa8)
                Err("MOA", alc.SegmentIndex, alc.LineNumber, "DE1.C1=8", "PORTA_029", "porta.029");
            if (!hasPcd3)
                Err("PCD", alc.SegmentIndex, alc.LineNumber, "DE1.C1=3", "PORTA_030", "porta.030");
        }

        // PORTA_031 — Artikel-ALC (SG38): wenn ALC bei Artikelposition → MOA+131 muss da sein
        var linSegs = msg.Segments.Where(s => s.Tag == "LIN").ToList();
        foreach (var lin in linSegs)
        {
            var linIdx  = msg.Segments.IndexOf(lin);
            var nextLin = msg.Segments.Skip(linIdx + 1).FirstOrDefault(s => s.Tag == "LIN");
            var endIdx  = nextLin is not null ? msg.Segments.IndexOf(nextLin) : msg.Segments.Count;
            var group   = msg.Segments.Skip(linIdx).Take(endIdx - linIdx).ToList();

            var hasAlc    = group.Any(s => s.Tag == "ALC");
            var hasMoa131 = group.Any(s => s.Tag == "MOA" && s.Comp(1, 1) == "131");

            if (hasAlc && !hasMoa131)
                Err("MOA", lin.SegmentIndex, lin.LineNumber, "DE1.C1=131", "PORTA_031", "porta.031");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static EdifactSegment? FindDtm(EdifactMessage msg, string qualifier) =>
        msg.Segments.FirstOrDefault(s => s.Tag == "DTM" && s.Comp(1, 1) == qualifier);

    private void CheckMoa(EdifactMessage msg, string qualifier, string code, string key)
    {
        var moa = msg.Segments.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == qualifier);
        if (moa is null || string.IsNullOrWhiteSpace(moa.Comp(1, 2)))
            Err("MOA", moa?.SegmentIndex ?? 0, moa?.LineNumber ?? 0, $"DE1.C1={qualifier}", code, key);
    }

    private static decimal GetMoaValue(EdifactMessage msg, string qualifier)
    {
        var moa = msg.Segments.FirstOrDefault(s => s.Tag == "MOA" && s.Comp(1, 1) == qualifier);
        return moa is not null ? ParseDecimal(moa.Comp(1, 2)) : 0m;
    }

    private static DateOnly? ParseEdifactDate(string? value, string? format)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (format == "102" && value.Length == 8 &&
            int.TryParse(value[..4], out var y) &&
            int.TryParse(value[4..6], out var m) &&
            int.TryParse(value[6..], out var d))
            return new DateOnly(y, m, d);
        return null;
    }

}
