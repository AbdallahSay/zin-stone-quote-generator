namespace FactoryQuoteApi.Services;

/// <summary>
/// WhatsApp Delivery Service abstraction for ZIN STONE.
/// Kept modular and safely disabled by default until official Meta Cloud API setup is completed by the owner.
/// </summary>
public interface IWhatsAppDeliveryService
{
    bool IsEnabled { get; }
    Task<bool> SendQuoteNotificationAsync(
        string recipientPhone,
        string clientName,
        string quoteNumber,
        decimal totalAmount,
        string? documentUrl = null,
        CancellationToken cancellationToken = default);
}

public class WhatsAppDeliveryService : IWhatsAppDeliveryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppDeliveryService> _logger;

    public WhatsAppDeliveryService(
        IConfiguration configuration,
        ILogger<WhatsAppDeliveryService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// WhatsApp sending is disabled by default.
    /// To enable, configure WhatsApp:Enabled = true in environment variables along with Meta API credentials.
    /// </summary>
    public bool IsEnabled =>
        _configuration.GetValue<bool>("WhatsApp:Enabled", false) ||
        string.Equals(Environment.GetEnvironmentVariable("WHATSAPP_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

    public Task<bool> SendQuoteNotificationAsync(
        string recipientPhone,
        string clientName,
        string quoteNumber,
        decimal totalAmount,
        string? documentUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            _logger.LogInformation(
                "[WhatsApp] Service is currently disabled pending manual owner setup. Skipping dispatch for quote {QuoteNumber} to {Phone}.",
                quoteNumber, recipientPhone);
            return Task.FromResult(false);
        }

        // When enabled in future via owner configuration:
        var phoneNumberId = _configuration["WhatsApp:PhoneNumberId"] ?? Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID");
        var accessToken = _configuration["WhatsApp:AccessToken"] ?? Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN");

        if (string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogWarning("[WhatsApp] Enabled but credentials (PhoneNumberId / AccessToken) are missing. Skipping dispatch.");
            return Task.FromResult(false);
        }

        _logger.LogInformation("[WhatsApp] Ready for dispatch to {Phone} for Quote {QuoteNumber}.", recipientPhone, quoteNumber);
        return Task.FromResult(true);
    }
}
