using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using edifact_validator;
using EdifactValidator.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<L10n>();
builder.Services.AddSingleton<GlnStore>();
builder.Services.AddSingleton<HistoryService>();
builder.Services.AddTransient<PortaValidator>();
builder.Services.AddTransient<OrdrspValidator>();
builder.Services.AddTransient<DesadvValidator>();
builder.Services.AddTransient<InvrptValidator>();

await builder.Build().RunAsync();
