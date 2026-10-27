using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FactoryQuoteApi.DTOs;
using FactoryQuoteApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FactoryQuoteApi.SecurityTests;

public sealed class ZinStoneQuotePresentationTests
{
    [Theory]
    [InlineData("المهندس")]
    [InlineData("المهندسة")]
    [InlineData("الأستاذ")]
    [InlineData("الأستاذة")]
    [InlineData("السيد")]
    [InlineData("السيدة")]
    [InlineData("الدكتور")]
    [InlineData("الدكتورة")]
    public async Task Generated_docx_uses_exact_selected_attention_title(string selectedTitle)
    {
        var template = Path.Combine(AppContext.BaseDirectory, "Templates", "ZIN_STONE_Quotation.docx");
        Assert.True(File.Exists(template), "Official ZIN STONE template must be copied to the test output.");
        var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            var service = new ZinStoneWordQuoteGeneratorService(NullLogger<ZinStoneWordQuoteGeneratorService>.Instance);
            var request = new QuoteRequestDto
            {
                ClientName = "شركة اختبار",
                ContactTitle = selectedTitle,
                ContactPerson = "سارة شريف",
                Items = new List<ProductItemDto>
                {
                    new() { ProductName = "بلوك", Size = "20x20x40", Capacity = "اختبار", Quantity = 1, UnitPrice = 100 }
                }
            };
            await service.GenerateQuoteDocumentAsync(template, output, request, "TEST-1");
            using var doc = WordprocessingDocument.Open(output, false);
            var paragraphs = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>()
                .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text)))
                .ToList();
            var attention = Assert.Single(paragraphs.Where(t => t.StartsWith("عناية", StringComparison.Ordinal)));
            Assert.Contains("عناية " + selectedTitle, attention);
            Assert.Contains("سارة شريف", attention);
            Assert.StartsWith("عناية " + selectedTitle + " :", attention);
            if (selectedTitle != "المهندس") Assert.DoesNotContain("عناية المهندس :", attention);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Theory]
    [InlineData("شركة الأفق", "مشروع الساحل", "عرض سعر شركة الأفق مشروع الساحل.pdf")]
    [InlineData("الأفق", "الساحل", "عرض سعر شركة الأفق مشروع الساحل.pdf")]
    [InlineData("شركة الأفق", null, "عرض سعر شركة الأفق.pdf")]
    [InlineData("شركة النور", "", "عرض سعر شركة النور.pdf")]
    [InlineData("شركة النور/الغد", "مدينة النور: المرحلة الأولى", "عرض سعر شركة النور الغد مشروع مدينة النور المرحلة الأولى.pdf")]
    public void Friendly_pdf_name_includes_company_and_optional_project(string client, string? project, string expected)
        => Assert.Equal(expected, QuoteDisplayFileName.BuildPdfName(client, project));
}
