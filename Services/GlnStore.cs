using System.Text.Json;
using Microsoft.JSInterop;
using EdifactValidator.Models;

namespace EdifactValidator.Services;

public class GlnStore
{
    private readonly IJSRuntime _js;
    private const string EntriesKey = "gln_entries";
    private const string CredsKey   = "gln_creds";

    public List<GlnEntry> Entries { get; private set; } = new();
    public bool IsAuthenticated { get; private set; }
    public bool Initialized { get; private set; }
    public event Action? Changed;

    public GlnStore(IJSRuntime js) => _js = js;

    public async Task InitAsync()
    {
        if (Initialized) return;
        var json = await _js.InvokeAsync<string?>("ls.get", EntriesKey);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<List<GlnEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (saved is { Count: > 0 }) { Entries = saved; Initialized = true; return; }
            }
            catch { }
        }
        Entries = GlnDatabase.All.ToList();
        Initialized = true;
    }

    public async Task<bool> LoginAsync(string user, string password)
    {
        var (u, p) = await GetCredsAsync();
        IsAuthenticated = user.Trim() == u && password == p;
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
        var json = JsonSerializer.Serialize(new { user = user.Trim(), password });
        await _js.InvokeVoidAsync("ls.set", CredsKey, json);
    }

    private async Task<(string User, string Password)> GetCredsAsync()
    {
        var json = await _js.InvokeAsync<string?>("ls.get", CredsKey);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var d = JsonSerializer.Deserialize<JsonElement>(json);
                return (d.GetProperty("user").GetString() ?? "admin",
                        d.GetProperty("password").GetString() ?? "porta2024");
            }
            catch { }
        }
        return ("admin", "porta2024");
    }

    private async Task PersistAsync()
    {
        var json = JsonSerializer.Serialize(Entries);
        await _js.InvokeVoidAsync("ls.set", EntriesKey, json);
        Changed?.Invoke();
    }
}
