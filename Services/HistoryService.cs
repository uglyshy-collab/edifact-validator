using System.Text.Json;
using Microsoft.JSInterop;
using EdifactValidator.Models;

namespace EdifactValidator.Services;

public class HistoryService
{
    private readonly IJSRuntime _js;

    private static readonly JsonSerializerOptions _camel  = new() { PropertyNamingPolicy  = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions _nocase = new() { PropertyNameCaseInsensitive = true };

    public HistoryService(IJSRuntime js) => _js = js;

    public async Task AddAsync(ValidationRecord record)
    {
        var json = JsonSerializer.Serialize(record, _camel);
        var el   = JsonSerializer.Deserialize<JsonElement>(json);
        await _js.InvokeVoidAsync("idb.add", el);
    }

    public async Task<List<ValidationRecord>> GetRecentAsync(int limit = 500)
    {
        var el   = await _js.InvokeAsync<JsonElement>("idb.getRecent", limit);
        var list = new List<ValidationRecord>();
        foreach (var item in el.EnumerateArray())
        {
            var rec = JsonSerializer.Deserialize<ValidationRecord>(item.GetRawText(), _nocase);
            if (rec is not null) list.Add(rec);
        }
        return list;
    }

    public async Task<int> GetCountAsync() =>
        await _js.InvokeAsync<int>("idb.count");

    public async Task ClearAsync() =>
        await _js.InvokeVoidAsync("idb.clear");

    public async Task DownloadCsvAsync(IJSRuntime js)
    {
        var csv  = await _js.InvokeAsync<string>("idb.exportCsv");
        var name = $"validierung_verlauf_{DateTime.Now:yyyyMMdd}.csv";
        await js.InvokeVoidAsync("downloadFile", name, csv, "text/csv;charset=utf-8");
    }
}
