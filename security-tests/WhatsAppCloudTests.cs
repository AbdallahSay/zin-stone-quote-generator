using System.Net;
using System.Text;
using System.Text.Json;
using FactoryQuoteApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FactoryQuoteApi.SecurityTests;

public sealed class WhatsAppCloudTests : IAsyncLifetime
{
    private readonly SyntheticHost _host = new(false);
    public Task InitializeAsync() => _host.StartAsync();
    public Task DisposeAsync() => _host.DisposeAsync();
    private IConfiguration Configuration(bool enabled = true, bool window = true) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["WhatsApp:Enabled"] = enabled.ToString(), ["WhatsApp:TestMode"] = "true",
        ["WhatsApp:ApiVersion"] = "v99.0", ["WhatsApp:PhoneNumberId"] = "123456",
        ["WhatsApp:AccessToken"] = "synthetic-meta-token", ["WhatsApp:ApprovedTestRecipients:0"] = "201000000000",
        ["WhatsApp:ConfirmedServiceWindowOpen"] = window.ToString(), ["WhatsApp:DocumentTemplateName"] = "synthetic_document",
        ["WhatsApp:TemplateLanguage"] = "ar" }).Build();

    private void Pdf() => File.WriteAllText(Path.Combine(_host.RootPath,"App_Data","quotes","synthetic.pdf"), "%PDF-1.4 synthetic test document");

    [Fact]
    public async Task Disabled_service_makes_no_network_requests()
    {
        var transport = new MetaTransport();
        await _host.WithDb(async db => {
            var service = new WhatsAppDeliveryService(Configuration(false), transport, db, _host.App.Environment);
            Assert.False(await service.SendQuoteNotificationAsync("201000000000","TEST","TEST-1",0,"synthetic.pdf"));
            Assert.Empty(await db.WhatsAppDeliveryAttempts.ToListAsync());
        });
        Assert.Equal(0, transport.Requests);
    }

    [Theory]
    [InlineData("209999999999", "synthetic.pdf")]
    [InlineData("201000000000", "../outside.pdf")]
    [InlineData("201000000000", "https://unapproved.invalid/document.pdf")]
    public async Task Unapproved_recipient_or_document_is_rejected(string phone, string document)
    {
        var transport = new MetaTransport();
        await _host.WithDb(async db => Assert.False(await new WhatsAppDeliveryService(Configuration(),transport,db,_host.App.Environment)
            .SendQuoteNotificationAsync(phone,"TEST","TEST-1",0,document)));
        Assert.Equal(0,transport.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Upload_and_document_or_template_send_is_durably_deduplicated(bool window)
    {
        Pdf(); var transport = new MetaTransport();
        await _host.WithDb(async db => {
            var service = new WhatsAppDeliveryService(Configuration(window:window),transport,db,_host.App.Environment);
            Assert.True(await service.SendQuoteNotificationAsync("201000000000","TEST","TEST-1",0,"synthetic.pdf"));
            Assert.False(await service.SendQuoteNotificationAsync("201000000000","TEST","TEST-1",0,"synthetic.pdf"));
            var attempt = await db.WhatsAppDeliveryAttempts.SingleAsync();
            Assert.Equal("Accepted",attempt.Status); Assert.Equal("synthetic-message",attempt.MessageId);
            Assert.Equal("synthetic-media",attempt.MediaId); Assert.Equal(1,attempt.SendAttempts);
        });
        Assert.Equal(2,transport.Requests); Assert.Contains("application/pdf",transport.UploadBody);
        using var payload = JsonDocument.Parse(transport.MessageBody);
        Assert.Equal(window ? "document" : "template",payload.RootElement.GetProperty("type").GetString());
        var actualFilename = window
            ? payload.RootElement.GetProperty("document").GetProperty("filename").GetString()
            : payload.RootElement.GetProperty("template").GetProperty("components")[0]
                .GetProperty("parameters")[0].GetProperty("document").GetProperty("filename").GetString();
        Assert.Equal("عرض سعر شركة TEST.pdf", actualFilename);
        Assert.DoesNotContain("synthetic-meta-token",transport.MessageBody);
    }

    [Fact]
    public async Task Expired_token_records_failure_without_sending_document()
    {
        Pdf(); var transport = new MetaTransport { RejectUpload = true };
        await _host.WithDb(async db => {
            Assert.False(await new WhatsAppDeliveryService(Configuration(),transport,db,_host.App.Environment)
                .SendQuoteNotificationAsync("201000000000","TEST","TEST-1",0,"synthetic.pdf"));
            Assert.Equal("TokenExpired", (await db.WhatsAppDeliveryAttempts.SingleAsync()).Status);
        });
        Assert.Equal(1,transport.Requests);
    }

    [Fact]
    public async Task Ambiguous_document_outcome_is_never_retried()
    {
        Pdf(); var transport = new MetaTransport { TimeoutMessage = true };
        await _host.WithDb(async db => {
            var service = new WhatsAppDeliveryService(Configuration(),transport,db,_host.App.Environment);
            Assert.False(await service.SendQuoteNotificationAsync("201000000000","TEST","TEST-1",0,"synthetic.pdf"));
            Assert.False(await service.SendQuoteNotificationAsync("201000000000","TEST","TEST-1",0,"synthetic.pdf"));
            Assert.Equal("UnknownOutcome", (await db.WhatsAppDeliveryAttempts.SingleAsync()).Status);
        });
        Assert.Equal(2,transport.Requests);
    }

    private sealed class MetaTransport : HttpMessageHandler, IHttpClientFactory
    {
        internal int Requests; internal bool RejectUpload, TimeoutMessage;
        internal string UploadBody="", MessageBody="";
        public HttpClient CreateClient(string name) => new(this,false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Assert.Equal("graph.facebook.com",request.RequestUri!.Host);
            Assert.Equal("Bearer",request.Headers.Authorization!.Scheme); Requests++;
            if(request.RequestUri.AbsolutePath.EndsWith("/media")) {
                UploadBody=await request.Content!.ReadAsStringAsync(cancellationToken);
                return new(RejectUpload ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) {Content=new StringContent("{\"id\":\"synthetic-media\"}")};
            }
            MessageBody=await request.Content!.ReadAsStringAsync(cancellationToken);
            if(TimeoutMessage) throw new TaskCanceledException("Synthetic ambiguous outcome");
            return new(HttpStatusCode.OK) {Content=new StringContent("{\"messages\":[{\"id\":\"synthetic-message\"}]}")};
        }
    }
}
