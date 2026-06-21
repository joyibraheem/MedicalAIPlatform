using System.Security.Claims;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Options;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MedicalAIPlatform.Controllers;

/// <summary>Admin-only synthetic demo dataset (<see cref="MedicalPlatformDemoDataSeeder.MrnPrefix"/>).</summary>
[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin/demo-data")]
public sealed class DemoDataAdminController : ControllerBase
{
    private readonly MedicalPlatformDemoDataSeeder _seeder;
    private readonly DemoDataSeederOptions _opt;

    public DemoDataAdminController(MedicalPlatformDemoDataSeeder seeder, IOptions<DemoDataSeederOptions> options)
    {
        _seeder = seeder;
        _opt = options.Value;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        var exists = await _seeder.DemoPatientsExistAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new { demoPatientsExist = exists, mrnPrefix = MedicalPlatformDemoDataSeeder.MrnPrefix });
    }

    /// <summary>Seeds ~14 demo patients. Use force=true to delete existing DEMO-* rows first.</summary>
    [HttpPost("seed")]
    public async Task<IActionResult> SeedAsync([FromQuery] bool force = false,
        [FromHeader(Name = "X-Demo-Seed-Key")] string? clientKey = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_opt.SecretKey) &&
            !string.Equals(clientKey, _opt.SecretKey, StringComparison.Ordinal))
        {
            return Unauthorized(new { error = "Invalid or missing X-Demo-Seed-Key header." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var result = await _seeder.SeedAsync(force, userId, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}
