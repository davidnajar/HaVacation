using HaVacation.Models;
using HaVacation.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HomeAssistantConfig>(
    builder.Configuration.GetSection("HomeAssistant"));

builder.Services.Configure<VacationConfig>(
    builder.Configuration.GetSection("Vacation"));

// HomeAssistantClient uses an HttpClient; the factory manages connection pooling.
builder.Services.AddHttpClient<HomeAssistantClient>();

builder.Services.AddHostedService<VacationWorker>();

var host = builder.Build();
host.Run();
