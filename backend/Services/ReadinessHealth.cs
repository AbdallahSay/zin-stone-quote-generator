using FactoryQuoteApi.Data;
using Microsoft.EntityFrameworkCore;

namespace FactoryQuoteApi.Services;

public static class ReadinessHealth
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _checkedAt;
    private static bool _converterReady;

    public static async Task<IResult> CheckAsync(ApplicationDbContext db, IWebHostEnvironment env,
        IPdfConverterService converter, CancellationToken cancellationToken)
    {
        bool database;
        try { database = await db.Database.CanConnectAsync(cancellationToken); }
        catch { database = false; }
        var storage = false;
        var directory = Path.Combine(env.ContentRootPath, "App_Data", "quotes");
        var probe = Path.Combine(directory, ".readiness-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(probe, "readiness", cancellationToken);
            storage = await File.ReadAllTextAsync(probe, cancellationToken) == "readiness";
        }
        catch { }
        finally { if (File.Exists(probe)) File.Delete(probe); }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow - _checkedAt > TimeSpan.FromMinutes(1))
            {
                var output = probe + ".pdf";
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    await converter.ConvertDocxToPdfAsync(Path.Combine(env.ContentRootPath, "Templates", "ZIN_STONE_Quotation.docx"), output, timeout.Token);
                    var bytes = await File.ReadAllBytesAsync(output, timeout.Token);
                    _converterReady = bytes.Length > 5 && System.Text.Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";
                }
                catch { _converterReady = false; }
                finally { if (File.Exists(output)) File.Delete(output); }
                _checkedAt = DateTime.UtcNow;
            }
        }
        finally { Gate.Release(); }
        var ready = database && storage && _converterReady;
        return Results.Json(new { status = ready ? "ready" : "not_ready", process = true,
            database, documentStorage = storage, pdfConverter = _converterReady,
            version = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion,
            timestamp = DateTime.UtcNow }, statusCode: ready ? 200 : 503);
    }
}
