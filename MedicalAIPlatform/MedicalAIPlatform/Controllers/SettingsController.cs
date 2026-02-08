using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers;

[AllowAnonymous]
public class SettingsController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<SettingsController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = new SettingsViewModel
        {
            IsDarkMode = true,
            Language = "en"
        };

        if (User?.Identity?.IsAuthenticated ?? false)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                model.IsDarkMode = user.IsDarkMode;
                model.Language = user.Language;
                model.FullName = user.FullName ?? user.UserName;
                model.Email = user.Email;
                model.Specialization = user.Specialization ?? "Medical Specialist";
                model.UserId = user.Id;
            }
        }

        return View(model);
    }

    // Called via JavaScript when the user toggles dark mode or changes language.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateSettings([FromBody] SettingsViewModel model)
    {
        // Normalize language
        if (model.Language != "en" && model.Language != "ar")
        {
            model.Language = "en";
        }

        if (User?.Identity?.IsAuthenticated ?? false)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                user.IsDarkMode = model.IsDarkMode;
                user.Language = model.Language;

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    _logger.LogWarning("Failed to update settings for user {UserId}: {Errors}",
                        user.Id,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
                else
                {
                    await _signInManager.RefreshSignInAsync(user);
                }
            }
        }

        // Set Cookies for Global Access (Persistent even if session lost or for redundancy)
        var cookieOptions = new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            SameSite = SameSiteMode.Lax,
            HttpOnly = false, // Allow JavaScript access for client-side reading
            IsEssential = true
        };
        
        // Set Secure flag based on request scheme
        cookieOptions.Secure = Request.IsHttps;
        
        Response.Cookies.Append("Settings_DarkMode", model.IsDarkMode.ToString(), cookieOptions);
        Response.Cookies.Append("Settings_Language", model.Language, cookieOptions);

        // For anonymous users we only update client-side (cookies/local storage via JS).
        return Json(new { success = true });
    }
}


