using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using System.ComponentModel.DataAnnotations;
using FactoryQuoteApi.DTOs;
using FactoryQuoteApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FactoryQuoteApi.SecurityTests;

public sealed class ZinStoneQuotePresentationTests
{
    [Theory]
    [InlineData("المهندسة")]
    [InlineData("الأستاذة")]
    [InlineData("الدكتورة")]
    public async Task Attention_title_split_across_runs_preserves_properties(string title)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var template = Path.Combine(root, "template.docx");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Templates", "ZIN_STONE_Quotation.docx"), template);
            string properties;
            string[] runProperties;
            using (var doc = WordprocessingDocument.Open(template, true))
            {
                var p = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().Single(p => p.InnerText.StartsWith("عناية"));
                var run = p.Elements<Run>().First();
                run.GetFirstChild<Text>()!.Text = "عناية الم";
                foreach (var text in new[] { "هن", "د", "س :" })
                {
                    var next = new Run((RunProperties)run.RunProperties!.CloneNode(true), new Text(text) { Space = SpaceProcessingModeValues.Preserve });
                    p.InsertAfter(next, run);
                    run = next;
                }
                properties = p.ParagraphProperties!.OuterXml;
                runProperties = p.Elements<Run>().Select(r => r.RunProperties!.OuterXml).ToArray();
                doc.MainDocumentPart.Document.Save();
            }
            var output = Path.Combine(root, "output.docx");
            await new ZinStoneWordQuoteGeneratorService(NullLogger<ZinStoneWordQuoteGeneratorService>.Instance)
                .GenerateQuoteDocumentAsync(template, output, new QuoteRequestDto {
                    ClientName = "اختبار", ContactTitle = title, ContactPerson = "سهيلة",
                    Items = [new() { ProductName = "بلوك", Quantity = 1, UnitPrice = 100 }]
                }, "SPLIT-TEST");
            using var result = WordprocessingDocument.Open(output, false);
            var attention = result.MainDocumentPart!.Document.Body!.Elements<Paragraph>().Single(p => p.InnerText.StartsWith("عناية"));
            Assert.StartsWith($"عناية {title} :", attention.InnerText);
            Assert.Contains("سهيلة", attention.InnerText);
            Assert.Equal(properties, attention.ParagraphProperties!.OuterXml);
            Assert.Equal(runProperties, attention.Elements<Run>().Take(runProperties.Length).Select(r => r.RunProperties!.OuterXml));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData(" المهندسة ", true)]
    [InlineData("مدير", false)]
    [InlineData("<script>", false)]
    public void Request_validates_contact_title(string? title, bool valid)
    {
        var request = new QuoteRequestDto { ClientName = "اختبار", ContactTitle = title };
        Assert.Equal(valid, Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

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
            var attention = Assert.Single(paragraphs, t => t.StartsWith("عناية", StringComparison.Ordinal));
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
