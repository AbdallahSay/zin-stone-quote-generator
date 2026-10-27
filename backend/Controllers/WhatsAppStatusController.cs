using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FactoryQuoteApi.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FactoryQuoteApi.Controllers;

[ApiController, Route("api/whatsapp/status")]
public sealed class WhatsAppStatusController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ApplicationDbContext _db;
    public WhatsAppStatusController(IConfiguration config, ApplicationDbContext db) { _config = config; _db = db; }

    [HttpGet]
    public IActionResult Verify([FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token, [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = _config["WhatsApp:WebhookVerifyToken"];
        if (!_config.GetValue<bool>("WhatsApp:WebhookEnabled") || string.IsNullOrWhiteSpace(expected) ||
            string.IsNullOrWhiteSpace(token) || mode != "subscribe" || challenge?.Length > 200 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token))) return Unauthorized();
        return Content(challenge ?? "", "text/plain");
    }

    [HttpPost, RequestSizeLimit(262144)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var secret = _config["WhatsApp:AppSecret"];
        if (!_config.GetValue<bool>("WhatsApp:WebhookEnabled") || string.IsNullOrWhiteSpace(secret)) return NotFound();
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var signature = Request.Headers["X-Hub-Signature-256"].ToString();
        if (!signature.StartsWith("sha256=", StringComparison.Ordinal)) return Unauthorized();
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature[7..]); } catch { return Unauthorized(); }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied)) return Unauthorized();
        try
        {
            using var payload = JsonDocument.Parse(bytes);
            if (!payload.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) return BadRequest();
            foreach (var entry in entries.EnumerateArray())
            foreach (var change in entry.GetProperty("changes").EnumerateArray())
            {
                var value = change.GetProperty("value");
                if (!value.TryGetProperty("metadata", out var metadata) ||
                    metadata.GetProperty("phone_number_id").GetString() != _config["WhatsApp:PhoneNumberId"]) continue;
                if (!value.TryGetProperty("statuses", out var statuses)) continue;
                foreach (var status in statuses.EnumerateArray())
                {
                    var state = status.GetProperty("status").GetString();
                    if (state is not ("sent" or "delivered" or "read" or "failed")) continue;
                    var id = status.GetProperty("id").GetString();
                    var recipient = status.GetProperty("recipient_id").GetString();
                    var attempt = await _db.WhatsAppDeliveryAttempts.FirstOrDefaultAsync(a => a.MessageId == id && a.RecipientPhone == recipient, cancellationToken);
                    if (attempt is null) continue;
                    // Prevent delayed callbacks from downgrading terminal delivery evidence.
                    if (attempt.Status == "read" || attempt.Status == "delivered" && state is "sent" or "failed") continue;
                    attempt.Status = state!;
                    attempt.UpdatedAt = DateTime.UtcNow;
                }
            }
            await _db.SaveChangesAsync(cancellationToken);
            return Ok();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return BadRequest(); }
    }
}
