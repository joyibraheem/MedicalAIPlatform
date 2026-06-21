using CheXNet.BlazorApp.Components;
using CheXNet.BlazorApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Base URL for CheXNet + LungAI (hosted service may switch to 8002 if 8000 doesn't have /predict/ct)
var chexnetApiBaseUrl = builder.Configuration["CheXNetApi:BaseUrl"] ?? "http://localhost:8000/";
if (!chexnetApiBaseUrl.EndsWith("/", StringComparison.Ordinal))
    chexnetApiBaseUrl += "/";
var lungAiOverride = builder.Configuration["LungAI:BaseUrl"]?.Trim();
var initialBaseUrl = string.IsNullOrWhiteSpace(lungAiOverride) ? chexnetApiBaseUrl : (lungAiOverride.EndsWith("/", StringComparison.Ordinal) ? lungAiOverride : lungAiOverride + "/");

builder.Services.AddSingleton(sp =>
{
    var opts = new CheXNetApiEndpointOptions { BaseUrl = initialBaseUrl };
    return opts;
});

builder.Services.AddHttpClient<CheXNetApiClient>(client =>
{
    client.BaseAddress = new Uri(initialBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddHttpClient<LungAIApiClient>(client =>
{
    client.BaseAddress = new Uri(initialBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(120);
});

// Chat service (Ollama by default)
var chatBaseUrl = builder.Configuration["Chat:BaseUrl"] ?? "http://localhost:11434";
builder.Services.AddHttpClient<ChatService>(client =>
{
    client.BaseAddress = new Uri(chatBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(120);
    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});

// Auto-start the local Python CheXNet API when the Blazor app starts (so VS can run only this project).
builder.Services.AddHostedService<CheXNetApiHostedService>();

// BioBERT Service
builder.Services.AddHttpClient<BioBertApiClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:8001/");
    client.Timeout = TimeSpan.FromSeconds(120);
});
builder.Services.AddHostedService<BioBertApiHostedService>();

// State service for sharing CheXNet results between pages
builder.Services.AddSingleton<CheXNetStateService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
