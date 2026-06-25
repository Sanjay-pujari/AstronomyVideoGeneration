using System.Globalization;
using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class NarrationTimeFormatter
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly string[] HindiMonths = ["जनवरी", "फ़रवरी", "मार्च", "अप्रैल", "मई", "जून", "जुलाई", "अगस्त", "सितंबर", "अक्टूबर", "नवंबर", "दिसंबर"];

    public string FormatEventDate(string? value, string language)
    {
        if (!TryParseDate(value, out var date)) return IsHindi(language) ? "सटीक तारीख उपलब्ध नहीं" : "the exact date is not available";
        return IsHindi(language) ? $"{date.Day} {HindiMonths[date.Month - 1]} {date.Year}" : date.ToString("MMMM d, yyyy", EnglishCulture);
    }

    public string FormatPeakTime(string? value, string language) => FormatTimeText(value, language);
    public string FormatViewingWindow(string? value, string language) => FormatTimeText(value, language);

    public string FormatDirection(string? value, string language)
    {
        var text = string.IsNullOrWhiteSpace(value) ? (IsHindi(language) ? "खुले आकाश की ओर" : "toward the open sky") : value.Trim();
        if (!IsHindi(language)) return CleanForbiddenLabels(text);
        text = CleanForbiddenLabels(text).ToLowerInvariant();
        text = text.Replace("from eastern sky toward overhead", "पूर्वी आकाश से सिर के ऊपर तक", StringComparison.OrdinalIgnoreCase)
            .Replace("eastern sky", "पूर्वी आकाश", StringComparison.OrdinalIgnoreCase)
            .Replace("western sky", "पश्चिमी आकाश", StringComparison.OrdinalIgnoreCase)
            .Replace("toward overhead", "सिर के ऊपर तक", StringComparison.OrdinalIgnoreCase)
            .Replace("overhead", "सिर के ऊपर", StringComparison.OrdinalIgnoreCase)
            .Replace("east", "पूर्व", StringComparison.OrdinalIgnoreCase)
            .Replace("west", "पश्चिम", StringComparison.OrdinalIgnoreCase)
            .Replace("south", "दक्षिण", StringComparison.OrdinalIgnoreCase)
            .Replace("north", "उत्तर", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private static string FormatTimeText(string? value, string language)
    {
        var text = CleanForbiddenLabels(string.IsNullOrWhiteSpace(value) ? (IsHindi(language) ? "रात में" : "at night") : value.Trim());
        text = Regex.Replace(text, @"\b\d{4}-\d{2}-\d{2}\b", m => new NarrationTimeFormatter().FormatEventDate(m.Value, language));
        text = Regex.Replace(text, @"\s*\+\d{2}:\d{2}\b", string.Empty);
        text = Regex.Replace(text, @"(?:(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+\d{4}\s+|(?:\b\d{4}-\d{2}-\d{2}\s+))?00:00\s*[–-]\s*05:00\s*IST\b", "from midnight to 5:00 AM IST", RegexOptions.IgnoreCase);
        if (!IsHindi(language)) return text.Replace("midnight", "midnight", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"from\s+midnight\s+to\s+5(?::00)?\s*AM\s*IST", "रात 12 बजे से सुबह 5 बजे तक (भारतीय समय)", RegexOptions.IgnoreCase);
        text = text.Replace("midnight", "रात 12 बजे", StringComparison.OrdinalIgnoreCase)
            .Replace("IST", "भारतीय समय", StringComparison.OrdinalIgnoreCase)
            .Replace("from", "", StringComparison.OrdinalIgnoreCase)
            .Replace("to", "से", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"\b(\d{1,2})(?::00)?\s*AM\b", m => $"सुबह {m.Groups[1].Value} बजे", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(\d{1,2})(?::00)?\s*PM\b", m => $"शाम {m.Groups[1].Value} बजे", RegexOptions.IgnoreCase);
        return text.Replace("  ", " ").Trim();
    }

    private static bool TryParseDate(string? value, out DateTime date)
    {
        if (DateTime.TryParse(value, EnglishCulture, DateTimeStyles.AssumeLocal, out date)) return true;
        date = default; return false;
    }
    private static bool IsHindi(string? language) => string.Equals(language, "hi", StringComparison.OrdinalIgnoreCase) || string.Equals(language, "hi-IN", StringComparison.OrdinalIgnoreCase);
    private static string CleanForbiddenLabels(string value) => Regex.Replace(value, @"\b(listed|local) viewing window:?\s*", string.Empty, RegexOptions.IgnoreCase).Replace("during December", "in December", StringComparison.OrdinalIgnoreCase).Trim();
}
