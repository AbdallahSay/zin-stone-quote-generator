using System.Text.RegularExpressions;

namespace FactoryQuoteApi.Services;

/// <summary>
/// User-facing quotation attachment name. Internal document storage IDs must remain unchanged.
/// </summary>
public static class QuoteDisplayFileName
{
    public static string BuildBaseName(string? clientName, string? projectName, string fallback = "عرض سعر")
    {
        var company = CleanPart(clientName);
        company = Regex.Replace(company, @"^شركة(?:\s+|$)", string.Empty).Trim();
        var project = CleanPart(projectName);
        project = Regex.Replace(project, @"^مشروع(?:\s+|$)", string.Empty).Trim();

        var title = string.IsNullOrWhiteSpace(company) ? CleanPart(fallback) : $"عرض سعر شركة {company}";
        if (string.IsNullOrWhiteSpace(title)) title = "عرض سعر";
        if (!string.IsNullOrWhiteSpace(project)) title += $" مشروع {project}";

        // Keep filenames short enough for mobile sharing and common filesystems.
        return title.Length > 140 ? title[..140].TrimEnd(' ', '.') : title;
    }

    public static string BuildPdfName(string? clientName, string? projectName) =>
        BuildBaseName(clientName, projectName) + ".pdf";

    private static string CleanPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var safe = Regex.Replace(value, "[\\\\/:*?\"<>|\\u0000-\\u001F]", " ");
        return Regex.Replace(safe, @"\s+", " ").Trim(' ', '.');
    }
}
