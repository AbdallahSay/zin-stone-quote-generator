using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FactoryQuoteApi.DTOs;
using FactoryQuoteApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Integration test: official template -> real registered generator -> application's
// actual PDF converter. No SQL, production credentials or notification transport.
var root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? ".");
var output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "document-test-artifacts");
Directory.CreateDirectory(output);
var template = Path.Combine(root, "backend/Templates/ZIN_STONE_Quotation.docx");
var templateHash = SHA256.HashData(File.ReadAllBytes(template));
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole());
services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
services.AddHttpClient();
services.AddScoped<IWordQuoteGeneratorService, ZinStoneWordQuoteGeneratorService>();
services.AddScoped<IPdfConverterService, PdfConverterService>();
using var provider = services.BuildServiceProvider();
var word = provider.GetRequiredService<IWordQuoteGeneratorService>();
var pdf = provider.GetRequiredService<IPdfConverterService>();
var titles = new[] { "المهندس", "المهندسة", "الأستاذ", "الأستاذة", "السيد", "السيدة", "الدكتور", "الدكتورة" };
var cases = titles.Select(t => (Input: (string?)t, Title: t, Name: "سهيلة", Case: "title-" + Array.IndexOf(titles, t)))
    .Concat(new (string? Input, string Title, string Name, string Case)[] {
        ((string?)null, "المهندس", "أحمد", "missing-title"),
        ((string?)"  ", "المهندس", "سارة", "blank-title"),
        ((string?)"السيدة", "السيدة", "", "empty-person"),
        ((string?)"الدكتورة", "الدكتورة", "سهيلة أحمد محمد عبد الرحمن زين الدين حسن عبد الله", "long-name")
    });
var evidence = new List<object>();
foreach (var item in cases)
{
    var request = new QuoteRequestDto {
        ClientName = "شركة اختبار", ProjectName = "مشروع الاختبار", ContactTitle = item.Input,
        ContactPerson = item.Name, QuoteDate = new DateTime(2026, 10, 28),
        Items = [new() { ProductName = "بلوك اختبار", Size = "40 × 20 × 20", Capacity = "1000", Quantity = 2, UnitPrice = 100 }]
    };
    var docx = Path.Combine(output, item.Case + ".docx");
    var pdfPath = Path.Combine(output, item.Case + ".pdf");
    await word.GenerateQuoteDocumentAsync(template, docx, request, "TEST-ATTENTION");
    using (var doc = WordprocessingDocument.Open(docx, false))
    {
        var attention = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().Single(p => p.InnerText.StartsWith("عناية"));
        Require(attention.InnerText.StartsWith($"عناية {item.Title} :"), "DOCX title " + item.Case);
        Require(attention.InnerText.Contains(item.Name), "DOCX name " + item.Case);
        Require(item.Title == "المهندس" || !attention.InnerText.Contains("عناية المهندس :"), "Stale default " + item.Case);
        using var source = WordprocessingDocument.Open(template, false);
        var original = source.MainDocumentPart!.Document.Body!.Elements<Paragraph>().Single(p => p.InnerText.StartsWith("عناية"));
        Require(original.ParagraphProperties!.OuterXml == attention.ParagraphProperties!.OuterXml, "Paragraph formatting");
        Require(original.Elements<Run>().First().RunProperties!.OuterXml == attention.Elements<Run>().First().RunProperties!.OuterXml, "Label formatting");
    }
    using (var original = ZipFile.OpenRead(template))
    using (var generated = ZipFile.OpenRead(docx))
        foreach (var entry in original.Entries.Where(e => e.FullName != "word/document.xml"))
        {
            using var a = entry.Open(); using var b = generated.GetEntry(entry.FullName)!.Open();
            Require(SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)), "Untouched template part " + entry.FullName);
        }
    await pdf.ConvertDocxToPdfAsync(docx, pdfPath);
    Require(File.ReadAllBytes(pdfPath).Take(5).SequenceEqual("%PDF-"u8.ToArray()), "Real PDF signature");
    Require(QuoteDisplayFileName.BuildPdfName(request.ClientName, request.ProjectName) == "عرض سعر شركة اختبار مشروع الاختبار.pdf", "Filename with project");
    Require(QuoteDisplayFileName.BuildPdfName(request.ClientName, null) == "عرض سعر شركة اختبار.pdf", "Filename without project");
    evidence.Add(new { id = item.Case, title = item.Title, name = item.Name, docx, pdf = pdfPath });
}
try
{
    await word.GenerateQuoteDocumentAsync(template, Path.Combine(output, "invalid.docx"), new QuoteRequestDto { ContactTitle = "غير صالح" }, "INVALID");
    throw new Exception("Invalid title was accepted");
}
catch (ArgumentException) { }
Require(templateHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(template))), "Original template unchanged");
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS: {evidence.Count} real DOCX/PDF cases, original template properties and package parts, invalid title and filenames.");
static void Require(bool success, string reason) { if (!success) throw new Exception(reason); }
