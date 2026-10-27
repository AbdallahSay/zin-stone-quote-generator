using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FactoryQuoteApi.Models;
using FactoryQuoteApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FactoryQuoteApi.SecurityTests;

public sealed class AdditionalIsolationHttpTests : IAsyncLifetime
{
    private readonly SyntheticHost _host = new(false);
    public Task InitializeAsync() => _host.StartAsync();
    public Task DisposeAsync() => _host.DisposeAsync();
    private HttpRequestMessage Request(HttpMethod method,string url,string? token=null,object? body=null)
    {
        var request=new HttpRequestMessage(method,url);
        if(token is not null) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(body is not null) request.Content=JsonContent.Create(body);
        return request;
    }
    [Theory]
    [InlineData("/api/clients")]
    [InlineData("/api/products")]
    public async Task Customer_and_product_endpoints_require_authentication(string route)
    { using var response=await _host.Client.GetAsync(route);Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); }

    [Fact]
    public async Task Employee_can_only_read_own_clients_and_never_password_hashes()
    {
        await _host.WithDb(async db => {
            db.Clients.AddRange(new Client{ClientName="TEST A",CreatedById=101},new Client{ClientName="TEST B",CreatedById=102});
            await db.SaveChangesAsync();
        });
        using var request=Request(HttpMethod.Get,"/api/clients",_host.EmployeeToken(101));
        using var response=await _host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var json=await response.Content.ReadAsStringAsync();Assert.Contains("TEST A",json);Assert.DoesNotContain("TEST B",json);
        Assert.DoesNotContain("passwordHash",json,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_file_cannot_be_read_with_another_employee_bearer()
    {
        File.WriteAllText(Path.Combine(_host.RootPath,"App_Data","quotes","preview_test.pdf"),"%PDF-TEST");
        using var request=Request(HttpMethod.Get,"/quotes/preview_test.pdf",_host.EmployeeToken(102));
        using var response=await _host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task Owner_rotation_verifies_current_password_and_revokes_old_sessions()
    {
        const string current="SyntheticCurrentOwnerSecret2026!";
        const string next="SyntheticNewOwnerSecret2026!";
        await _host.WithDb(async db => {
            using var scope=_host.App.Services.CreateScope();
            var security=scope.ServiceProvider.GetRequiredService<ISecurityService>();
            var owner=await db.SystemCredentials.SingleAsync(c=>c.Role=="owner");owner.PasswordHash=security.HashPassword(current);await db.SaveChangesAsync();
        });
        var oldToken=_host.RoleToken("owner");
        using(var bad=Request(HttpMethod.Post,"/api/auth/rotate-owner-password",oldToken,new{CurrentPassword="wrong",NewPassword=next,ConfirmPassword=next}))
        using(var rejected=await _host.Client.SendAsync(bad)) Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        using(var request=Request(HttpMethod.Post,"/api/auth/rotate-owner-password",oldToken,new{CurrentPassword=current,NewPassword=next,ConfirmPassword=next}))
        using(var response=await _host.Client.SendAsync(request)) Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using(var verify=Request(HttpMethod.Get,"/api/auth/verify?role=owner",oldToken))
        using(var revoked=await _host.Client.SendAsync(verify)) Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
        using var login=await _host.Client.PostAsJsonAsync("/api/auth/login",new{Role="owner",Password=next});
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
    }

    [Fact]
    public async Task Owner_can_create_first_employee_in_a_fresh_database_with_valid_audit_foreign_key()
    {
        await _host.WithDb(async db => { await db.Quotes.ExecuteDeleteAsync(); await db.Users.ExecuteDeleteAsync(); });
        using var request=Request(HttpMethod.Post,"/api/admin/employees",_host.RoleToken("owner"),
            new {FullName="TEST first employee",Email="first@example.invalid",Password="SyntheticFirstEmployeeSecret2026!",ConfirmPassword="SyntheticFirstEmployeeSecret2026!"});
        using var response=await _host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        await _host.WithDb(async db => {
            var actor=await db.Users.SingleAsync(u=>u.Role=="System");Assert.False(actor.IsActive);
            Assert.Equal(actor.Id,(await db.AuditLogs.SingleAsync()).UserId);
        });
    }

    [Fact]
    public async Task Webhook_requires_signature_and_only_updates_matching_delivery()
    {
        var config=_host.App.Services.GetRequiredService<IConfiguration>();
        config["WhatsApp:WebhookEnabled"]="true";config["WhatsApp:AppSecret"]="synthetic-callback-secret";config["WhatsApp:PhoneNumberId"]="123456";
        await _host.WithDb(async db=>{
            db.WhatsAppDeliveryAttempts.Add(new WhatsAppDeliveryAttempt{Id=new string('A',64),QuoteNumber="TEST",RecipientPhone="201000000000",MessageId="mock-message",Status="Accepted",CreatedAt=DateTime.UtcNow});await db.SaveChangesAsync();
        });
        var body=JsonSerializer.Serialize(new{entry=new[]{new{changes=new[]{new{value=new{metadata=new{phone_number_id="123456"},statuses=new[]{new{id="mock-message",recipient_id="201000000000",status="delivered"}}}}}}}});
        using(var rejected=await _host.Client.PostAsync("/api/whatsapp/status",new StringContent(body,Encoding.UTF8,"application/json"))) Assert.Equal(HttpStatusCode.Unauthorized,rejected.StatusCode);
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/whatsapp/status"){Content=new StringContent(body,Encoding.UTF8,"application/json")};
        request.Headers.Add("X-Hub-Signature-256","sha256="+Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("synthetic-callback-secret"),Encoding.UTF8.GetBytes(body))).ToLowerInvariant());
        using var response=await _host.Client.SendAsync(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        await _host.WithDb(async db=>Assert.Equal("delivered",(await db.WhatsAppDeliveryAttempts.SingleAsync()).Status));
    }
}
