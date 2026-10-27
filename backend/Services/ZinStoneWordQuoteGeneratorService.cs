using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FactoryQuoteApi.DTOs;

namespace FactoryQuoteApi.Services;

/// <summary>
/// ZIN STONE-specific document adapter. It fills the client's original .docx in place,
/// without rebuilding the cover, footer, background illustration, or product heading row.
/// Register instead of WordQuoteGeneratorService in the isolated ZIN STONE application.
/// </summary>
public sealed class ZinStoneWordQuoteGeneratorService : IWordQuoteGeneratorService
{
    private readonly ILogger<ZinStoneWordQuoteGeneratorService> _logger;

    public ZinStoneWordQuoteGeneratorService(ILogger<ZinStoneWordQuoteGeneratorService> logger)
    {
        _logger = logger;
    }

    public Task<string> GenerateQuoteDocumentAsync(
        string templatePath,
        string outputPath,
        QuoteRequestDto request,
        string quoteNumber,
        decimal totalAmount = 0)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("ZIN STONE quotation template is missing.", templatePath);
        }

        var targetDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (targetDir is not null) Directory.CreateDirectory(targetDir);
        File.Copy(templatePath, outputPath, overwrite: true);

        using var wordDoc = WordprocessingDocument.Open(outputPath, true);
        var body = wordDoc.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException("Invalid ZIN STONE DOCX template.");

        var quoteDate = request.QuoteDate ?? DateTime.Now;
        var day = GetArabicDay(quoteDate.DayOfWeek);
        // Keep the template labels, typography and background unchanged.
        PopulateField(body, "اليوم", day);
        PopulateField(body, "التاريخ", quoteDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        PopulateField(body, "السادة شركة", request.ClientName ?? string.Empty);
        PopulateField(body, "عناية المهندس", request.ContactPerson ?? string.Empty);
        PopulateProductRows(body, request);
        PopulateTerms(body, request.Terms);
        PopulateIssuer(body, "يوسف زين الدين", "المدير المسؤل", null);

        // Quote number remains in SQL/audit/PDF filename, because the supplied layout
        // has no visual quote-number field; don't add elements the client didn't request.
        wordDoc.MainDocumentPart!.Document.Save();
        _logger.LogInformation("ZIN STONE quote {QuoteNumber} generated: {Path}", quoteNumber, outputPath);
        return Task.FromResult(outputPath);
    }

    public List<string> GetDefaultTerms(string templatePath)
    {
        if (!File.Exists(templatePath)) return new List<string>();
        using var wordDoc = WordprocessingDocument.Open(templatePath, false);
        var body = wordDoc.MainDocumentPart?.Document?.Body;
        return body is null ? new List<string>() : FindTerms(body).Terms.Select(t => ReadText(t)).ToList();
    }

    public string SanitizeFileName(string? clientName, string? projectName, string fallback = "Quote")
    {
        var joined = string.Join(" ", new[] { clientName, projectName }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        if (joined.Length == 0) return fallback;

        var forbidden = new HashSet<char>(Path.GetInvalidFileNameChars());
        foreach (var ch in "\\/:*?\"<>|") forbidden.Add(ch);
        var cleaned = new string(joined.Where(c => !forbidden.Contains(c) && !char.IsControl(c)).ToArray());
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Length == 0 ? fallback : cleaned;
    }

    private static void PopulateField(Body body, string label, string value)
    {
        var normalizedLabel = Normalize(label);
        var para = body.Elements<Paragraph>()
            .FirstOrDefault(p => Normalize(ReadText(p)).StartsWith(normalizedLabel, StringComparison.Ordinal));
        if (para is null) throw new InvalidOperationException($"Required ZIN STONE field missing: {label}");

        // The source document contains labels but no placeholders; append only the value.
        // The appended run inherits the typography from the label rather than changing it.
        var refRun = para.Elements<Run>().LastOrDefault(r => !string.IsNullOrWhiteSpace(ReadText(r)));
        var rPr = refRun?.RunProperties?.CloneNode(true) as RunProperties ?? new RunProperties();
        var inputLength = value.Trim().Length;
        if (inputLength > 45)
        {
            rPr.FontSize = new FontSize { Val = "20" }; // 10pt on long company names
            rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = "20" };
        }
        else if (inputLength > 30)
        {
            rPr.FontSize = new FontSize { Val = "24" }; // 12pt otherwise
            rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = "24" };
        }
        rPr.RightToLeftText = new RightToLeftText { Val = true };
        var run = new Run();
        run.AppendChild(rPr);
        run.AppendChild(new Text("  " + value.Trim()) { Space = SpaceProcessingModeValues.Preserve });
        para.AppendChild(run);
    }

    // Keep the four approved columns, but give longer product descriptions more room.
    // Indices follow the SOURCE DOCX XML order (visually RTL): type, size, load, price.
    private static readonly int[] ProductColumnWidthsDxa = { 2760, 2260, 1570, 1932 };
    private static readonly string[] ProductHeaderLabels = { "النوع", "المقاس", "الحمولة", "سعر الألف" };

    private static void PopulateProductRows(Body body, QuoteRequestDto request)
    {
        var table = body.Descendants<Table>().FirstOrDefault(t => t.Elements<TableRow>().Any());
        if (table is null) throw new InvalidOperationException("ZIN STONE product table not found.");
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count < 2 || rows[0].Elements<TableCell>().Count() != 4)
            throw new InvalidOperationException("Unexpected ZIN STONE product table layout.");

        // Fixed widths on both the column grid and individual cells prevent Word/PDF
        // from rearranging the price/load/size/type columns. Keep original table borders
        // and horizontal offset so the company's background/branding stays unchanged.
        var tblPr = table.GetFirstChild<TableProperties>() ?? table.PrependChild(new TableProperties());
        tblPr.TableLayout = new TableLayout { Type = TableLayoutValues.Fixed };
        tblPr.TableWidth = new TableWidth { Width = "8522", Type = TableWidthUnitValues.Dxa };
        var grid = table.GetFirstChild<TableGrid>();
        if (grid is null) throw new InvalidOperationException("ZIN STONE product grid not found.");
        var gridColumns = grid.Elements<GridColumn>().ToList();
        if (gridColumns.Count != 4) throw new InvalidOperationException("Expected four grid columns.");
        for (int i = 0; i < 4; i++) gridColumns[i].Width = ProductColumnWidthsDxa[i].ToString(CultureInfo.InvariantCulture);

        var headerCells = rows[0].Elements<TableCell>().ToList();
        for (int i = 0; i < 4; i++)
            FormatProductCell(headerCells[i], ProductHeaderLabels[i], i, isHeader: true);

        var templateRow = (TableRow)rows.Last().CloneNode(true);
        var items = GetItems(request);
        if (items.Count == 0) throw new InvalidOperationException("At least one quotation product is required.");
        foreach (var old in rows.Skip(1)) old.Remove();

        var after = rows[0];
        foreach (var values in items)
        {
            var row = (TableRow)templateRow.CloneNode(true);
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count != 4) throw new InvalidOperationException("Unexpected product row width.");
            for (int i = 0; i < 4; i++) FormatProductCell(cells[i], values[i], i, isHeader: false);
            table.InsertAfter(row, after);
            after = row;
        }
    }

    private static List<string[]> GetItems(QuoteRequestDto request)
    {
        if (request.Rows is { Count: > 0 })
        {
            return request.Rows.Select(r => new[]
            {
                GetValue(r, "النوع", "الصنف", "اسم المنتج", "Product", "Type"),
                GetValue(r, "المقاس", "Size"),
                GetValue(r, "الحمولة", "Capacity"),
                GetValue(r, "سعر الألف", "سعر الالف", "السعر", "Price", "UnitPrice")
            }).ToList();
        }
        return (request.Items ?? new List<ProductItemDto>())
            .Select(i => new[]
            {
                i.ProductName ?? string.Empty,
                i.Size ?? string.Empty,
                i.Capacity ?? string.Empty,
                i.UnitPrice > 0 ? i.UnitPrice.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty
            }).ToList();
    }

    private static string GetValue(Dictionary<string, string> row, params string[] possibleKeys)
    {
        foreach (var key in possibleKeys)
        {
            var found = row.Keys.FirstOrDefault(k => Normalize(k) == Normalize(key));
            if (found is not null) return row[found] ?? string.Empty;
        }
        // Price and product keys may be decorated by the UI, e.g. "سعر الألف (جنيه)".
        foreach (var key in possibleKeys)
        {
            var found = row.Keys.FirstOrDefault(k => Normalize(k).Contains(Normalize(key), StringComparison.Ordinal));
            if (found is not null) return row[found] ?? string.Empty;
        }
        return string.Empty;
    }

    private static void FormatProductCell(TableCell cell, string? value, int column, bool isHeader)
    {
        var cPr = cell.TableCellProperties ?? cell.PrependChild(new TableCellProperties());
        cPr.TableCellWidth = new TableCellWidth
        {
            Width = ProductColumnWidthsDxa[column].ToString(CultureInfo.InvariantCulture),
            Type = TableWidthUnitValues.Dxa
        };
        cPr.TableCellVerticalAlignment = new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center };

        var para = cell.Elements<Paragraph>().FirstOrDefault() ?? cell.AppendChild(new Paragraph());
        foreach (var extra in cell.Elements<Paragraph>().Where(p => p != para).ToList()) extra.Remove();
        foreach (var run in para.Elements<Run>().ToList()) run.Remove();
        var pPr = para.ParagraphProperties ?? para.PrependChild(new ParagraphProperties());
        pPr.RemoveAllChildren<Indentation>(); // Old template has large first-line indents that offset text.
        pPr.RemoveAllChildren<Tabs>();
        pPr.Justification = new Justification { Val = JustificationValues.Center };
        pPr.SpacingBetweenLines = new SpacingBetweenLines
        {
            Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto
        };

        var cleaned = Regex.Replace(value?.Trim() ?? string.Empty, @"\s+", " ");
        var arabic = cleaned.Any(c => c >= '\u0600' && c <= '\u06FF');
        // 13pt bold headings, 11pt bold cells. Decrease the DATA font for long values;
        // never hide or truncate the text: Word may wrap very long values inside the cell.
        var pointSize = isHeader ? 13.0 : cleaned.Length switch
        {
            > 34 => 8.5,
            > 25 => 9.0,
            > 18 => 10.0,
            _ => 11.0
        };
        var halfPoints = ((int)Math.Round(pointSize * 2)).ToString(CultureInfo.InvariantCulture);
        var rPr = new RunProperties
        {
            Bold = new Bold { Val = true },
            BoldComplexScript = new BoldComplexScript { Val = true },
            FontSize = new FontSize { Val = halfPoints },
            FontSizeComplexScript = new FontSizeComplexScript { Val = halfPoints },
            Color = new Color { Val = "000000" },
            RightToLeftText = new RightToLeftText { Val = arabic },
            Languages = new Languages { Bidi = "ar-EG" }
        };
        var text = !arabic && cleaned.Length > 0 ? "\u200E" + cleaned + "\u200E" : cleaned;
        para.AppendChild(new Run(rPr, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static (Paragraph? Header, List<Paragraph> Terms, Paragraph? Signature) FindTerms(Body body)
    {
        Paragraph? heading = null;
        Paragraph? signature = null;
        var terms = new List<Paragraph>();
        foreach (var para in body.Elements<Paragraph>())
        {
            var text = Normalize(ReadText(para));
            if (heading is null)
            {
                if (text.Contains("الشروط", StringComparison.Ordinal) && text.Contains("عرض السعر", StringComparison.Ordinal))
                    heading = para;
                continue;
            }
            if (text.Contains("المدير", StringComparison.Ordinal))
            {
                signature = para;
                break;
            }
            if (!string.IsNullOrWhiteSpace(text)) terms.Add(para);
        }
        return (heading, terms, signature);
    }

    private static void PopulateTerms(Body body, List<string>? replacementTerms)
    {
        if (replacementTerms is null || replacementTerms.Count == 0) return;
        var (heading, existing, signature) = FindTerms(body);
        if (heading is null || signature is null || existing.Count == 0)
            throw new InvalidOperationException("ZIN STONE terms boundaries are missing.");

        var prototype = (Paragraph)existing[0].CloneNode(true);
        foreach (var paragraph in existing) paragraph.Remove();
        foreach (var raw in replacementTerms.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            var cloned = (Paragraph)prototype.CloneNode(true);
            var run = cloned.Elements<Run>().FirstOrDefault() ?? cloned.AppendChild(new Run());
            foreach (var other in cloned.Elements<Run>().Where(r => r != run).ToList()) other.Remove();
            foreach (var t in run.Descendants<Text>().ToList()) t.Remove();
            run.AppendChild(new Text(raw.Trim()) { Space = SpaceProcessingModeValues.Preserve });
            body.InsertBefore(cloned, signature);
        }
    }

    private static void PopulateIssuer(Body body, string? name, string? jobTitle, string? prefix)
    {
        // The original name paragraph has 120+ literal spaces, splitting "يوسف زين الدين"
        // across the PAGE. Never retain those spaces, tatweel, or legacy runs.
        var titleP = body.Elements<Paragraph>().FirstOrDefault(p =>
            Normalize(ReadText(p)).Contains("المدير المس", StringComparison.Ordinal));
        var nameP = body.Elements<Paragraph>().FirstOrDefault(p =>
        {
            var text = Normalize(ReadText(p));
            return text.Contains("يوسف", StringComparison.Ordinal) && text.Contains("زين", StringComparison.Ordinal);
        });
        if (titleP is null || nameP is null)
            throw new InvalidOperationException("ZIN STONE signature paragraphs not found.");

        var title = !string.IsNullOrWhiteSpace(jobTitle)
            ? CleanSignatureText(jobTitle)
            : CleanSignatureText(ReadText(titleP));
        var baseName = !string.IsNullOrWhiteSpace(name)
            ? CleanSignatureText(name)
            : CleanSignatureText(ReadText(nameP));
        var cleanPrefix = CleanSignatureText(prefix);
        var issuer = !string.IsNullOrWhiteSpace(cleanPrefix) &&
                     !baseName.StartsWith(cleanPrefix, StringComparison.Ordinal)
            ? cleanPrefix + " " + baseName
            : baseName;
        SetLeftSignatureParagraph(titleP, title);
        SetLeftSignatureParagraph(nameP, issuer);
    }

    private static string CleanSignatureText(string? raw)
    {
        var normal = (raw ?? string.Empty).Normalize(NormalizationForm.FormKC)
            .Replace("ـ", string.Empty); // eliminate visually stretched/fragmented Arabic words
        return Regex.Replace(normal, @"\s+", " ").Trim();
    }

    private static void SetLeftSignatureParagraph(Paragraph para, string value)
    {
        var pPr = para.ParagraphProperties ?? para.PrependChild(new ParagraphProperties());
        pPr.RemoveAllChildren<Tabs>();
        pPr.Indentation = new Indentation { Left = "0", Right = "0", FirstLine = "0" };
        // IMPORTANT: This SOURCE has RTL paragraphs: jc=right yields the physical LEFT
        // of the page in LibreOffice/Word for these Arabic runs (verified in PDF render).
        pPr.Justification = new Justification { Val = JustificationValues.Right };
        pPr.SpacingBetweenLines = new SpacingBetweenLines
        {
            Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto
        };
        foreach (var run in para.Elements<Run>().ToList()) run.Remove();
        var rPr = new RunProperties
        {
            Bold = new Bold { Val = true },
            BoldComplexScript = new BoldComplexScript { Val = true },
            FontSize = new FontSize { Val = "24" },
            FontSizeComplexScript = new FontSizeComplexScript { Val = "24" },
            Color = new Color { Val = "000000" },
            RightToLeftText = new RightToLeftText { Val = true },
            Languages = new Languages { Bidi = "ar-EG" }
        };
        para.AppendChild(new Run(rPr, new Text(value) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static string ReadText(OpenXmlElement element) => string.Concat(element.Descendants<Text>().Select(x => x.Text));

    private static string Normalize(string input)
    {
        var s = input.Normalize(NormalizationForm.FormKC).Replace("ـ", string.Empty);
        s = Regex.Replace(s, @"[\u064B-\u065F]", string.Empty);
        s = s.Replace("أ", "ا").Replace("إ", "ا").Replace("آ", "ا");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string GetArabicDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Saturday => "السبت",
        DayOfWeek.Sunday => "الأحد",
        DayOfWeek.Monday => "الإثنين",
        DayOfWeek.Tuesday => "الثلاثاء",
        DayOfWeek.Wednesday => "الأربعاء",
        DayOfWeek.Thursday => "الخميس",
        DayOfWeek.Friday => "الجمعة",
        _ => string.Empty
    };
}
