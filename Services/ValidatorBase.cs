using EdifactValidator.Models;

namespace EdifactValidator.Services;

/// <summary>
/// Gemeinsame Basis für alle EDIFACT-Validatoren.
/// Enthält Syntax-Prüfung, Hilfsmethoden und Issue-Verwaltung.
/// </summary>
public abstract class ValidatorBase
{
    protected readonly List<ValidationIssue> _issues = new();
    protected readonly GlnStore _store;

    protected ValidatorBase(GlnStore store) => _store = store;

    // ── Syntax validation (SYN_001–005b, plus nachrichtentyp-spezifische 006) ──

    protected void ValidateSyntax(EdifactInterchange ic,
        string expectedMsgType, string typeErrCode, string typeErrKey)
    {
        if (ic.Unb is null) Err("", 0, 0, "", "SYN_001", "syn.001");
        if (ic.Unz is null) Err("", 0, 0, "", "SYN_002", "syn.002");

        if (ic.Unb is not null && ic.Unz is not null &&
            ic.DeclaredMessageCount >= 0 &&
            ic.DeclaredMessageCount != ic.Messages.Count)
            Err("UNZ", ic.Unz.SegmentIndex, ic.Unz.LineNumber, "DE1", "SYN_003", "syn.003");

        foreach (var msg in ic.Messages)
        {
            if (msg.Unh is null) { Err("UNH", 0, 0, "", "SYN_004", "syn.004"); continue; }
            if (msg.Unt is null) { Err("UNT", msg.Unh.SegmentIndex, msg.Unh.LineNumber, "", "SYN_004", "syn.004"); continue; }

            if (msg.Unh.El(1) != msg.Unt.El(2))
                Err("UNT", msg.Unt.SegmentIndex, msg.Unt.LineNumber, "DE2", "SYN_005", "syn.005");

            if (int.TryParse(msg.Unt.El(1), out var declared))
            {
                var actual = msg.Segments.Count + 2; // UNH + UNT eingeschlossen
                if (declared != actual)
                    Err("UNT", msg.Unt.SegmentIndex, msg.Unt.LineNumber, "DE1", "SYN_005b", "syn.005b");
            }

            if (msg.MessageType != expectedMsgType)
                Err("UNH", msg.Unh.SegmentIndex, msg.Unh.LineNumber, "DE2.C1", typeErrCode, typeErrKey);
        }
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    protected void CheckNad(Dictionary<string, EdifactSegment> nads,
        string qualifier, string code, string key)
    {
        if (!nads.ContainsKey(qualifier))
            Err("NAD", 0, 0, $"DE1={qualifier}", code, key);
    }

    protected static decimal ParseDecimal(string? v) =>
        decimal.TryParse(v?.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0m;

    protected List<ValidationIssue> FinalizeIssues() =>
        _issues.OrderBy(i => i.Severity).ThenBy(i => i.LineNumber).ToList();

    // ── Issue reporting ───────────────────────────────────────────────────────

    protected void Err(string tag, int idx, int line, string el, string code, string key)
    {
        if (!_store.IsRuleEnabled(code)) return;
        var sev = _store.GetSeverity(code, Severity.Error);
        _issues.Add(new ValidationIssue
        {
            Severity = sev, SegmentTag = tag, SegmentIndex = idx,
            LineNumber = line, ElementPosition = el, Code = code, MessageKey = key,
        });
    }

    protected void Warn(string tag, int idx, int line, string el, string code, string key)
    {
        if (!_store.IsRuleEnabled(code)) return;
        var sev = _store.GetSeverity(code, Severity.Warning);
        _issues.Add(new ValidationIssue
        {
            Severity = sev, SegmentTag = tag, SegmentIndex = idx,
            LineNumber = line, ElementPosition = el, Code = code, MessageKey = key,
        });
    }
}
