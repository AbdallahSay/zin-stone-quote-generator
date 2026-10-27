using Microsoft.EntityFrameworkCore;
using FactoryQuoteApi.Data;
using FactoryQuoteApi.Models;
using FactoryQuoteApi.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

// Use pure managed networking for SQL Client to avoid native SNI.dll dependencies
AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);

var builder = WebApplication.CreateBuilder(args);

// Configure dynamic listening port only if PORT env var is present (e.g. Docker/Render), never under IIS
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// Configure SQL Server DbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? string.Empty;

ProductionIsolation.ValidateConnection(connectionString, builder.Environment.IsProduction());

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Register HTTP Client Factory
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("WhatsAppMeta", client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IWhatsAppDeliveryService, WhatsAppDeliveryService>();
builder.Services.AddScoped<IDocumentStorageService, LocalDocumentStorageService>();

// Register Application Services
builder.Services.AddScoped<IWordQuoteGeneratorService, ZinStoneWordQuoteGeneratorService>();
builder.Services.AddScoped<IPdfConverterService, PdfConverterService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ISecurityService, SecurityService>();
builder.Services.AddScoped<IQuoteSequenceService, QuoteSequenceService>();
builder.Services.AddScoped<ITelegramDeliveryService, TelegramDeliveryService>();

// Register Background Queue
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>(sp => new BackgroundTaskQueue(100));
builder.Services.AddHostedService<QueuedHostedService>();
if (builder.Configuration.GetValue<bool>("Telegram:Enabled") &&
    builder.Configuration.GetValue<bool>("Telegram:OutboxWorkerEnabled"))
    builder.Services.AddHostedService<TelegramOutboxBackgroundService>();

// Configure CORS for Vercel Frontend and MonsterASP.NET
builder.Services.AddCors(options =>
{
    options.AddPolicy("ZinStoneCorsPolicy", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (origins.Length > 0) policy.WithOrigins(origins);
        policy        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

// Ensure Database is created and Seed initial data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        logger.LogInformation("Ensuring SQL Server database is created...");
        if (app.Environment.IsProduction())
            await ProductionIsolation.VerifyConnectedDatabaseAsync(db);
        else
            db.Database.EnsureCreated();

        // Additive schema upgrades for metadata
        if (!app.Environment.IsProduction() && db.Database.IsSqlServer())
        {
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Quotes') AND name = 'PdfSha256')
                    BEGIN
                        ALTER TABLE Quotes ADD PdfSha256 NVARCHAR(64) NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Quotes') AND name = 'PdfSizeBytes')
                    BEGIN
                        ALTER TABLE Quotes ADD PdfSizeBytes BIGINT NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Quotes') AND name = 'StorageFileName')
                    BEGIN
                        ALTER TABLE Quotes ADD StorageFileName NVARCHAR(260) NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Quotes') AND name = 'DocxStorageFileName')
                    BEGIN
                        ALTER TABLE Quotes ADD DocxStorageFileName NVARCHAR(260) NULL;
                    END

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('QuoteTelegramDeliveries') AND name = 'ClaimToken')
                    BEGIN
                        ALTER TABLE QuoteTelegramDeliveries ADD ClaimToken NVARCHAR(100) NULL;
                        ALTER TABLE QuoteTelegramDeliveries ADD ClaimedAt DATETIME2 NULL;
                        ALTER TABLE QuoteTelegramDeliveries ADD ClaimExpiresAt DATETIME2 NULL;
                        ALTER TABLE QuoteTelegramDeliveries ADD NextRetryAt DATETIME2 NULL;
                        ALTER TABLE QuoteTelegramDeliveries ADD LastAttemptAt DATETIME2 NULL;
                        ALTER TABLE QuoteTelegramDeliveries ADD FileSha256 NVARCHAR(64) NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('QuoteTelegramDeliveries') AND name = 'IX_QuoteTelegramDeliveries_Status_NextRetryAt')
                    BEGIN
                        CREATE NONCLUSTERED INDEX IX_QuoteTelegramDeliveries_Status_NextRetryAt 
                        ON QuoteTelegramDeliveries (Status, NextRetryAt);
                    END
                ");
                logger.LogInformation("SQL Server additive schema verified.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not run additive schema upgrade SQL; may already exist.");
            }
        }

        // Migrate legacy public files outside wwwroot (F03)
        try
        {
            var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
            var legacyDir = Path.Combine(webRoot, "quotes");
            var privateDir = Path.Combine(app.Environment.ContentRootPath, "App_Data", "quotes");
            Directory.CreateDirectory(privateDir);

            if (!app.Environment.IsProduction() && Directory.Exists(legacyDir))
            {
                foreach (var file in Directory.GetFiles(legacyDir))
                {
                    var fileName = Path.GetFileName(file);
                    var destPath = Path.Combine(privateDir, fileName);
                    if (!File.Exists(destPath))
                    {
                        File.Copy(file, destPath, overwrite: false);
                        logger.LogInformation("Migrated historical quote document {FileName} to private storage.", fileName);
                    }
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error while migrating legacy public files.");
        }

        // Initial users are provisioned exclusively by the Owner; no hardcoded bootstrap accounts
        if (!db.Users.Any())
        {
            logger.LogInformation("No initial users present. Users must be provisioned by the Owner.");
        }

        // Initialize and Seed System Credentials (quote, admin, owner)
        var securityService = services.GetRequiredService<ISecurityService>();
        await securityService.EnsureInitializedAsync();

        // Initialize Quote Sequence and Index
        var sequenceService = services.GetRequiredService<IQuoteSequenceService>();
        await sequenceService.EnsureInitializedAsync();

        // Initialize Telegram Delivery Table
        var telegramService = services.GetRequiredService<ITelegramDeliveryService>();
        await telegramService.EnsureInitializedAsync();
    }
    catch (Exception)
    {
        logger.LogError("Database initialization failed.");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Private document routes must bypass static file serving and reach authorization.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/quotes"), branch =>
{
    branch.UseDefaultFiles();
    branch.UseStaticFiles();
});
app.UseRouting();
app.UseRateLimiter();
app.UseCors("ZinStoneCorsPolicy");
app.MapControllers();

app.MapGet("/api/health/live", () => Results.Ok(new { process = "available" }));
app.MapGet("/api/health", ReadinessHealth.CheckAsync);
app.Run();
