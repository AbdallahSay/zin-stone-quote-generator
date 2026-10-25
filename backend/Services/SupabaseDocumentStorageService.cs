using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactoryQuoteApi.Services;

public interface IDocumentStorageService
{
    bool IsSupabaseConfigured { get; }
    Task<string> SaveDocumentAsync(string localTempPath, string storageFileName, string contentType, CancellationToken cancellationToken = default);
    Task<Stream?> GetDocumentStreamAsync(string storageFileName, CancellationToken cancellationToken = default);
    Task<string?> GetSignedDownloadUrlAsync(string storageFileName, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(string storageFileName, CancellationToken cancellationToken = default);
}

public class SupabaseDocumentStorageService : IDocumentStorageService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SupabaseDocumentStorageService> _logger;

    private readonly string? _supabaseUrl;
    private readonly string? _serviceKey;
    private readonly string _bucket;
    private readonly string _localQuotesDir;

    public SupabaseDocumentStorageService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<SupabaseDocumentStorageService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _env = env;
        _logger = logger;

        _supabaseUrl = (configuration["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL"))?.TrimEnd('/');
        _serviceKey = configuration["Supabase:ServiceKey"] ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_KEY");
        _bucket = configuration["Supabase:Bucket"] ?? Environment.GetEnvironmentVariable("SUPABASE_BUCKET") ?? "quotes-private";

        var contentRoot = env.ContentRootPath ?? Directory.GetCurrentDirectory();
        _localQuotesDir = Path.Combine(contentRoot, "App_Data", "quotes");
        Directory.CreateDirectory(_localQuotesDir);
    }

    public bool IsSupabaseConfigured =>
        !string.IsNullOrWhiteSpace(_supabaseUrl) &&
        !string.IsNullOrWhiteSpace(_serviceKey) &&
        !_supabaseUrl.Contains("<YOUR_");

    public async Task<string> SaveDocumentAsync(
        string localTempPath,
        string storageFileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageFileName);

        // Always ensure a secure local cache/fallback copy exists in App_Data/quotes
        var localDestination = Path.Combine(_localQuotesDir, storageFileName);
        if (!string.Equals(Path.GetFullPath(localTempPath), Path.GetFullPath(localDestination), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(localTempPath, localDestination, overwrite: true);
        }

        if (!IsSupabaseConfigured)
        {
            _logger.LogInformation("Supabase Storage not configured. Storing {FileName} in local secure storage.", storageFileName);
            return storageFileName;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);
            client.DefaultRequestHeaders.Add("apikey", _serviceKey);

            var uploadUrl = $"{_supabaseUrl}/storage/v1/object/{_bucket}/{Uri.EscapeDataString(storageFileName)}";
            
            await using var fileStream = File.OpenRead(localTempPath);
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            // Use POST or PUT with upsert header
            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = streamContent };
            request.Headers.Add("x-upsert", "true");

            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // If POST failed, try PUT
                await using var retryStream = File.OpenRead(localTempPath);
                using var putContent = new StreamContent(retryStream);
                putContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                using var putRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = putContent };
                
                var putResponse = await client.SendAsync(putRequest, cancellationToken);
                if (!putResponse.IsSuccessStatusCode)
                {
                    var err = await putResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Supabase upload returned {Status}: {Err}. Fallback to local storage maintained.", putResponse.StatusCode, err);
                    return storageFileName;
                }
            }

            _logger.LogInformation("Successfully uploaded {FileName} to Supabase private bucket {Bucket}.", storageFileName, _bucket);
            return storageFileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception uploading {FileName} to Supabase Storage. File is safe in local storage.", storageFileName);
            return storageFileName;
        }
    }

    public async Task<Stream?> GetDocumentStreamAsync(string storageFileName, CancellationToken cancellationToken = default)
    {
        // 1. Check local storage first (instant access and caching)
        var localPath = Path.Combine(_localQuotesDir, storageFileName);
        if (File.Exists(localPath))
        {
            return File.OpenRead(localPath);
        }

        // 2. Fetch from Supabase Storage if configured
        if (IsSupabaseConfigured)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);
                client.DefaultRequestHeaders.Add("apikey", _serviceKey);

                var downloadUrl = $"{_supabaseUrl}/storage/v1/object/authenticated/{_bucket}/{Uri.EscapeDataString(storageFileName)}";
                var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var memoryStream = new MemoryStream();
                    await response.Content.CopyToAsync(memoryStream, cancellationToken);
                    memoryStream.Position = 0;

                    // Cache locally for future requests
                    try
                    {
                        await using var localFile = File.Create(localPath);
                        await memoryStream.CopyToAsync(localFile, cancellationToken);
                        memoryStream.Position = 0;
                    }
                    catch
                    {
                        // Non-critical if local cache write fails
                    }

                    return memoryStream;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download {FileName} from Supabase Storage.", storageFileName);
            }
        }

        return null;
    }

    public async Task<string?> GetSignedDownloadUrlAsync(string storageFileName, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured)
        {
            return null; // Controller will stream through authenticated API
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);
            client.DefaultRequestHeaders.Add("apikey", _serviceKey);

            var seconds = (int)(expiry?.TotalSeconds ?? 3600);
            var signUrl = $"{_supabaseUrl}/storage/v1/object/sign/{_bucket}/{Uri.EscapeDataString(storageFileName)}";

            var payload = JsonSerializer.Serialize(new { expiresIn = seconds });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(signUrl, content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("signedURL", out var signedProp))
                {
                    var relativeOrFull = signedProp.GetString();
                    if (!string.IsNullOrEmpty(relativeOrFull))
                    {
                        if (relativeOrFull.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            return relativeOrFull;
                        return $"{_supabaseUrl}/storage/v1{relativeOrFull}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating signed URL for {FileName} on Supabase.", storageFileName);
        }

        return null;
    }

    public async Task<bool> DeleteDocumentAsync(string storageFileName, CancellationToken cancellationToken = default)
    {
        var localPath = Path.Combine(_localQuotesDir, storageFileName);
        if (File.Exists(localPath))
        {
            try { File.Delete(localPath); } catch { }
        }

        if (IsSupabaseConfigured)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);
                client.DefaultRequestHeaders.Add("apikey", _serviceKey);

                var deleteUrl = $"{_supabaseUrl}/storage/v1/object/{_bucket}";
                var payload = JsonSerializer.Serialize(new { prefixes = new[] { storageFileName } });
                using var request = new HttpRequestMessage(HttpMethod.Delete, deleteUrl)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                var response = await client.SendAsync(request, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting {FileName} from Supabase.", storageFileName);
                return false;
            }
        }

        return true;
    }
}
