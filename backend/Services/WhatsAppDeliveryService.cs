using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FactoryQuoteApi.Data;
using FactoryQuoteApi.Models;
using Microsoft.EntityFrameworkCore;

namespace FactoryQuoteApi.Services;

public interface IWhatsAppDeliveryService
{
    bool IsEnabled { get; }
    Task<bool> SendQuoteNotificationAsync(string recipientPhone, string clientName, string quoteNumber,
        decimal totalAmount, string? documentUrl = null, CancellationToken cancellationToken = default);
}

// Explicit test invocation only. No background worker or automatic quotation trigger.
public sealed class WhatsAppDeliveryService : IWhatsAppDeliveryService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _clients;
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    public WhatsAppDeliveryService(IConfiguration configuration, IHttpClientFactory clients,
        ApplicationDbContext db, IWebHostEnvironment env)
    { _configuration = configuration; _clients = clients; _db = db; _env = env; }

    public bool IsEnabled => _configuration.GetValue<bool>("WhatsApp:Enabled") &&
        _configuration.GetValue<bool>("WhatsApp:TestMode", true);

    public async Task<bool> SendQuoteNotificationAsync(string recipientPhone, string clientName,
        string quoteNumber, decimal totalAmount, string? documentUrl = null, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return false;
        if (string.IsNullOrWhiteSpace(documentUrl) || Path.GetFileName(documentUrl) != documentUrl ||
            documentUrl.Contains('\\') || documentUrl.Contains('/') || !documentUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return false;
        var recipients = _configuration.GetSection("WhatsApp:ApprovedTestRecipients").Get<string[]>() ?? [];
        if (!Regex.IsMatch(recipientPhone, @"^[1-9]\d{7,14}$") || !recipients.Contains(recipientPhone, StringComparer.Ordinal)) return false;
        var version = _configuration["WhatsApp:ApiVersion"];
        var phoneId = _configuration["WhatsApp:PhoneNumberId"];
        var token = _configuration["WhatsApp:AccessToken"];
        var template = _configuration["WhatsApp:DocumentTemplateName"];
        var language = _configuration["WhatsApp:TemplateLanguage"];
        var serviceWindow = _configuration.GetValue<bool>("WhatsApp:ConfirmedServiceWindowOpen");
        if (version is null || !Regex.IsMatch(version, @"^v\d+\.\d+$") || phoneId is null || !Regex.IsMatch(phoneId, @"^\d+$") ||
            string.IsNullOrWhiteSpace(token) || token.StartsWith('<') ||
            (!serviceWindow && (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(language)))) return false;
        // Resolve the official customer and optional project from the saved quotation.
        // The disk filename remains immutable; only the WhatsApp attachment label changes.
        var savedQuote = await _db.Quotes.AsNoTracking()
            .Where(q => q.QuoteNumber == quoteNumber)
            .Select(q => new { q.ClientName, q.ProjectName })
            .SingleOrDefaultAsync(cancellationToken);
        var attachmentName = QuoteDisplayFileName.BuildPdfName(
            savedQuote?.ClientName ?? clientName, savedQuote?.ProjectName);
        var path = Path.Combine(_env.ContentRootPath, "App_Data", "quotes", documentUrl);
        if (!File.Exists(path)) return false;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-") return false;
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{quoteNumber}|{recipientPhone}|{hash}")));
        if (await _db.WhatsAppDeliveryAttempts.AnyAsync(a => a.Id == id, cancellationToken)) return false;
        var attempt = new WhatsAppDeliveryAttempt { Id = id, QuoteNumber = quoteNumber,
            RecipientPhone = recipientPhone, FileSha256 = hash, Status = "Claimed", CreatedAt = DateTime.UtcNow };
        _db.WhatsAppDeliveryAttempts.Add(attempt);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            _db.Entry(attempt).State = EntityState.Detached;
            if (await _db.WhatsAppDeliveryAttempts.AnyAsync(a => a.Id == id, cancellationToken)) return false;
            throw;
        }
        using var client = _clients.CreateClient("WhatsAppMeta");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var baseUrl = $"https://graph.facebook.com/{version}/{phoneId}";
        try
        {
            string? mediaId = null;
            for (var uploadAttempt = 0; uploadAttempt < 3; uploadAttempt++)
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent("whatsapp"), "messaging_product");
                var file = new ByteArrayContent(bytes);
                file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                form.Add(file, "file", attachmentName);
                attempt.UploadAttempts++;
                using var upload = await client.PostAsync(baseUrl + "/media", form, cancellationToken);
                if (upload.IsSuccessStatusCode)
                {
                    using var json = JsonDocument.Parse(await upload.Content.ReadAsStringAsync(cancellationToken));
                    mediaId = json.RootElement.GetProperty("id").GetString();
                    break;
                }
                if (upload.StatusCode != HttpStatusCode.TooManyRequests && (int)upload.StatusCode < 500)
                { attempt.Status = upload.StatusCode == HttpStatusCode.Unauthorized ? "TokenExpired" : "UploadRejected"; break; }
                await Task.Delay(TimeSpan.FromSeconds(uploadAttempt + 1), cancellationToken);
            }
            if (string.IsNullOrWhiteSpace(mediaId)) { if (attempt.Status == "Claimed") attempt.Status = "UploadFailed"; return false; }
            attempt.MediaId = mediaId;
            object payload = serviceWindow
                ? new { messaging_product = "whatsapp", to = recipientPhone, type = "document",
                    document = new { id = mediaId, filename = attachmentName, caption = $"TEST {quoteNumber}" } }
                : new { messaging_product = "whatsapp", to = recipientPhone, type = "template",
                    template = new { name = template, language = new { code = language },
                        components = new object[] { new { type = "header", parameters = new object[] {
                            new { type = "document", document = new { id = mediaId, filename = attachmentName } } } },
                            new { type = "body", parameters = new object[] {
                                new { type = "text", text = quoteNumber }, new { type = "text", text = clientName } } } } } };
            // A timeout/5xx may mean accepted delivery. Never automatically retry a document POST.
            attempt.Status = "Submitting";
            attempt.SendAttempts++;
            await _db.SaveChangesAsync(cancellationToken);
            using var response = await client.PostAsJsonAsync(baseUrl + "/messages", payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                attempt.Status = response.StatusCode == HttpStatusCode.Unauthorized ? "TokenExpired" :
                    (int)response.StatusCode >= 500 ? "UnknownOutcome" : "Rejected";
                attempt.HttpStatus = (int)response.StatusCode;
                return false;
            }
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            attempt.MessageId = result.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();
            attempt.Status = "Accepted";
            return !string.IsNullOrWhiteSpace(attempt.MessageId);
        }
        catch (OperationCanceledException) { attempt.Status = attempt.SendAttempts > 0 ? "UnknownOutcome" : "UploadFailed"; return false; }
        catch (HttpRequestException) { attempt.Status = attempt.SendAttempts > 0 ? "UnknownOutcome" : "UploadFailed"; return false; }
        catch (JsonException) { attempt.Status = attempt.SendAttempts > 0 ? "UnknownOutcome" : "UploadFailed"; return false; }
        finally { attempt.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(CancellationToken.None); }
    }
}
