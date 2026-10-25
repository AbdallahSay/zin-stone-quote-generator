using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Spire.Doc;

namespace FactoryQuoteApi.Services;

public interface IPdfConverterService
{
    Task<string> ConvertDocxToPdfAsync(string docxPath, string pdfPath, CancellationToken cancellationToken = default);
}

public class PdfConverterService : IPdfConverterService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PdfConverterService> _logger;

    public PdfConverterService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PdfConverterService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> ConvertDocxToPdfAsync(string docxPath, string pdfPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(docxPath))
        {
            throw new FileNotFoundException($"DOCX file not found at {docxPath}");
        }

        var pdfDir = Path.GetDirectoryName(pdfPath);
        if (!string.IsNullOrEmpty(pdfDir) && !Directory.Exists(pdfDir))
        {
            Directory.CreateDirectory(pdfDir);
        }

        // 1. Primary: Use FreeSpire.Doc for local in-process conversion
        try
        {
            _logger.LogInformation("Attempting PDF conversion via FreeSpire.Doc: {DocxPath} -> {PdfPath}", docxPath, pdfPath);
            await Task.Run(() =>
            {
                var document = new Document();
                document.LoadFromFile(docxPath);
                document.SaveToFile(pdfPath, FileFormat.PDF);
                document.Close();
                document.Dispose();
            }, cancellationToken);

            if (File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0)
            {
                _logger.LogInformation("FreeSpire.Doc conversion succeeded: {PdfPath} (Size: {Size} bytes)", pdfPath, new FileInfo(pdfPath).Length);
                return pdfPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FreeSpire.Doc conversion failed. Attempting alternative converters.");
        }

        // 2. Linux Container Native: LibreOffice CLI (soffice)
        try
        {
            var libreOfficeBinary = FindLibreOfficeBinary();
            if (!string.IsNullOrEmpty(libreOfficeBinary))
            {
                _logger.LogInformation("Attempting PDF conversion via LibreOffice: {Binary}", libreOfficeBinary);
                var targetDir = Path.GetDirectoryName(Path.GetFullPath(pdfPath))!;
                var psi = new ProcessStartInfo
                {
                    FileName = libreOfficeBinary,
                    Arguments = $"--headless --convert-to pdf --outdir \"{targetDir}\" \"{docxPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                    var expectedOutput = Path.Combine(targetDir, Path.GetFileNameWithoutExtension(docxPath) + ".pdf");
                    if (File.Exists(expectedOutput) && new FileInfo(expectedOutput).Length > 0)
                    {
                        if (!string.Equals(Path.GetFullPath(expectedOutput), Path.GetFullPath(pdfPath), StringComparison.OrdinalIgnoreCase))
                        {
                            File.Move(expectedOutput, pdfPath, overwrite: true);
                        }
                        _logger.LogInformation("LibreOffice PDF conversion succeeded: {PdfPath}", pdfPath);
                        return pdfPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LibreOffice conversion failed.");
        }

        // 3. Secondary: PDFShift API if configured
        var apiKey = _configuration["PdfShift:ApiKey"] ?? Environment.GetEnvironmentVariable("PDFSHIFT_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.Contains("<YOUR_"))
        {
            try
            {
                _logger.LogInformation("Attempting PDF conversion via PDFShift API...");
                var success = await ConvertViaPdfShiftAsync(apiKey, docxPath, pdfPath, cancellationToken);
                if (success)
                {
                    _logger.LogInformation("PDFShift conversion succeeded: {PdfPath}", pdfPath);
                    return pdfPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PDFShift API conversion failed.");
            }
        }

        // 4. Fallback: Python docx_to_pdf.py if available
        var pythonScript = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "docx_to_pdf.py"));
        var pythonVenv = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".venv", "Scripts", "python.exe"));

        if (!File.Exists(pythonVenv))
        {
            pythonVenv = "python";
        }

        if (File.Exists(pythonScript))
        {
            try
            {
                _logger.LogInformation("Attempting PDF conversion via local Python script: {Script}", pythonScript);
                var psi = new ProcessStartInfo
                {
                    FileName = pythonVenv,
                    Arguments = $"\"{pythonScript}\" \"{docxPath}\" \"{pdfPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                    if (process.ExitCode == 0 && File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0)
                    {
                        _logger.LogInformation("Local Python PDF conversion succeeded: {PdfPath}", pdfPath);
                        return pdfPath;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local Python conversion failed.");
            }
        }

        if (File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0)
        {
            return pdfPath;
        }

        throw new InvalidOperationException($"All PDF conversion strategies failed for '{docxPath}'.");
    }

    private static string? FindLibreOfficeBinary()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
            }
            : new[]
            {
                "/usr/bin/soffice",
                "/usr/bin/libreoffice",
                "/usr/local/bin/soffice",
                "/usr/local/bin/libreoffice"
            };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return OperatingSystem.IsWindows() ? null : "soffice";
    }

    private async Task<bool> ConvertViaPdfShiftAsync(string apiKey, string docxPath, string pdfPath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        var authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{apiKey}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(docxPath, cancellationToken);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        form.Add(fileContent, "source", Path.GetFileName(docxPath));

        var response = await client.PostAsync("https://api.pdfshift.io/v3/convert/pdf", form, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var pdfBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(pdfPath, pdfBytes, cancellationToken);
            return true;
        }

        _logger.LogWarning("PDFShift returned status code {StatusCode}: {Reason}", response.StatusCode, response.ReasonPhrase);
        return false;
    }
}
