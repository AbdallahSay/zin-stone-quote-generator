using System.ComponentModel.DataAnnotations;
namespace FactoryQuoteApi.Models;

public sealed class WhatsAppDeliveryAttempt
{
    [Key, MaxLength(64)] public string Id { get; set; } = "";
    [MaxLength(100)] public string QuoteNumber { get; set; } = "";
    [MaxLength(20)] public string RecipientPhone { get; set; } = "";
    [MaxLength(64)] public string FileSha256 { get; set; } = "";
    [MaxLength(30)] public string Status { get; set; } = "Claimed";
    [MaxLength(200)] public string? MediaId { get; set; }
    [MaxLength(200)] public string? MessageId { get; set; }
    public int UploadAttempts { get; set; }
    public int SendAttempts { get; set; }
    public int? HttpStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
