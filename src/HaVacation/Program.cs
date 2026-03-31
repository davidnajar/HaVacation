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

// Required so App.razor can read HttpContext.Request.PathBase for the <base href>.
builder.Services.AddHttpContextAccessor();

builder.Services.AddHostedService<VacationWorker>();

builder.Services.AddSingleton<ConfigurationService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// When running as a Home Assistant add-on, the Supervisor ingress proxy strips
// the path prefix (e.g. /api/hassio_ingress/<token>) before forwarding to the
// container and sends the original prefix in the X-Ingress-Path header.
// Setting PathBase from that header lets ASP.NET Core generate correct URLs for
// redirects and lets the dynamic <base href> in App.razor resolve Blazor assets
// and the SignalR endpoint correctly through the proxy.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("X-Ingress-Path", out var ingressPath))
    {
        var path = ingressPath.FirstOrDefault();
        if (!string.IsNullOrEmpty(path))
            context.Request.PathBase = new PathString(path.TrimEnd('/'));
    }
    await next();
});

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
