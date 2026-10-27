using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FactoryQuoteApi.DTOs;
using FactoryQuoteApi.Models;
using FactoryQuoteApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FactoryQuoteApi.SecurityTests;

public sealed class ZinStoneIsolationTests : IAsyncLifetime
{
    private readonly SyntheticHost _host = new(false);
    public Task InitializeAsync() => _host.StartAsync();
    public Task DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Disabled_telegram_never_schedules_sends_retries_or_imports_after_restart()
    {
        using var scope = _host.App.Services.CreateScope();
        var telegram = scope.ServiceProvider.GetRequiredService<ITelegramDeliveryService>();
        await telegram.EnsureInitializedAsync();
        await telegram.EnsureInitializedAsync();
        await _host.WithDb(async db => {
            Assert.Empty(await db.TelegramRecipients.ToListAsync());
            db.TelegramRecipients.Add(new TelegramRecipient { ChatId = "synthetic-approved", DisplayName = "TEST", IsActive = true });
            db.QuoteTelegramDeliveries.Add(new QuoteTelegramDelivery { QuoteId = 11, QuoteNumber = "SEC-01-11", ChatId = "synthetic-approved", Status = "Pending" });
            await db.SaveChangesAsync();
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes/generate") {
            Content = JsonContent.Create(new QuoteRequestDto { ClientName = "TEST ZIN ISOLATION", Items = [new() { ProductName = "TEST", Quantity = 1, UnitPrice = 1 }] }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.EmployeeToken(101));
        using var response = await _host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await telegram.ProcessPendingOutboxAsync());
        Assert.Empty(await telegram.SendQuoteDocumentAsync(11));
        Assert.Empty(await telegram.RetryQuoteDeliveryAsync(11));
        Assert.Equal(0, _host.Transport.Sends);
        await _host.WithDb(async db => Assert.Equal(1, await db.QuoteTelegramDeliveries.CountAsync()));
    }

    [Fact]
    public void Empty_chat_configuration_has_no_fallback()
    {
        using var scope = _host.App.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IConfiguration>()["Telegram:ChatIds"] = "";
        Assert.Empty(((TelegramDeliveryService)scope.ServiceProvider.GetRequiredService<ITelegramDeliveryService>()).GetConfiguredChatIds());
    }

    [Theory]
    [InlineData("Server=db71745.public.databaseasp.net;Database=db71745;Encrypt=True")]
    [InlineData("Server=db76143.public.databaseasp.net;Database=db71745;Encrypt=True")]
    [InlineData("Server=other.example.invalid;Database=db76143;Encrypt=True")]
    [InlineData("Server=db76143.public.databaseasp.net;Database=db76143;Encrypt=False")]
    [InlineData("")]
    [InlineData("Host=somewhere;Database=postgres")]
    public void Production_database_rejects_unapproved_targets(string connection) =>
        Assert.Throws<InvalidOperationException>(() => ProductionIsolation.ValidateConnection(connection, true));

    [Fact]
    public void Approved_database_is_accepted() => ProductionIsolation.ValidateConnection(
        "Server=db76143.public.databaseasp.net;Database=db76143;Encrypt=True", true);

    [Theory]
    [InlineData(null)]
    [InlineData("FactoryQuote_Prod_HMAC_SecretKey_2026_!#SafeMasterKey99")]
    [InlineData("<YOUR_HMAC_SIGNING_SECRET>")]
    public void Missing_or_shared_signing_key_fails_closed(string? key)
    {
        using var scope = _host.App.Services.CreateScope();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Security:SigningKey"] = key }).Build();
        Assert.Throws<InvalidOperationException>(() => new SecurityService(scope.ServiceProvider.GetRequiredService<FactoryQuoteApi.Data.ApplicationDbContext>(), config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityService>.Instance));
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("..\\outside.pdf")]
    [InlineData("C:outside.pdf")]
    [InlineData("/outside.pdf")]
    public async Task Local_storage_rejects_traversal(string name)
    {
        var storage = new LocalDocumentStorageService(_host.App.Environment);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.GetDocumentStreamAsync(name));
    }
}
