namespace MedicalAIPlatform.Services;

/// <summary>Logs outbound mail (replace with SMTP/SendGrid in production).</summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        _logger.LogInformation("Email → {Email} | Subject: {Subject}", email, subject);
        _logger.LogDebug("Email body: {Body}", htmlMessage);
        return Task.CompletedTask;
    }
}
