using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using EdifactValidator.Models;

// Password format: "pbkdf2:{iterations}:{salt_hex}:{hash_hex}"
// Legacy format (migration): 64-char hex SHA-256 string

namespace EdifactValidator.Services;

public class GlnStore
{
    private readonly IJSRuntime _js;
    private const string EntriesKey = "gln_entries";
    private const string CredsKey   = "gln_creds";
    private const string RulesKey   = "rule_configs";
    private const string StatsKey   = "validation_stats";

    public List<GlnEntry> Entries { get; private set; } = new();
    public bool IsAuthenticated { get; private set; }
    public bool Initialized { get; private set; }
    public event Action? Changed;
    public Dictionary<string, RuleConfig> Rules { get; private set; } = new();
    public ValidationStats Stats { get; private set; } = new();

    private string _credUser;
    private string _credPassword = ""; // stored as "pbkdf2:{iterations}:{salt_hex}:{hash_hex}"
    private string _pendingDefaultPassword = ""; // plaintext until first InitAsync, then cleared

    // 1_000 iterations: near-instant in WASM single thread (~5 ms), still provides salt-based protection.
    // Key stretching has minimal value here because the hash lives in client-side LocalStorage anyway.
    private const int Pbkdf2Iterations = 1_000;

    private static string HashPassword(string pw)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pw), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2:{Pbkdf2Iterations}:{Convert.ToHexString(salt).ToLowerInvariant()}:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static bool VerifyPassword(string pw, string stored)
    {
        if (stored.StartsWith("pbkdf2:"))
        {
            var parts = stored.Split(':');
            if (parts.Length != 4 ||
                !int.TryParse(parts[1], out var iterations)) return false;
            try
            {
                var salt = Convert.FromHexString(parts[2]);
                var expected = Convert.FromHexString(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(pw), salt, iterations, HashAlgorithmName.SHA256, 32);
                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch { return false; }
        }
        // Legacy: SHA-256 hex
        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pw))).ToLowerInvariant();
        return sha256 == stored;
    }

    public GlnStore(IJSRuntime js, IConfiguration config)
    {
        _js                    = js;
        _credUser              = config["Admin:DefaultUser"]     ?? "admin";
        _pendingDefaultPassword = config["Admin:DefaultPassword"] ?? string.Empty;
        // HashPassword is NOT called here — PBKDF2 must not block the WASM UI thread at startup.
        // It is deferred to InitAsync which runs in an async context.
    }

    public async Task InitAsync()
    {
        if (Initialized) return;

        // Load credentials into memory
        bool credsLoaded = false;
        try
        {
            var cj = await _js.InvokeAsync<string?>("ls.get", CredsKey);
            if (!string.IsNullOrEmpty(cj))
            {
                var d = JsonSerializer.Deserialize<JsonElement>(cj);
                _credUser = d.GetProperty("user").GetString() ?? "admin";
                var stored = d.GetProperty("password").GetString() ?? "";
                if (stored.StartsWith("pbkdf2:"))
                {
                    // Already PBKDF2 — use as-is
                    _credPassword = stored;
                }
                else if (stored.Length == 64 && stored.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                {
                    // Legacy SHA-256 — keep for now, will upgrade to PBKDF2 on next login
                    _credPassword = stored;
                }
                else
                {
                    // Plaintext — upgrade to PBKDF2 immediately (plaintext available here)
                    _credPassword = HashPassword(stored);
                    var migrated = JsonSerializer.Serialize(new { user = _credUser, password = _credPassword });
                    await _js.InvokeVoidAsync("ls.set", CredsKey, migrated);
                }
                credsLoaded = true;
            }
        }
        catch { /* keep defaults */ }

        // No stored creds: hash the default password now (safe — we are inside async InitAsync)
        if (!credsLoaded)
        {
            _credPassword = HashPassword(_pendingDefaultPassword);
        }
        _pendingDefaultPassword = string.Empty; // clear plaintext from memory

        // Load GLN entries
        bool entriesLoaded = false;
        try
        {
            var json = await _js.InvokeAsync<string?>("ls.get", EntriesKey);
            if (!string.IsNullOrEmpty(json))
            {
                var saved = JsonSerializer.Deserialize<List<GlnEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (saved is { Count: > 0 }) { Entries = saved; entriesLoaded = true; }
            }
        }
        catch { /* fall back to defaults */ }

        if (!entriesLoaded)
            Entries = GlnDatabase.All.ToList();

        // Load rule configs
        try
        {
            var rj = await _js.InvokeAsync<string?>("ls.get", RulesKey);
            if (!string.IsNullOrEmpty(rj))
            {
                var saved = JsonSerializer.Deserialize<List<RuleConfig>>(rj,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (saved != null)
                    Rules = saved.ToDictionary(r => r.Code);
            }
        }
        catch { }
        // Fill missing rules with defaults
        foreach (var (code, ov) in DefaultRuleOverrides.Where(kv => !Rules.ContainsKey(kv.Key)))
            Rules[code] = new RuleConfig { Code = code, Enabled = true, Override = ov };

        // Migrate: apply new Warning defaults to rules that were never manually overridden
        bool rulesMigrated = false;
        foreach (var (code, ov) in DefaultRuleOverrides.Where(kv => kv.Value != null))
            if (Rules.TryGetValue(code, out var r) && r.Override == null)
            { Rules[code] = r with { Override = ov }; rulesMigrated = true; }
        if (rulesMigrated)
        {
            var json2 = JsonSerializer.Serialize(Rules.Values.ToList());
            await _js.InvokeVoidAsync("ls.set", RulesKey, json2);
        }

        // Load stats
        try
        {
            var sj = await _js.InvokeAsync<string?>("ls.get", StatsKey);
            if (!string.IsNullOrEmpty(sj))
                Stats = JsonSerializer.Deserialize<ValidationStats>(sj,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { }

        Initialized = true;
    }

    public async Task<bool> LoginAsync(string user, string password)
    {
        await Task.Yield(); // yield to UI before blocking computation
        IsAuthenticated = user.Trim() == _credUser && VerifyPassword(password, _credPassword);
        if (IsAuthenticated && !_credPassword.StartsWith("pbkdf2:"))
            await SaveCredentialsAsync(_credUser, password); // Upgrade legacy hash to PBKDF2
        Changed?.Invoke();
        return IsAuthenticated;
    }

    public void Logout() { IsAuthenticated = false; Changed?.Invoke(); }

    public async Task AddAsync(GlnEntry entry)
    {
        Entries.Add(entry);
        await PersistAsync();
    }

    public async Task UpdateAsync(GlnEntry original, GlnEntry updated)
    {
        var idx = Entries.FindIndex(e => e == original);
        if (idx >= 0) Entries[idx] = updated;
        await PersistAsync();
    }

    public async Task DeleteAsync(GlnEntry entry)
    {
        Entries.Remove(entry);
        await PersistAsync();
    }

    public async Task ResetToDefaultsAsync()
    {
        Entries = GlnDatabase.All.ToList();
        await _js.InvokeVoidAsync("ls.del", EntriesKey);
        Changed?.Invoke();
    }

    public async Task SaveCredentialsAsync(string user, string password)
    {
        _credUser     = user.Trim();
        _credPassword = HashPassword(password);
        var json = JsonSerializer.Serialize(new { user = _credUser, password = _credPassword });
        await _js.InvokeVoidAsync("ls.set", CredsKey, json);
    }

    private async Task PersistAsync()
    {
        var json = JsonSerializer.Serialize(Entries);
        await _js.InvokeVoidAsync("ls.set", EntriesKey, json);
        Changed?.Invoke();
    }

    public bool IsRuleEnabled(string code) =>
        !Rules.TryGetValue(code, out var r) || r.Enabled;

    public Severity GetSeverity(string code, Severity defaultSev) =>
        Rules.TryGetValue(code, out var r) && r.Override.HasValue
            ? r.Override.Value : defaultSev;

    public async Task SaveRulesAsync()
    {
        var json = JsonSerializer.Serialize(Rules.Values.ToList());
        await _js.InvokeVoidAsync("ls.set", RulesKey, json);
        Changed?.Invoke();
    }

    public async Task ResetRulesAsync()
    {
        Rules = DefaultRuleOverrides.ToDictionary(
            kv => kv.Key,
            kv => new RuleConfig { Code = kv.Key, Enabled = true, Override = kv.Value });
        await _js.InvokeVoidAsync("ls.del", RulesKey);
        Changed?.Invoke();
    }

    public async Task RecordValidationAsync(List<ValidationIssue> issues, string? sellerName = null)
    {
        var errors   = issues.Count(i => i.Severity == Severity.Error);
        var warnings = issues.Count(i => i.Severity == Severity.Warning);
        var isTest   = issues.Any(i => i.Code == "WARN_001");
        var date     = DateTime.Now.ToString("dd.MM.yyyy");

        Stats.TotalRuns++;
        if (errors == 0) Stats.TotalValid++;
        if (isTest)  Stats.TestFileCount++;

        Stats.RunsByDate[date] = Stats.RunsByDate.GetValueOrDefault(date) + 1;

        if (!string.IsNullOrWhiteSpace(sellerName))
            Stats.SellerFrequency[sellerName] = Stats.SellerFrequency.GetValueOrDefault(sellerName) + 1;

        foreach (var i in issues)
            Stats.RuleFrequency[i.Code] = Stats.RuleFrequency.GetValueOrDefault(i.Code) + 1;

        Stats.RecentRuns.Insert(0, new ValidationRun
        {
            Timestamp    = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
            ErrorCount   = errors,
            WarningCount = warnings,
            IsValid      = errors == 0,
            IsTest       = isTest,
            SellerName   = sellerName ?? "",
        });
        if (Stats.RecentRuns.Count > 20)
            Stats.RecentRuns = Stats.RecentRuns.Take(20).ToList();
        var json = JsonSerializer.Serialize(Stats);
        await _js.InvokeVoidAsync("ls.set", StatsKey, json);
    }

    public async Task ResetStatsAsync()
    {
        Stats = new ValidationStats();
        await _js.InvokeVoidAsync("ls.del", StatsKey);
        Changed?.Invoke();
    }

    // Default severity overrides derived from analysis of 20 valid INVOIC test files (2026-03).
    // null  = use the rule's own default severity (Error for most rules)
    // Severity.Warning = downgraded because the rule fires on at least one of the 20 valid files
    private static readonly Dictionary<string, Severity?> DefaultRuleOverrides = new()
    {
        // ── Syntax (all clean in every test file) ──────────────────────────────
        { "SYN_001",   null }, { "SYN_002",   null }, { "SYN_003",   null },
        { "SYN_004",   null }, { "SYN_005",   null }, { "SYN_005b",  Severity.Warning }, // ROTHO: Sender zählt UNH/UNT nicht mit
        { "SYN_006",   null },

        // ── INVOIC Pflichtfelder ───────────────────────────────────────────────
        { "PORTA_001",  null },               // GLN Absender immer vorhanden
        { "PORTA_001b", null },               // GLN immer 13-stellig
        { "PORTA_002",  Severity.Warning },   // MATHEIS (Gutschrift BGM+393) hat kein NAD+BY
        { "PORTA_003",  Severity.Warning },   // MATHEIS hat kein NAD+IV
        { "PORTA_004",  null },               // NAD+SU immer vorhanden
        { "PORTA_005",  Severity.Warning },   // MATHEIS hat weder RFF+VA noch RFF+FC
        { "PORTA_006",  Severity.Warning },   // MATHEIS: BGM+393 (Gutschrift), nicht 380
        { "PORTA_007",  null },               // Belegnummer immer vorhanden
        { "PORTA_008",  null },               // DTM+137 immer vorhanden
        { "PORTA_009",  Severity.Warning },   // MATHEIS hat kein DTM+35
        { "PORTA_010",  Severity.Warning },   // MATHEIS hat keine RFF+ON
        { "PORTA_011",  Severity.Warning },   // MATHEIS hat keine RFF+DQ
        { "PORTA_012",  Severity.Warning },   // KleineWolke (13Z), Steinpol (14Z) überschreiten 10
        { "PORTA_013",  Severity.Warning },   // HIMOLLA: DTM+171 nicht direkt nach RFF+DQ
        { "PORTA_014",  Severity.Warning },   // 15 von 20 Dateien ohne RFF+API
        { "PORTA_015",  null },               // TAX+7+VAT immer vorhanden
        { "PORTA_016",  null },               // CUX+2 immer vorhanden
        { "PORTA_017",  Severity.Warning },   // HIMOLLA (Dienstl.-Pos.), Steinpol (leere GTIN), MATHEIS
        { "PORTA_018",  Severity.Warning },   // HIMOLLA: IMD+B statt IMD+A; MATHEIS: keine Pos.
        { "PORTA_019",  null },               // QTY+47 immer vorhanden (wo Pos. existieren)
        { "PORTA_019b", null },               // Menge immer > 0
        { "PORTA_020",  Severity.Warning },   // BOSCH Pos.3+4 und MATHEIS ohne MOA+203
        { "PORTA_020b", null },               // Betrag nie negativ
        { "PORTA_021",  Severity.Warning },   // BOSCH Pos.3+4 und MATHEIS ohne PRI
        { "PORTA_021b", Severity.Warning },   // KleineWolke: nur PRI+AAA (kein AAB); MATHEIS
        { "PORTA_022",  null },               // UNS+S immer vorhanden
        { "PORTA_023",  Severity.Warning },   // MATHEIS hat kein MOA+77
        { "PORTA_024",  Severity.Warning },   // MATHEIS hat kein MOA+79
        { "PORTA_025",  null },               // MOA+125 immer vorhanden
        { "PORTA_026",  null },               // MOA+124 immer vorhanden
        { "PORTA_027",  null },               // MOA+77 immer > 0 (wenn vorhanden)
        { "PORTA_028",  null },               // Rechnungsdatum nie vor Lieferdatum
        { "PORTA_029",  Severity.Warning },   // COTTA: MOA+21 statt MOA+8; MONDEX: kein MOA+8 in ALC
        { "PORTA_030",  Severity.Warning },   // BOSCH/NEFF: Header-ALC ohne PCD+3
        { "PORTA_031",  Severity.Warning },   // AMICA, BIEDERLACK, MONDEX: Artikel-ALC ohne MOA+131

        // ── Generic Warnings (already Warning by default) ─────────────────────
        { "WARN_001", null }, { "WARN_002", null }, { "WARN_003", null }, { "WARN_004", null },

        // ── ORDRSP ────────────────────────────────────────────────────────────
        { "ORDRSP_SYN_006", null },
        { "ORDRSP_001", null }, { "ORDRSP_002", null },
        { "ORDRSP_003", Severity.Warning },   // GLOBO: DTM+4 statt DTM+137
        { "ORDRSP_004", null }, { "ORDRSP_005", null }, { "ORDRSP_006", null },
        { "ORDRSP_007", null },
        { "ORDRSP_008", Severity.Warning },   // 20/21 Dateien: kein RFF+VA/FC
        { "ORDRSP_009", Severity.Warning },   // BENFORMATO, GLOBO, INTERLINK: kein TAX+VAT
        { "ORDRSP_010", null }, { "ORDRSP_011", null },
        { "ORDRSP_012", Severity.Warning },   // BOENNINGHOFF: nur PIA+BP, keine SA
        { "ORDRSP_013", null }, { "ORDRSP_014", null }, { "ORDRSP_014b", null },
        { "ORDRSP_015", null },
        { "ORDRSP_016", Severity.Warning },   // GLOBO, MAEUSBACHER, REALITY-LEUCHTEN, TRIOLEUCHTEN: kein PRI+AAA
        { "ORDRSP_017", null },
        { "ORDRSP_018", null }, { "ORDRSP_WARN_001", null },

        // ── DESADV ────────────────────────────────────────────────────────────
        { "DESADV_SYN_006", null },
        { "DESADV_001", null }, { "DESADV_002", null }, { "DESADV_003", null },
        { "DESADV_004", null }, { "DESADV_005", null }, { "DESADV_006", null },
        { "DESADV_007", Severity.Warning },   // 21/21 Dateien: kein RFF+VA/FC (DESADV ist kein Steuerbeleg)
        { "DESADV_008", null },
        { "DESADV_009", Severity.Warning },   // DE-EEKHOORN, FLEXWELL, NORDLUX, ROBA: kein PAC
        { "DESADV_010", null }, { "DESADV_011", null }, { "DESADV_011b", null },
        { "DESADV_012", Severity.Warning },   // 7/21: DTM+79 auf Header-Ebene statt in LIN-Gruppe
        { "DESADV_013", Severity.Warning },   // 7/21: DTM+76 auf Header-Ebene statt in LIN-Gruppe
        { "DESADV_014", Severity.Warning },   // 14/21: RFF+DQ auf Header-Ebene oder fehlt in LIN-Gruppe
        { "DESADV_016", Severity.Warning },   // 21/21 Dateien: kein UNS+S (DESADV endet direkt mit UNT)
        { "DESADV_WARN_001", null }, { "DESADV_WARN_002", null }, { "DESADV_WARN_003", null },

        // ── INVRPT ────────────────────────────────────────────────────────────
        { "INVRPT_SYN_006", null },
        { "INVRPT_001", null }, { "INVRPT_002", null }, { "INVRPT_003", null },
        { "INVRPT_004", null }, { "INVRPT_005", null }, { "INVRPT_006", null },
        { "INVRPT_007", null }, { "INVRPT_008", null }, { "INVRPT_008b", null },
        { "INVRPT_009", null }, { "INVRPT_010", null },
        { "INVRPT_WARN_002", null }, { "INVRPT_WARN_003", null },
    };
}
