using MedicalAIPlatform.Controllers;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Options;
using MedicalAIPlatform.Services;
using MedicalAIPlatform.Services.Dicom;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.NativeCodec;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

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

// Configure external authentication (Google only)
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    // Configure cookie options for SameSite and Secure
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    
    // Set Secure policy based on environment
    if (builder.Environment.IsDevelopment())
    {
        // In development, allow both HTTP and HTTPS
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }
    else
    {
        // In production, always use Secure cookies (HTTPS only)
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    }
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
    options.SignInScheme = IdentityConstants.ExternalScheme;
});

// Register email sender
builder.Services.AddSingleton<IEmailSender, EmailSender>();

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

// Analytics State Service
builder.Services.AddSingleton<AnalyticsStateService>();

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

builder.Services.AddControllersWithViews();

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

// Seed roles (Admin, Doctor) and initial users
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        string[] roleNames = { "Admin", "Doctor" };
        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        // Create initial admin user
        const string adminEmail = "admin@medicalai.com";
        const string adminPassword = "Admin@12345";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);

        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FullName = "System Administrator",
                Specialization = null,
                CreatedAt = DateTime.UtcNow
            };

            var createAdminResult = await userManager.CreateAsync(adminUser, adminPassword);
            if (!createAdminResult.Succeeded)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogError("Failed to create initial admin user {Email}. Errors: {Errors}",
                    adminEmail, string.Join(", ", createAdminResult.Errors.Select(e => e.Description)));
            }
        }

        if (adminUser != null && !await userManager.IsInRoleAsync(adminUser, "Admin"))
        {
            var addToRoleResult = await userManager.AddToRoleAsync(adminUser, "Admin");
            if (!addToRoleResult.Succeeded)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogError("Failed to assign Admin role to user {Email}. Errors: {Errors}",
                    adminEmail, string.Join(", ", addToRoleResult.Errors.Select(e => e.Description)));
            }
        }

        // Create initial doctor user
        const string doctorEmail = "doctor@medicalai.com";
        const string doctorPassword = "Doctor@12345";
        var doctorUser = await userManager.FindByEmailAsync(doctorEmail);

        if (doctorUser == null)
        {
            doctorUser = new ApplicationUser
            {
                UserName = doctorEmail,
                Email = doctorEmail,
                EmailConfirmed = true,
                FullName = "Dr. Smith",
                Specialization = "Cardiologist",
                CreatedAt = DateTime.UtcNow
            };

            var createDoctorResult = await userManager.CreateAsync(doctorUser, doctorPassword);
            if (!createDoctorResult.Succeeded)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogError("Failed to create initial doctor user {Email}. Errors: {Errors}",
                    doctorEmail, string.Join(", ", createDoctorResult.Errors.Select(e => e.Description)));
            }
        }

        if (doctorUser != null && !await userManager.IsInRoleAsync(doctorUser, "Doctor"))
        {
            await userManager.AddToRoleAsync(doctorUser, "Doctor");
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

app.MapControllers();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();