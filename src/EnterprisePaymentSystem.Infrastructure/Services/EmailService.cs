using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace EnterprisePaymentSystem.Infrastructure.Services;

public class EmailService : IEmailService
{
    private static readonly CultureInfo IndianCulture = CultureInfo.GetCultureInfo("en-IN");

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public EmailConfigurationStatusDto GetConfigurationStatus()
    {
        var resendKey = GetConfig("Email:ResendApiKey", "RESEND_API_KEY");
        if (!string.IsNullOrWhiteSpace(resendKey))
        {
            var fromAddr = GetConfig("Email:FromAddress", "EMAIL_FROM") ?? "onboarding@resend.dev";
            return new EmailConfigurationStatusDto(
                IsConfigured: true,
                Provider: "Resend",
                FromAddress: fromAddr,
                Message: $"Resend API active (sending from {fromAddr}).");
        }

        var smtpHost = GetConfig("Email:SmtpHost", "SMTP_HOST");
        if (!string.IsNullOrWhiteSpace(smtpHost))
        {
            var smtpPort = int.TryParse(GetConfig("Email:SmtpPort", "SMTP_PORT"), out var p) ? p : 587;
            var smtpUser = GetConfig("Email:SmtpUser", "SMTP_USER", "SMTP_USERNAME");
            var fromAddr = GetConfig("Email:FromAddress", "EMAIL_FROM", "SMTP_FROM") ?? smtpUser;
            return new EmailConfigurationStatusDto(
                IsConfigured: true,
                Provider: "SMTP",
                FromAddress: fromAddr,
                Message: $"SMTP configured ({smtpHost}:{smtpPort} as {fromAddr ?? smtpHost}).");
        }

        return new EmailConfigurationStatusDto(
            IsConfigured: false,
            Provider: "None",
            FromAddress: null,
            Message: "Email delivery not configured on server. Add Email__ResendApiKey or Email__SmtpHost / Email__SmtpUser / Email__SmtpPassword in Render Environment Variables to receive inbox emails.");
    }

    public async Task<EmailDispatchResult> SendAuditReportReadyEmailAsync(
        AuditEmailNotification notification,
        CancellationToken ct = default)
    {
        var subject = $"Audit Pack Ready: {notification.ReportTitle} ({notification.PeriodLabel})";
        var htmlBody = BuildHtmlBody(notification);
        var fromName = GetConfig("Email:FromName", "EMAIL_FROM_NAME") ?? "Khan Enterprises Audit";

        // 1. Try Resend REST API
        var resendApiKey = GetConfig("Email:ResendApiKey", "RESEND_API_KEY");
        if (!string.IsNullOrWhiteSpace(resendApiKey))
        {
            var rawFrom = GetConfig("Email:FromAddress", "EMAIL_FROM") ?? "onboarding@resend.dev";
            var formattedFrom = rawFrom.Contains('<') ? rawFrom : $"{fromName} <{rawFrom}>";

            try
            {
                var client = _httpClientFactory.CreateClient("EmailClient");
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resendApiKey);
                var payload = new
                {
                    from = formattedFrom,
                    to = new[] { notification.RecipientEmail },
                    subject,
                    html = htmlBody
                };
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var resp = await client.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Audit Pack email sent via Resend to {Email}", notification.RecipientEmail);
                    return new EmailDispatchResult(true, $"Email sent via Resend to {notification.RecipientEmail}");
                }

                var errorBody = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Resend API returned {StatusCode}: {Body}", resp.StatusCode, errorBody);
                return new EmailDispatchResult(false, $"Resend returned {(int)resp.StatusCode} ({ExtractResendHint(errorBody)}); files ready in Audit Files.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Resend delivery failed for {Email}", notification.RecipientEmail);
                return new EmailDispatchResult(false, $"Resend failed ({ex.Message}); files ready in Audit Files.");
            }
        }

        // 2. Try MailKit SMTP (supports port 465 SSL, 587 STARTTLS, Gmail App Passwords, Outlook, etc.)
        var smtpHost = GetConfig("Email:SmtpHost", "SMTP_HOST");
        if (!string.IsNullOrWhiteSpace(smtpHost))
        {
            var smtpPort = int.TryParse(GetConfig("Email:SmtpPort", "SMTP_PORT"), out var p) ? p : 587;
            var smtpUser = GetConfig("Email:SmtpUser", "SMTP_USER", "SMTP_USERNAME");
            var smtpPassword = GetConfig("Email:SmtpPassword", "SMTP_PASSWORD", "SMTP_PASS");
            var fromAddress = GetConfig("Email:FromAddress", "EMAIL_FROM", "SMTP_FROM") ?? smtpUser;

            if (string.IsNullOrWhiteSpace(fromAddress))
            {
                return new EmailDispatchResult(false, "SMTP FromAddress/SmtpUser missing; files ready in Audit Files.");
            }

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(fromName, fromAddress));
                message.To.Add(MailboxAddress.Parse(notification.RecipientEmail));
                message.Subject = subject;
                message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

                var enableSslConfig = GetConfig("Email:EnableSsl", "SMTP_ENABLE_SSL");
                var enableSsl = string.IsNullOrWhiteSpace(enableSslConfig) || bool.Parse(enableSslConfig);

                var socketOption = smtpPort == 465
                    ? SecureSocketOptions.SslOnConnect
                    : (enableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);

                using var smtpClient = new SmtpClient();
                await smtpClient.ConnectAsync(smtpHost, smtpPort, socketOption, ct);
                if (!string.IsNullOrWhiteSpace(smtpUser))
                {
                    await smtpClient.AuthenticateAsync(smtpUser, smtpPassword ?? string.Empty, ct);
                }
                await smtpClient.SendAsync(new MailKit.SendRequest(message), ct);
                await smtpClient.DisconnectAsync(true, ct);

                _logger.LogInformation("Audit Pack email sent via SMTP ({Host}:{Port}) to {Email}", smtpHost, smtpPort, notification.RecipientEmail);
                return new EmailDispatchResult(true, $"Email sent via SMTP to {notification.RecipientEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SMTP delivery failed for {Email} via {Host}:{Port}", notification.RecipientEmail, smtpHost, smtpPort);
                return new EmailDispatchResult(false, $"SMTP failed ({ex.Message}); files ready in Audit Files.");
            }
        }

        // 3. Honest reporting when server has no mail provider configured
        _logger.LogWarning(
            "[Email Not Configured] Cannot email Audit Pack to {Email} because Email:ResendApiKey and Email:SmtpHost are empty.",
            notification.RecipientEmail);

        return new EmailDispatchResult(
            false,
            $"Email not sent: Server mail provider not configured (files ready in Audit Files for {notification.RecipientEmail}).");
    }

    private string? GetConfig(params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = _configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }
        return null;
    }

    private static string ExtractResendHint(string responseBody)
    {
        if (responseBody.Contains("testing emails", StringComparison.OrdinalIgnoreCase) ||
            responseBody.Contains("verify a domain", StringComparison.OrdinalIgnoreCase))
        {
            return "Resend sandbox only permits verified sender domains";
        }
        return "check API key / sender domain";
    }

    private static string BuildHtmlBody(AuditEmailNotification n)
    {
        var reqAmt = $"INR {n.TotalRequestedAmount.ToString("N2", IndianCulture)}";
        var appAmt = $"INR {n.TotalApprovedAmount.ToString("N2", IndianCulture)}";
        return $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:640px;margin:0 auto;background:#0f172a;color:#e2e8f0;padding:24px;border-radius:12px;">
              <h2 style="color:#fbbf24;margin:0 0 8px 0;">{WebUtility.HtmlEncode(n.TenantName)} — Audit Pack Ready</h2>
              <p style="color:#94a3b8;margin:0 0 18px 0;">Hello {WebUtility.HtmlEncode(n.RecipientName)}, your background audit export has finished generating.</p>
              <div style="background:#1e293b;padding:16px;border-radius:8px;margin-bottom:18px;border:1px solid #334155;">
                <p style="margin:4px 0;"><strong>Report:</strong> {WebUtility.HtmlEncode(n.ReportTitle)}</p>
                <p style="margin:4px 0;"><strong>Period:</strong> {WebUtility.HtmlEncode(n.PeriodLabel)}</p>
                <p style="margin:4px 0;"><strong>Filters:</strong> {WebUtility.HtmlEncode(n.FilterSummary)}</p>
                <p style="margin:4px 0;"><strong>Matching Records:</strong> {n.TotalRecords}</p>
                <p style="margin:4px 0;"><strong>Requested Total:</strong> {reqAmt} | <strong>Approved &amp; Disbursed:</strong> {appAmt}</p>
              </div>
              <div style="margin:20px 0;">
                <a href="{WebUtility.HtmlEncode(n.PdfDownloadUrl)}" style="display:inline-block;background:#f59e0b;color:#0f172a;font-weight:700;padding:10px 16px;border-radius:8px;text-decoration:none;margin-right:10px;">Download Breakdown (PDF)</a>
                <a href="{WebUtility.HtmlEncode(n.ExcelDownloadUrl)}" style="display:inline-block;background:#10b981;color:#0f172a;font-weight:700;padding:10px 16px;border-radius:8px;text-decoration:none;">Download Expenses (Excel)</a>
              </div>
              <p style="font-size:12px;color:#94a3b8;">Note: Each file is automatically cleared from server database storage once downloaded. You can click <strong>Regenerate</strong> anytime in the <a href="{WebUtility.HtmlEncode(n.AuditFilesPortalUrl)}" style="color:#fbbf24;">Audit Files section</a> using your saved period &amp; filter details.</p>
            </div>
            """;
    }
}
