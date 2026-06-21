using System.Net;
using System.Text;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedicalAIPlatform.Services;

public sealed class DoctorRegistrationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IEmailSender _emailSender;
    private readonly IUrlHelperFactory _urlHelperFactory;
    private readonly IActionContextAccessor _actionContextAccessor;
    private readonly DoctorRegistrationOptions _options;
    private readonly ILogger<DoctorRegistrationService> _logger;

    public DoctorRegistrationService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IEmailSender emailSender,
        IUrlHelperFactory urlHelperFactory,
        IActionContextAccessor actionContextAccessor,
        IOptions<DoctorRegistrationOptions> options,
        ILogger<DoctorRegistrationService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _emailSender = emailSender;
        _urlHelperFactory = urlHelperFactory;
        _actionContextAccessor = actionContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(bool Ok, string? Error)> SubmitProfileAsync(
        ApplicationUser user,
        CompleteDoctorProfileViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(user.DoctorStatus, DoctorRegistrationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            return (false, "Only pending registrations can submit a profile.");

        if (user.ProfileSubmittedAt is not null)
            return (false, "Your registration profile has already been submitted.");

        user.FullName = model.FullName.Trim();
        user.Specialization = model.Specialization.Trim();
        user.HospitalOrganization = model.HospitalOrganization.Trim();
        user.MedicalLicenseNumber = model.MedicalLicenseNumber.Trim();
        user.PhoneNumber = model.PhoneNumber.Trim();
        user.RegistrationNotes = string.IsNullOrWhiteSpace(model.RegistrationNotes)
            ? null
            : model.RegistrationNotes.Trim();
        user.ProfileSubmittedAt = DateTimeOffset.UtcNow;

        var update = await _userManager.UpdateAsync(user).ConfigureAwait(false);
        if (!update.Succeeded)
            return (false, string.Join(" ", update.Errors.Select(e => e.Description)));

        var reviewUrl = BuildAbsoluteUrl("DoctorDetails", "Admin", new { userId = user.Id });
        await NotifyAdminsOfNewRegistrationAsync(user, reviewUrl, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Doctor registration profile submitted by {UserId} ({Email})", user.Id, user.Email);
        return (true, null);
    }

    public async Task<IReadOnlyList<ApplicationUser>> GetPendingSubmittedRequestsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _userManager.Users.AsNoTracking()
            .Where(u =>
                u.DoctorStatus == DoctorRegistrationStatuses.Pending
                && u.ProfileSubmittedAt != null)
            .OrderByDescending(u => u.ProfileSubmittedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DoctorRegistrationDetailsViewModel?> GetDetailsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
            return null;

        string? approvedByName = null;
        string? rejectedByName = null;
        if (!string.IsNullOrEmpty(user.ApprovedByAdminId))
        {
            var admin = await _userManager.FindByIdAsync(user.ApprovedByAdminId).ConfigureAwait(false);
            approvedByName = admin?.FullName ?? admin?.Email;
        }

        if (!string.IsNullOrEmpty(user.RejectedByAdminId))
        {
            var admin = await _userManager.FindByIdAsync(user.RejectedByAdminId).ConfigureAwait(false);
            rejectedByName = admin?.FullName ?? admin?.Email;
        }

        return new DoctorRegistrationDetailsViewModel
        {
            UserId = user.Id,
            Email = user.Email ?? "",
            FullName = user.FullName,
            Specialization = user.Specialization,
            HospitalOrganization = user.HospitalOrganization,
            MedicalLicenseNumber = user.MedicalLicenseNumber,
            PhoneNumber = user.PhoneNumber,
            RegistrationNotes = user.RegistrationNotes,
            DoctorStatus = user.DoctorStatus,
            CreatedAt = user.CreatedAt,
            ProfileSubmittedAt = user.ProfileSubmittedAt,
            ApprovedAt = user.ApprovedAt,
            RejectedAt = user.RejectedAt,
            RejectionReason = user.RejectionReason,
            ApprovedByAdminName = approvedByName,
            RejectedByAdminName = rejectedByName
        };
    }

    public async Task<(bool Ok, string? Error)> ApproveAsync(
        string userId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId).ConfigureAwait(false);
        if (user is null)
            return (false, "User not found.");

        if (!string.Equals(user.DoctorStatus, DoctorRegistrationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            return (false, "Only pending registrations can be approved.");

        if (user.ProfileSubmittedAt is null)
            return (false, "The doctor has not submitted a registration profile yet.");

        user.DoctorStatus = DoctorRegistrationStatuses.Verified;
        user.ApprovedAt = DateTimeOffset.UtcNow;
        user.ApprovedByAdminId = adminUserId;
        user.RejectedAt = null;
        user.RejectedByAdminId = null;
        user.RejectionReason = null;

        var update = await _userManager.UpdateAsync(user).ConfigureAwait(false);
        if (!update.Succeeded)
            return (false, string.Join(" ", update.Errors.Select(e => e.Description)));

        if (!await _userManager.IsInRoleAsync(user, "Doctor").ConfigureAwait(false))
        {
            var roleResult = await _userManager.AddToRoleAsync(user, "Doctor").ConfigureAwait(false);
            if (!roleResult.Succeeded)
                return (false, string.Join(" ", roleResult.Errors.Select(e => e.Description)));
        }

        await SendApprovalEmailAsync(user, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Doctor {UserId} approved by admin {AdminId}", userId, adminUserId);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> RejectAsync(
        string userId,
        string adminUserId,
        string rejectionReason,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId).ConfigureAwait(false);
        if (user is null)
            return (false, "User not found.");

        if (!string.Equals(user.DoctorStatus, DoctorRegistrationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            return (false, "Only pending registrations can be rejected.");

        user.DoctorStatus = DoctorRegistrationStatuses.Rejected;
        user.RejectedAt = DateTimeOffset.UtcNow;
        user.RejectedByAdminId = adminUserId;
        user.RejectionReason = rejectionReason.Trim();
        user.ApprovedAt = null;
        user.ApprovedByAdminId = null;

        if (await _userManager.IsInRoleAsync(user, "Doctor").ConfigureAwait(false))
            await _userManager.RemoveFromRoleAsync(user, "Doctor").ConfigureAwait(false);

        var update = await _userManager.UpdateAsync(user).ConfigureAwait(false);
        if (!update.Succeeded)
            return (false, string.Join(" ", update.Errors.Select(e => e.Description)));

        await SendRejectionEmailAsync(user, user.RejectionReason, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Doctor {UserId} rejected by admin {AdminId}", userId, adminUserId);
        return (true, null);
    }

    private async Task NotifyAdminsOfNewRegistrationAsync(
        ApplicationUser doctor,
        string reviewUrl,
        CancellationToken cancellationToken)
    {
        var recipients = await ResolveAdminEmailsAsync(cancellationToken).ConfigureAwait(false);
        if (recipients.Count == 0)
        {
            _logger.LogWarning("No admin emails configured — skipping registration notification for {Email}", doctor.Email);
            return;
        }

        var submitted = doctor.ProfileSubmittedAt?.ToString("yyyy-MM-dd HH:mm UTC") ?? "—";
        var subject = "New Doctor Registration Request";
        var body = new StringBuilder()
            .AppendLine("<h2>New doctor registration request</h2>")
            .AppendLine("<table style=\"border-collapse:collapse\">")
            .AppendRow("Name", WebUtility.HtmlEncode(doctor.FullName ?? "—"))
            .AppendRow("Email", WebUtility.HtmlEncode(doctor.Email ?? "—"))
            .AppendRow("Specialty", WebUtility.HtmlEncode(doctor.Specialization ?? "—"))
            .AppendRow("Hospital", WebUtility.HtmlEncode(doctor.HospitalOrganization ?? "—"))
            .AppendRow("License", WebUtility.HtmlEncode(doctor.MedicalLicenseNumber ?? "—"))
            .AppendRow("Registration date", WebUtility.HtmlEncode(submitted))
            .AppendLine("</table>")
            .AppendLine($"<p><a href=\"{WebUtility.HtmlEncode(reviewUrl)}\">Review registration request</a></p>")
            .ToString();

        foreach (var email in recipients)
            await _emailSender.SendEmailAsync(email, subject, body).ConfigureAwait(false);
    }

    private async Task SendApprovalEmailAsync(ApplicationUser doctor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(doctor.Email))
            return;

        var subject = "Registration Approved";
        var body =
            $"<p>Your account has been approved.</p>" +
            $"<p>You may now access the {_options.PlatformDisplayName}.</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(BuildAbsoluteUrl("Login", "Account"))}\">Sign in</a></p>";

        await _emailSender.SendEmailAsync(doctor.Email, subject, body).ConfigureAwait(false);
    }

    private async Task SendRejectionEmailAsync(
        ApplicationUser doctor,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(doctor.Email))
            return;

        var subject = "Registration Request Rejected";
        var body =
            "<p>Your registration request has been rejected.</p>" +
            $"<p><strong>Reason:</strong> {WebUtility.HtmlEncode(reason)}</p>";

        await _emailSender.SendEmailAsync(doctor.Email, subject, body).ConfigureAwait(false);
    }

    private async Task<List<string>> ResolveAdminEmailsAsync(CancellationToken cancellationToken)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var admins = await _userManager.GetUsersInRoleAsync("Admin").ConfigureAwait(false);
        foreach (var admin in admins)
        {
            if (!string.IsNullOrWhiteSpace(admin.Email))
                set.Add(admin.Email.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_options.FallbackAdminNotificationEmails))
        {
            foreach (var part in _options.FallbackAdminNotificationEmails.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                    set.Add(trimmed);
            }
        }

        return set.ToList();
    }

    private string BuildAbsoluteUrl(string action, string controller, object? values = null)
    {
        var httpContext = _actionContextAccessor.ActionContext?.HttpContext;
        if (httpContext is null)
            return $"/{controller}/{action}";

        var urlHelper = _urlHelperFactory.GetUrlHelper(_actionContextAccessor.ActionContext!);
        var relative = urlHelper.Action(action, controller, values);
        if (string.IsNullOrEmpty(relative))
            return $"/{controller}/{action}";

        var request = httpContext.Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}{relative}";
    }
}

file static class DoctorRegistrationEmailBuilder
{
    public static StringBuilder AppendRow(this StringBuilder sb, string label, string value)
    {
        sb.Append("<tr><td style=\"padding:4px 12px 4px 0;font-weight:600\">")
            .Append(label)
            .Append("</td><td style=\"padding:4px 0\">")
            .Append(value)
            .AppendLine("</td></tr>");
        return sb;
    }
}
