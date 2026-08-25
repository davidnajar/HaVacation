using HaVacation.Components;
using HaVacation.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient<HomeAssistantClient>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddSingleton<VacationWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<VacationWorker>());
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");

app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("X-Ingress-Path", out var ingressPath))
    {
        var path = ingressPath.FirstOrDefault();
        if (!string.IsNullOrEmpty(path)) context.Request.PathBase = new PathString(path.TrimEnd('/'));
    }
    await next();
});

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
