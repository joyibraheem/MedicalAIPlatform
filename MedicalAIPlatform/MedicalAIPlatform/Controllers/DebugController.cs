using MedicalAIPlatform.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers;

/// <summary>
/// Local development utilities only. Removed from MVC in non-Development environments;
/// requests never reach this type in Production.
/// </summary>
[DevelopmentOnly]
[Authorize(Roles = "Admin")]
[Route("debug")]
public sealed class DebugController : Controller
{
    [HttpGet("")]
    [HttpGet("index")]
    public IActionResult Index()
    {
        // Intentionally minimal — no claims, users, roles, or configuration details.
        return View();
    }
}
