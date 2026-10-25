using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FactoryQuoteApi.Data;
using FactoryQuoteApi.Models;
using FactoryQuoteApi.Services;

// Use pure managed networking for SQL Client if Windows SQL Server is used
AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);

var builder = WebApplication.CreateBuilder(args);

// Configure dynamic listening port for Render (PORT env) or local default
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// Configure Database (PostgreSQL / Supabase, SQLite for testing, or SQL Server fallback)
var rawConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? string.Empty;

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var conn = rawConnection.Trim();
    if (conn.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        conn.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
        (conn.Contains("Host=", StringComparison.OrdinalIgnoreCase) && conn.Contains("Port=", StringComparison.OrdinalIgnoreCase)))
    {
        var npgsqlString = ParsePostgresConnectionString(conn);
        options.UseNpgsql(npgsqlString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
        });
    }
    else
    {
        options.UseSqlServer(conn);
    }
});

// Register HTTP Client Factory
builder.Services.AddHttpClient();

// Register Storage and WhatsApp Services
builder.Services.AddScoped<IDocumentStorageService, SupabaseDocumentStorageService>();
builder.Services.AddScoped<IWhatsAppDeliveryService, WhatsAppDeliveryService>();

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
builder.Services.AddHostedService<TelegramOutboxBackgroundService>();

// Configure CORS for Netlify, Render, and Vercel Frontends
builder.Services.AddCors(options =>
{
    options.AddPolicy("ZinStoneCorsPolicy", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            try
            {
                var uri = new Uri(origin);
                return uri.Host.EndsWith("netlify.app", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.EndsWith("onrender.com", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.EndsWith("vercel.app", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.EndsWith("runasp.net", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

builder.Services.AddOpenApi();

var app = builder.Build();

// Configure Forwarded Headers for reverse-proxy HTTPS behind Render / Netlify
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Ensure Database is created and schema initialized
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        logger.LogInformation("Ensuring database is created and schema up-to-date...");
        db.Database.EnsureCreated();

        if (db.Database.IsNpgsql())
        {
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE ""Quotes"" ADD COLUMN IF NOT EXISTS ""PdfSha256"" VARCHAR(64) NULL;
                    ALTER TABLE ""Quotes"" ADD COLUMN IF NOT EXISTS ""PdfSizeBytes"" BIGINT NULL;
                    ALTER TABLE ""Quotes"" ADD COLUMN IF NOT EXISTS ""StorageFileName"" VARCHAR(260) NULL;
                    ALTER TABLE ""Quotes"" ADD COLUMN IF NOT EXISTS ""DocxStorageFileName"" VARCHAR(260) NULL;

                    ALTER TABLE ""QuoteTelegramDeliveries"" ADD COLUMN IF NOT EXISTS ""ClaimToken"" VARCHAR(100) NULL;
                    ALTER TABLE ""QuoteTelegramDeliveries"" ADD COLUMN IF NOT EXISTS ""ClaimedAt"" TIMESTAMP WITH TIME ZONE NULL;
                    ALTER TABLE ""QuoteTelegramDeliveries"" ADD COLUMN IF NOT EXISTS ""ClaimExpiresAt"" TIMESTAMP WITH TIME ZONE NULL;
                    ALTER TABLE ""QuoteTelegramDeliveries"" ADD COLUMN IF NOT EXISTS ""NextRetryAt"" TIMESTAMP WITH TIME ZONE NULL;
                    ALTER TABLE ""QuoteTelegramDeliveries"" ADD COLUMN IF NOT EXISTS ""LastAttemptAt"" TIMESTAMP WITH TIME ZONE NULL;
                    ALTER TABLE ""QuoteTelegramDeliveries"" ADD COLUMN IF NOT EXISTS ""FileSha256"" VARCHAR(64) NULL;

                    CREATE INDEX IF NOT EXISTS ""IX_QuoteTelegramDeliveries_Status_NextRetryAt"" 
                    ON ""QuoteTelegramDeliveries"" (""Status"", ""NextRetryAt"");
                ");
                logger.LogInformation("PostgreSQL additive schema verified.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not run additive schema upgrade SQL on PostgreSQL.");
            }
        }
        else if (db.Database.IsSqlServer())
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

            if (Directory.Exists(legacyDir))
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

        // No other-company products or client data are seeded in ZIN STONE.

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
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while initializing or seeding the database.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("ZinStoneCorsPolicy");
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("ZinStoneCorsPolicy");
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ZIN STONE Quote API Running",
    environment = app.Environment.EnvironmentName,
    storage = "Supabase Storage Ready",
    database = "Supabase PostgreSQL Ready",
    timestamp = DateTime.UtcNow
}));

app.Run();

// Helper method to parse PostgreSQL URI (postgres://user:pass@host:port/dbname)
static string ParsePostgresConnectionString(string input)
{
    if (!input.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !input.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return input;
    }

    try
    {
        var uri = new Uri(input);
        var userInfo = uri.UserInfo.Split(':');
        var user = Uri.UnescapeDataString(userInfo[0]);
        var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(database)) database = "postgres";

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = port,
            Database = database,
            Username = user,
            Password = pass,
            SslMode = SslMode.Require,
            Pooling = true
        };

        return builder.ConnectionString;
    }
    catch
    {
        return input;
    }
}
