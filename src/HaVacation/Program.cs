using HaVacation.Components;
using HaVacation.Models;
using HaVacation.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HomeAssistantConfig>(
    builder.Configuration.GetSection("HomeAssistant"));

builder.Services.Configure<VacationConfig>(
    builder.Configuration.GetSection("Vacation"));

// HomeAssistantClient uses an HttpClient; the factory manages connection pooling.
builder.Services.AddHttpClient<HomeAssistantClient>();

builder.Services.AddHostedService<VacationWorker>();

builder.Services.AddSingleton<ConfigurationService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
