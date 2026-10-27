namespace FactoryQuoteApi.Services;

public sealed class LocalDocumentStorageService : IDocumentStorageService
{
    private readonly string _root;
    public LocalDocumentStorageService(IWebHostEnvironment environment)
    {
        _root = Path.Combine(environment.ContentRootPath, "App_Data", "quotes");
        Directory.CreateDirectory(_root);
    }
    public bool IsSupabaseConfigured => false;
    private string Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name.Contains('/') ||
            name.Contains('\\') || name.Contains(':') || name is "." or "..")
            throw new ArgumentException("Invalid document storage name.");
        return Path.Combine(_root, name);
    }
    public Task<string> SaveDocumentAsync(string source, string name, string type, CancellationToken cancellationToken = default)
    {
        var destination = Resolve(name);
        if (!string.Equals(Path.GetFullPath(source), destination, StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, overwrite: false);
        return Task.FromResult(name);
    }
    public Task<Stream?> GetDocumentStreamAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = Resolve(name);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }
    public Task<string?> GetSignedDownloadUrlAsync(string name, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    { Resolve(name); return Task.FromResult<string?>(null); }
    public Task<bool> DeleteDocumentAsync(string name, CancellationToken cancellationToken = default)
    { Resolve(name); return Task.FromResult(false); }
}
