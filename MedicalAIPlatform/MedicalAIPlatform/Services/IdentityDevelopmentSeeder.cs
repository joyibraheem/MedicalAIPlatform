using MedicalAIPlatform.Models;
using MedicalAIPlatform.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MedicalAIPlatform.Services;

/// <summary>Creates default admin/doctor accounts in Development only, using secrets from configuration or environment.</summary>
public sealed class IdentityDevelopmentSeeder
{
    public const string AdminPasswordEnvVar = "MEDICALAI_DEV_ADMIN_PASSWORD";
    public const string DoctorPasswordEnvVar = "MEDICALAI_DEV_DOCTOR_PASSWORD";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DevelopmentSeedOptions _opt;
    private readonly ILogger<IdentityDevelopmentSeeder> _logger;

    public IdentityDevelopmentSeeder(
        UserManager<ApplicationUser> userManager,
        IOptions<DevelopmentSeedOptions> options,
        ILogger<IdentityDevelopmentSeeder> logger)
    {
        _userManager = userManager;
        _opt = options.Value;
        _logger = logger;
    }

    public static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var roleName in new[] { "Admin", "Doctor" })
        {
            if (!await roleManager.RoleExistsAsync(roleName).ConfigureAwait(false))
                await roleManager.CreateAsync(new IdentityRole(roleName)).ConfigureAwait(false);
        }
    }

    public async Task SeedDevelopmentUsersAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!_opt.Enabled)
        {
            _logger.LogInformation("Development user seeding disabled (DevelopmentSeed:Enabled = false).");
            return;
        }

        var adminPassword = ResolvePassword(AdminPasswordEnvVar, configuration["DevelopmentSeed:AdminPassword"] ?? _opt.AdminPassword);
        var doctorPassword = ResolvePassword(DoctorPasswordEnvVar, configuration["DevelopmentSeed:DoctorPassword"] ?? _opt.DoctorPassword);

        if (string.IsNullOrWhiteSpace(adminPassword) || string.IsNullOrWhiteSpace(doctorPassword))
        {
            _logger.LogWarning(
                "Development user seed skipped: set {AdminEnv} and {DoctorEnv} environment variables, " +
                "or DevelopmentSeed:AdminPassword / DevelopmentSeed:DoctorPassword via user secrets.",
                AdminPasswordEnvVar, DoctorPasswordEnvVar);
            return;
        }

        await EnsureAdminAsync(_opt.AdminEmail.Trim(), adminPassword).ConfigureAwait(false);
        await EnsureDoctorAsync(_opt.DoctorEmail.Trim(), doctorPassword).ConfigureAwait(false);
    }

    private static string? ResolvePassword(string envVarName, string? configPassword)
    {
        var fromEnv = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();
        return string.IsNullOrWhiteSpace(configPassword) ? null : configPassword.Trim();
    }

    private async Task EnsureAdminAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = "System Administrator",
                DoctorStatus = "None",
                CreatedAt = DateTime.UtcNow,
                MustChangePasswordOnLogin = true
            };

            var create = await _userManager.CreateAsync(user, password).ConfigureAwait(false);
            if (!create.Succeeded)
            {
                _logger.LogError("Failed to create dev admin {Email}: {Errors}",
                    email, string.Join(", ", create.Errors.Select(e => e.Description)));
                return;
            }

            _logger.LogInformation("Created development admin account {Email} (must change password on first login).", email);
        }

        if (!await _userManager.IsInRoleAsync(user, "Admin").ConfigureAwait(false))
            await _userManager.AddToRoleAsync(user, "Admin").ConfigureAwait(false);
    }

    private async Task EnsureDoctorAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = "Dr. Smith",
                Specialization = "Cardiologist",
                DoctorStatus = DoctorRegistrationStatuses.Verified,
                CreatedAt = DateTime.UtcNow,
                MustChangePasswordOnLogin = true
            };

            var create = await _userManager.CreateAsync(user, password).ConfigureAwait(false);
            if (!create.Succeeded)
            {
                _logger.LogError("Failed to create dev doctor {Email}: {Errors}",
                    email, string.Join(", ", create.Errors.Select(e => e.Description)));
                return;
            }

            _logger.LogInformation("Created development doctor account {Email} (must change password on first login).", email);
        }
        else if (!string.Equals(user.DoctorStatus, DoctorRegistrationStatuses.Verified, StringComparison.OrdinalIgnoreCase))
        {
            user.DoctorStatus = DoctorRegistrationStatuses.Verified;
            await _userManager.UpdateAsync(user).ConfigureAwait(false);
        }

        if (!await _userManager.IsInRoleAsync(user, "Doctor").ConfigureAwait(false))
            await _userManager.AddToRoleAsync(user, "Doctor").ConfigureAwait(false);
    }
}
