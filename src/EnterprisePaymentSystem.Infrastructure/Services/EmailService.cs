using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using EnterprisePaymentSystem.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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

    public async Task<EmailDispatchResult> SendAuditReportReadyEmailAsync(
        AuditEmailNotification notification,
        CancellationToken ct = default)
    {
        var subject = $"Audit Pack Ready: {notification.ReportTitle} ({notification.PeriodLabel})";
        var htmlBody = BuildHtmlBody(notification);

        var resendApiKey = _configuration["Email:ResendApiKey"];
        var fromAddress = _configuration["Email:FromAddress"] ?? "Khan Enterprises Audit <onboarding@resend.dev>";

        // 1. Try Resend REST API if configured
        if (!string.IsNullOrWhiteSpace(resendApiKey))
        {
            try
            {
                var client = _httpClientFactory.CreateClient("EmailClient");
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resendApiKey);
                var payload = new
                {
                    from = fromAddress,
                    to = new[] { notification.RecipientEmail },
                    subject,
                    html = htmlBody
                };
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var resp = await client.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Audit Pack email dispatched via Resend to {Email}", notification.RecipientEmail);
                    return new EmailDispatchResult(true, $"Email sent via Resend to {notification.RecipientEmail}");
                }

                var errorBody = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Resend API returned {StatusCode}: {Body}", resp.StatusCode, errorBody);
                return new EmailDispatchResult(false, $"Resend returned {(int)resp.StatusCode}; files saved in Audit Files.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Resend delivery failed for {Email}", notification.RecipientEmail);
                return new EmailDispatchResult(false, $"Email delivery error ({ex.Message}); files saved in Audit Files.");
            }
        }

        // 2. Try SMTP if configured
        var smtpHost = _configuration["Email:SmtpHost"];
        if (!string.IsNullOrWhiteSpace(smtpHost))
        {
            try
            {
                var smtpPort = int.TryParse(_configuration["Email:SmtpPort"], out var p) ? p : 587;
                var smtpUser = _configuration["Email:SmtpUser"];
                var smtpPassword = _configuration["Email:SmtpPassword"];
                var enableSsl = !bool.TryParse(_configuration["Email:EnableSsl"], out var ssl) || ssl;

                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                message.To.Add(notification.RecipientEmail);

                using var smtpClient = new SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = enableSsl
                };
                if (!string.IsNullOrWhiteSpace(smtpUser))
                {
                    smtpClient.Credentials = new NetworkCredential(smtpUser, smtpPassword ?? string.Empty);
                }

                await smtpClient.SendMailAsync(message, ct);
                _logger.LogInformation("Audit Pack email dispatched via SMTP to {Email}", notification.RecipientEmail);
                return new EmailDispatchResult(true, $"Email sent via SMTP to {notification.RecipientEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SMTP delivery failed for {Email}", notification.RecipientEmail);
                return new EmailDispatchResult(false, $"SMTP delivery error ({ex.Message}); files saved in Audit Files.");
            }
        }

        // 3. Structured simulation fallback when no mail provider key is configured
        _logger.LogInformation(
            "[Simulated Email Dispatch] To: {Email} | Subject: {Subject} | PDF: {PdfUrl} | Excel: {ExcelUrl}",
            notification.RecipientEmail,
            subject,
            notification.PdfDownloadUrl,
            notification.ExcelDownloadUrl);

        return new EmailDispatchResult(
            true,
            $"Dispatched to {notification.RecipientEmail} (Mail preview logged; download anytime in Audit Files)");
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
              <p style="font-size:12px;color:#94a3b8;">You can also access all generated files from the Admin Portal <a href="{WebUtility.HtmlEncode(n.AuditFilesPortalUrl)}" style="color:#fbbf24;">Audit Files section</a>.</p>
            </div>
            """;
    }
}
