using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace MedicalAIPlatform.Authorization;

/// <summary>Admin, or Doctor role with <see cref="ApplicationUser.DoctorStatus"/> = Verified.</summary>
public sealed class VerifiedMedicalUserRequirement : IAuthorizationRequirement;

public sealed class VerifiedMedicalUserHandler : AuthorizationHandler<VerifiedMedicalUserRequirement>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public VerifiedMedicalUserHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        VerifiedMedicalUserRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
            return;

        if (context.User.IsInRole("Admin"))
        {
            context.Succeed(requirement);
            return;
        }

        var user = await _userManager.GetUserAsync(context.User).ConfigureAwait(false);
        if (user is null)
            return;

        if (await _userManager.IsInRoleAsync(user, "Doctor").ConfigureAwait(false)
            && string.Equals(user.DoctorStatus, DoctorRegistrationStatuses.Verified, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }
    }
}
