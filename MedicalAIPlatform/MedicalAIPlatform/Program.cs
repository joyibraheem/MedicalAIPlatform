using MedicalAIPlatform.Controllers;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Hubs;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Options;
using MedicalAIPlatform.Services;
using MedicalAIPlatform.Services.Dicom;
using MedicalAIPlatform.Authorization;
using MedicalAIPlatform.Infrastructure;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.NativeCodec;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

const long maxUploadBytes = 512L * 1024 * 1024;
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = maxUploadBytes;
});
builder.Services.Configure<KestrelServerOptions>(o =>
{
    o.Limits.MaxRequestBodySize = maxUploadBytes;
});
builder.Services.Configure<IISServerOptions>(o =>
{
    o.MaxRequestBodySize = maxUploadBytes;
});

new DicomSetupBuilder()
    .RegisterServices(s => s.AddFellowOakDicom()
        .AddImageManager<ImageSharpImageManager>()
        .AddTranscoderManager<NativeTranscoderManager>())
    .SkipValidation()
    .Build();

builder.Services.AddFellowOakDicom();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddSignalR();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Password settings
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
        options.Password.RequiredUniqueChars = 1;

        // User settings
        options.User.RequireUniqueEmail = true;

        // Lockout settings
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;

        // Sign-in settings
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedPhoneNumber = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("VerifiedMedicalUser", policy =>
        policy.Requirements.Add(new VerifiedMedicalUserRequirement()));
});
builder.Services.AddScoped<IAuthorizationHandler, VerifiedMedicalUserHandler>();

// Google external login only
// AddIdentity already registers Identity.Application as the default scheme. A second cookie scheme
// breaks POST JSON endpoints (fetch "Failed to fetch" / flaky auth after navigation).
builder.Services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? string.Empty;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? string.Empty;
        options.SignInScheme = IdentityConstants.ExternalScheme;
    });

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.AccessDeniedPath = "/Account/ApprovalPending";

    var runningInContainer = string.Equals(
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    // Docker runs HTTP only; Secure cookies would never be sent to the browser.
    if (builder.Environment.IsDevelopment() || runningInContainer)
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    else
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

    options.Events.OnRedirectToLogin = context =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Equals("/AIAssistant/SendMessage", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            return context.Response.WriteAsync(
                "{\"error\":\"Session expired. Please refresh the page and sign in again.\",\"loginRequired\":true}");
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

// Email (logs in dev; swap for SMTP provider in production)
builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
builder.Services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();
builder.Services.Configure<DoctorRegistrationOptions>(
    builder.Configuration.GetSection(DoctorRegistrationOptions.SectionName));
builder.Services.AddScoped<DoctorRegistrationService>();

// Analytics API Services
var chexnetApiBaseUrl = builder.Configuration["CheXNetApi:BaseUrl"] ?? "http://localhost:8000/";
if (!chexnetApiBaseUrl.EndsWith("/", StringComparison.Ordinal))
    chexnetApiBaseUrl += "/";

builder.Services.AddSingleton(sp => new CheXNetApiEndpointOptions { BaseUrl = chexnetApiBaseUrl });

builder.Services.AddHttpClient<CheXNetApiClient>(client =>
{
    client.BaseAddress = new Uri(chexnetApiBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromMinutes(10);
});

builder.Services.AddHttpClient<BioBertApiClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:8001/");
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddHttpClient<LungAIApiClient>(client =>
{
    client.BaseAddress = new Uri(chexnetApiBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromMinutes(10);
});

// DICOM slice pipeline (fo-dicom + ImageSharp + ONNX / HTTP inference)
builder.Services.Configure<DicomPipelineOptions>(builder.Configuration.GetSection(DicomPipelineOptions.SectionName));
builder.Services.AddSingleton<IDicomDatasetLoaderService, DicomDatasetLoaderService>();
builder.Services.AddScoped<DicomSliceExtractionService>();
builder.Services.AddScoped<DicomMetadataParser>();
builder.Services.AddScoped<DicomSlicePreprocessor>();
builder.Services.AddScoped<PredictionAggregationEngine>();
builder.Services.AddSingleton<OnnxSliceInferenceRunner>();
builder.Services.AddScoped<DicomInferencePipelineOrchestrator>();
builder.Services.AddScoped<AnalyticsCtInferenceExecutor>();
builder.Services.AddScoped<AnalyticsXRayInferenceExecutor>();
builder.Services.AddSingleton<AnalyticsJobQueueService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IUserAnalyticsSessionStore, UserAnalyticsSessionStore>();
builder.Services.AddScoped<AnalyticsStateService>();
builder.Services.AddScoped<AnalyticsSessionLoader>();
builder.Services.AddSingleton<ICtScanViewerSessionStore, CtScanViewerSessionStore>();
builder.Services.AddScoped<CtScanViewerService>();

builder.Services.AddScoped<PredictionFeedbackService>();
builder.Services.AddScoped<AdminDashboardService>();

builder.Services.AddScoped<MedicalReportService>();
builder.Services.AddScoped<ScanAiAnalysisPipelineService>();
builder.Services.AddScoped<MedicalReportPdfService>();

builder.Services.Configure<DevelopmentSeedOptions>(
    builder.Configuration.GetSection(DevelopmentSeedOptions.SectionName));
builder.Services.AddScoped<IdentityDevelopmentSeeder>();

builder.Services.Configure<DemoDataSeederOptions>(
    builder.Configuration.GetSection(DemoDataSeederOptions.SectionName));
builder.Services.AddScoped<MedicalPlatformDemoDataSeeder>();

// Auto-start the local Python CheXNet API when the app starts
builder.Services.AddHostedService<CheXNetApiHostedService>();

// Auto-start the local BioBERT API when the app starts
builder.Services.AddHostedService<BioBertApiHostedService>();

// Chat Service (Ollama)
var chatBaseUrl = builder.Configuration["Chat:BaseUrl"] ?? "http://localhost:11434";
builder.Services.AddHttpClient<ChatService>(client =>
{
    client.BaseAddress = new Uri(chatBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(120);
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddControllersWithViews(options =>
{
    options.Conventions.Add(new RemoveDevelopmentOnlyControllersConvention(builder.Environment));
    options.Filters.Add<DevelopmentOnlyActionFilter>();
});
builder.Services.Configure<AntiforgeryOptions>(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// Configure global cookie policy
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => false; // Not using consent, cookies are essential
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = CookieSecurePolicy.SameAsRequest; // Secure in HTTPS, not Secure in HTTP
});

var app = builder.Build();

// fo-dicom resolves render/graph services from the host service provider
DicomSetupBuilder.UseServiceProvider(app.Services);

// Migrate database; seed roles always; dev accounts only in Development with env-based passwords
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        DatabaseMigrationBootstrap.PrepareLegacyDatabase(context);
        context.Database.Migrate();

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        await IdentityDevelopmentSeeder.SeedRolesAsync(roleManager).ConfigureAwait(false);

        var dockerDemoSeed = string.Equals(
            Environment.GetEnvironmentVariable("DOCKER_DEMO_SEED"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (app.Environment.IsDevelopment() || dockerDemoSeed)
        {
            var devSeeder = services.GetRequiredService<IdentityDevelopmentSeeder>();
            var configuration = services.GetRequiredService<IConfiguration>();
            await devSeeder.SeedDevelopmentUsersAsync(configuration).ConfigureAwait(false);

            var demoOpts = services.GetRequiredService<IOptions<DemoDataSeederOptions>>().Value;
            if (demoOpts.RunOnStartup)
            {
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
                var demoSeeder = services.GetRequiredService<MedicalPlatformDemoDataSeeder>();
                var seedOpts = services.GetRequiredService<IOptions<DevelopmentSeedOptions>>().Value;
                var seedActor = await userManager.FindByEmailAsync(seedOpts.DoctorEmail).ConfigureAwait(false)
                                ?? await userManager.FindByEmailAsync(seedOpts.AdminEmail).ConfigureAwait(false);
                if (seedActor != null)
                {
                    var demoResult = await demoSeeder.SeedAsync(false, seedActor.Id, CancellationToken.None)
                        .ConfigureAwait(false);
                    var demoLog = services.GetRequiredService<ILogger<MedicalPlatformDemoDataSeeder>>();
                    if (demoResult.Skipped)
                        demoLog.LogInformation("Demo data: {Message}", demoResult.Message);
                    else
                        demoLog.LogInformation(
                            "Demo data seeded: patients={P}, historyRows={H}, scans={S}, reports={R}",
                            demoResult.PatientsCreated, demoResult.HistoryEntriesCreated, demoResult.ScansCreated,
                            demoResult.ReportsCreated);
                }
            }
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding roles.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Only force HTTPS redirection if not running in Docker (where we use HTTP)
// Check if we're running in a container by checking for Docker environment variable
var isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
if (!isDocker && !app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Apply cookie policy globally (must be before UseAuthentication)
app.UseCookiePolicy();

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<AssistantHub>("/hubs/assistant");

app.MapControllers();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();