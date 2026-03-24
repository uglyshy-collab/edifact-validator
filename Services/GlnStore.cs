using System.Text.Json;
using Microsoft.JSInterop;
using EdifactValidator.Models;

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

    private string _credUser     = "admin";
    private string _credPassword = "porta2024";

    public GlnStore(IJSRuntime js) => _js = js;

    public async Task InitAsync()
    {
        if (Initialized) return;

        // Load credentials into memory
        try
        {
            var cj = await _js.InvokeAsync<string?>("ls.get", CredsKey);
            if (!string.IsNullOrEmpty(cj))
            {
                var d = JsonSerializer.Deserialize<JsonElement>(cj);
                _credUser     = d.GetProperty("user").GetString()     ?? "admin";
                _credPassword = d.GetProperty("password").GetString() ?? "porta2024";
            }
        }
        catch { /* keep defaults */ }

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
        foreach (var code in DefaultRuleCodes.Where(c => !Rules.ContainsKey(c)))
            Rules[code] = new RuleConfig { Code = code, Enabled = true, Override = null };

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

    public bool Login(string user, string password)
    {
        IsAuthenticated = user.Trim() == _credUser && password == _credPassword;
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
        _credPassword = password;
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
        Rules = DefaultRuleCodes.ToDictionary(c => c, c => new RuleConfig { Code = c });
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

    private static readonly string[] DefaultRuleCodes =
    {
        "SYN_001","SYN_002","SYN_003","SYN_004","SYN_005","SYN_005b","SYN_006",
        "PORTA_001","PORTA_001b","PORTA_002","PORTA_003","PORTA_004","PORTA_005",
        "PORTA_006","PORTA_007","PORTA_008","PORTA_009","PORTA_010","PORTA_011",
        "PORTA_012","PORTA_013","PORTA_014","PORTA_015","PORTA_016","PORTA_017",
        "PORTA_018","PORTA_019","PORTA_019b","PORTA_020","PORTA_020b",
        "PORTA_021","PORTA_021b","PORTA_022","PORTA_023","PORTA_024","PORTA_025",
        "PORTA_026","PORTA_027","PORTA_028","PORTA_029","PORTA_030","PORTA_031",
        "WARN_001","WARN_002","WARN_003","WARN_004",
        // ORDRSP rules
        "ORDRSP_SYN_006",
        // DESADV rules
        "DESADV_SYN_006",
        "DESADV_001","DESADV_002","DESADV_003","DESADV_004","DESADV_005",
        "DESADV_006","DESADV_007","DESADV_008","DESADV_009",
        "DESADV_010","DESADV_011","DESADV_011b","DESADV_012","DESADV_013",
        "DESADV_014","DESADV_016",
        "DESADV_WARN_001","DESADV_WARN_002","DESADV_WARN_003",
        "ORDRSP_001","ORDRSP_002","ORDRSP_003","ORDRSP_004","ORDRSP_005",
        "ORDRSP_006","ORDRSP_007","ORDRSP_008","ORDRSP_009","ORDRSP_010",
        "ORDRSP_011","ORDRSP_012","ORDRSP_013","ORDRSP_014","ORDRSP_014b",
        "ORDRSP_015","ORDRSP_016","ORDRSP_017","ORDRSP_018",
        "ORDRSP_WARN_001"
    };
}
