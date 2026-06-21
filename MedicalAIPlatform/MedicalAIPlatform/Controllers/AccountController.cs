using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Text.Encodings.Web;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Controllers;

[Authorize]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AccountController> _logger;
    private readonly IEmailSender _emailSender;
    private readonly Data.ApplicationDbContext _context;
    private readonly DoctorRegistrationService _doctorRegistration;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<AccountController> logger,
        IEmailSender emailSender,
        Data.ApplicationDbContext context,
        DoctorRegistrationService doctorRegistration)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
        _emailSender = emailSender;
        _context = context;
        _doctorRegistration = doctorRegistration;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // 1. Mandatory Doctor Verification
        var verificationCode = await _context.DoctorVerificationCodes
            .FirstOrDefaultAsync(c => c.Code == model.DoctorVerificationCode && !c.IsUsed);

        if (verificationCode == null)
        {
            ModelState.AddModelError("DoctorVerificationCode", "Invalid or already used verification code.");
            return View(model);
        }

        // 2. Create User (Always DoctorStatus = Verified because we have a valid code)
        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            Specialization = model.Specialization,
            CreatedAt = DateTime.UtcNow,
            DoctorStatus = DoctorRegistrationStatuses.Verified
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            // 3. Mark code as used
            verificationCode.IsUsed = true;
            verificationCode.UsedByUserId = user.Id;
            verificationCode.UsedAt = DateTime.UtcNow;
            _context.Update(verificationCode);
            await _context.SaveChangesAsync();

            // 4. Automatically Assign Doctor Role
            await _userManager.AddToRoleAsync(user, "Doctor");

            // 5. Sign In
            await _signInManager.SignInAsync(user, isPersistent: false);
            _logger.LogInformation("Doctor registered and signed in.");
            return RedirectToAction("Index", "Dashboard");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        var model = new LoginViewModel { ReturnUrl = returnUrl };
        return View(model);
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            model.Email,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("User signed in.");
            var user = await _userManager.FindByEmailAsync(model.Email).ConfigureAwait(false);
            if (user != null)
            {
                var redirect = await RedirectAfterSignInAsync(user, model.ReturnUrl).ConfigureAwait(false);
                if (redirect != null)
                    return redirect;
            }

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);

            return RedirectToAction("Index", "Dashboard");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account has been locked out due to multiple failed sign-in attempts.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid sign-in attempt.");
        }

        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Logout()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return RedirectToAction("Login");
        }
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName("Logout")]
    public async Task<IActionResult> LogoutConfirmed()
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User signed out.");
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
        {
            // Don't reveal that the user does not exist or is not confirmed
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        var code = await _userManager.GeneratePasswordResetTokenAsync(user);
        var callbackUrl = Url.Action(
            action: nameof(ResetPassword),
            controller: "Account",
            values: new { code, email = user.Email },
            protocol: Request.Scheme);

        await _emailSender.SendEmailAsync(
            model.Email,
            "Reset Password",
            $"Please reset your password by clicking here: <a href='{HtmlEncoder.Default.Encode(callbackUrl ?? "")}'>link</a>");

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPassword(string? code = null, string? email = null)
    {
        if (code == null || email == null)
        {
            return BadRequest("A code and email must be supplied for password reset.");
        }

        var model = new ResetPasswordViewModel
        {
            Code = code,
            Email = email
        };

        return View(model);
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            // Don't reveal that the user does not exist
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        var result = await _userManager.ResetPasswordAsync(user, model.Code, model.Password);
        if (result.Succeeded)
        {
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError != null)
        {
            ModelState.AddModelError(string.Empty, $"Error from external provider: {remoteError}");
            return View(nameof(Login));
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return RedirectToAction(nameof(Login));
        }

        // Sign in the user with this external login provider if the user already has a login
        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("User signed in with {Provider} provider.", info.LoginProvider);
            var existingUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).ConfigureAwait(false)
                         ?? await _userManager.FindByEmailAsync(
                             info.Principal.FindFirstValue(ClaimTypes.Email) ?? "").ConfigureAwait(false);
            if (existingUser != null)
            {
                var redirect = await RedirectAfterSignInAsync(existingUser, returnUrl).ConfigureAwait(false);
                if (redirect != null)
                    return redirect;
            }

            return RedirectToLocal(returnUrl);
        }

        if (result.IsLockedOut)
        {
            return RedirectToAction(nameof(Login));
        }

        // If the user does not have an account, create one
        var email = info.Principal.FindFirstValue(System.Security.Claims.ClaimTypes.Email);
        var name = info.Principal.FindFirstValue(System.Security.Claims.ClaimTypes.Name) 
                   ?? info.Principal.FindFirstValue(System.Security.Claims.ClaimTypes.GivenName)
                   ?? email?.Split('@')[0] 
                   ?? "User";

        if (string.IsNullOrEmpty(email))
        {
            ModelState.AddModelError(string.Empty, "Email claim not received from external provider.");
            return View(nameof(Login));
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = name,
                CreatedAt = DateTime.UtcNow,
                DoctorStatus = DoctorRegistrationStatuses.Pending
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return View(nameof(Login));
            }

            // Google sign-up: pending admin approval — no Doctor role until approved.
        }

        // Add external login to user
        var addLoginResult = await _userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            foreach (var error in addLoginResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(nameof(Login));
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        _logger.LogInformation("User created an account using {Provider} provider.", info.LoginProvider);

        var postSignIn = await RedirectAfterSignInAsync(user, returnUrl).ConfigureAwait(false);
        return postSignIn ?? RedirectToAction(nameof(CompleteProfile));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> CompleteProfile()
    {
        var user = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
            return RedirectToAction(nameof(Login));

        if (!string.Equals(user.DoctorStatus, DoctorRegistrationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "Dashboard");

        if (user.ProfileSubmittedAt is not null)
            return RedirectToAction(nameof(ApprovalPending));

        return View(new CompleteDoctorProfileViewModel
        {
            FullName = user.FullName ?? "",
            Specialization = user.Specialization ?? "",
            HospitalOrganization = user.HospitalOrganization ?? "",
            MedicalLicenseNumber = user.MedicalLicenseNumber ?? "",
            PhoneNumber = user.PhoneNumber ?? "",
            RegistrationNotes = user.RegistrationNotes
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteProfile(CompleteDoctorProfileViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
            return RedirectToAction(nameof(Login));

        var (ok, error) = await _doctorRegistration.SubmitProfileAsync(user, model, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!ok)
        {
            ModelState.AddModelError(string.Empty, error ?? "Could not submit profile.");
            return View(model);
        }

        return RedirectToAction(nameof(ApprovalPending));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> ChangePasswordRequired()
    {
        var user = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
            return RedirectToAction(nameof(Login));

        if (!user.MustChangePasswordOnLogin)
            return RedirectToAction("Index", "Dashboard");

        return View(new ChangePasswordRequiredViewModel());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePasswordRequired(ChangePasswordRequiredViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
            return RedirectToAction(nameof(Login));

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        user.MustChangePasswordOnLogin = false;
        await _userManager.UpdateAsync(user).ConfigureAwait(false);
        await _signInManager.RefreshSignInAsync(user).ConfigureAwait(false);
        _logger.LogInformation("User {UserId} completed required password change.", user.Id);

        return await RedirectAfterSignInAsync(user, null).ConfigureAwait(false)
               ?? RedirectToAction("Index", "Dashboard");
    }

    private async Task<IActionResult?> RedirectAfterSignInAsync(ApplicationUser user, string? returnUrl)
    {
        if (user.MustChangePasswordOnLogin)
            return RedirectToAction(nameof(ChangePasswordRequired));

        if (await _userManager.IsInRoleAsync(user, "Admin").ConfigureAwait(false))
            return RedirectToLocal(returnUrl);

        if (string.Equals(user.DoctorStatus, DoctorRegistrationStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            return RedirectToAction(nameof(ApprovalPending));

        if (string.Equals(user.DoctorStatus, DoctorRegistrationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
        {
            if (user.ProfileSubmittedAt is null)
                return RedirectToAction(nameof(CompleteProfile));
            return RedirectToAction(nameof(ApprovalPending));
        }

        var isVerifiedDoctor = await _userManager.IsInRoleAsync(user, "Doctor").ConfigureAwait(false)
            && string.Equals(user.DoctorStatus, DoctorRegistrationStatuses.Verified, StringComparison.OrdinalIgnoreCase);
        if (!isVerifiedDoctor)
            return RedirectToAction(nameof(ApprovalPending));

        return RedirectToLocal(returnUrl);
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Dashboard");
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> ApprovalPending()
    {
        var user = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
            return RedirectToAction(nameof(Login));

        if (string.Equals(user.DoctorStatus, DoctorRegistrationStatuses.Pending, StringComparison.OrdinalIgnoreCase)
            && user.ProfileSubmittedAt is null)
        {
            return RedirectToAction(nameof(CompleteProfile));
        }

        var model = new ApprovalPendingViewModel
        {
            Status = user.DoctorStatus,
            RejectionReason = user.RejectionReason,
            ProfileSubmitted = user.ProfileSubmittedAt is not null
        };
        return View(model);
    }
}

