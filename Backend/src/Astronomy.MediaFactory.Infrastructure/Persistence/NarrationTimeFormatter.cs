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
        if (!IsHindi(language))
        {
            if (Regex.IsMatch(text, @"^east\s+to\s+overhead\s+after\s+10\s*PM$", RegexOptions.IgnoreCase))
                return "eastern sky toward overhead after 10 PM";
            text = Regex.Replace(text, @"\beast(?:ern)?\s+sky\s+to\s+overhead\b", "eastern sky toward overhead", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\beast\s+to\s+overhead\b", "eastern sky toward overhead", RegexOptions.IgnoreCase);
            return CleanForbiddenLabels(text);
        }
        text = CleanForbiddenLabels(text);
        if (Regex.IsMatch(text, @"^east\s+to\s+overhead\s+after\s+10\s*PM$", RegexOptions.IgnoreCase))
            return "रात 10 बजे के बाद पूर्वी आकाश से सिर के ऊपर तक";

        text = Regex.Replace(text, @"\bnorth[-–— ]?east\s+after\s+midnight\b", "आधी रात के बाद उत्तर-पूर्व दिशा", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bnorth[-–— ]?east\b", "उत्तर-पूर्व", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bafter\s+midnight\b", "आधी रात के बाद", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bafter\s+10\s*PM\b", "रात 10 बजे के बाद", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bbefore\s+sunrise\b", "सूर्योदय से पहले", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bfrom\s+eastern\s+sky\s+toward\s+overhead\b", "पूर्वी आकाश से सिर के ऊपर तक", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\beast(?:ern)?\s+sky\s+to\s+overhead\b", "पूर्वी आकाश से सिर के ऊपर तक", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\beast\s+to\s+overhead\b", "पूर्वी आकाश से सिर के ऊपर तक", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\beastern\s+sky\b", "पूर्वी आकाश", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bwestern\s+sky\b", "पश्चिमी आकाश", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\btoward\s+overhead\b", "सिर के ऊपर तक", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\boverhead\b", "सिर के ऊपर", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\beast\b", "पूर्व", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bwest\b", "पश्चिम", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bsouth\b", "दक्षिण", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bnorth\b", "उत्तर", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private static string FormatTimeText(string? value, string language)
    {
        var text = CleanForbiddenLabels(string.IsNullOrWhiteSpace(value) ? (IsHindi(language) ? "रात में" : "at night") : value.Trim());
        text = Regex.Replace(text, @"\b\d{4}-\d{2}-\d{2}\b", m => new NarrationTimeFormatter().FormatEventDate(m.Value, language));
        text = Regex.Replace(text, @"\s*\+\d{2}:?\d{2}\b", string.Empty);
        text = Regex.Replace(text, @"\s*\bUTC\b", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?:(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+\d{4}\s+|(?:\b\d{4}-\d{2}-\d{2}\s+))?00:00\s*[–-]\s*05:00\s*IST\b", "from midnight to 5:00 AM IST", RegexOptions.IgnoreCase);
        if (!IsHindi(language)) return text.Replace("midnight", "midnight", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"(?<h1>\d{1,2}):(?<m1>\d{2})\s*[–-]\s*(?<h2>\d{1,2}):(?<m2>\d{2})\s*IST\b", m =>
        {
            var start = FormatHindiClock(int.Parse(m.Groups["h1"].Value), m.Groups["m1"].Value);
            var end = FormatHindiClock(int.Parse(m.Groups["h2"].Value), m.Groups["m2"].Value);
            return $"{start} से {end} तक";
        }, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?<date>\b\d{1,2}\s+(?:जनवरी|फ़रवरी|मार्च|अप्रैल|मई|जून|जुलाई|अगस्त|सितंबर|अक्टूबर|नवंबर|दिसंबर)\s+\d{4})\s+(?=(?:रात|सुबह|शाम)\b)", "${date} की ");
        text = Regex.Replace(text, @"from\s+midnight\s+to\s+5(?::00)?\s*AM\s*IST", "रात 12 बजे से सुबह 5 बजे तक", RegexOptions.IgnoreCase);
        text = text.Replace("midnight", "रात 12 बजे", StringComparison.OrdinalIgnoreCase)
            .Replace("IST", "", StringComparison.OrdinalIgnoreCase)
            .Replace("from", "", StringComparison.OrdinalIgnoreCase)
            .Replace("to", "से", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"\b(\d{1,2})(?::00)?\s*AM\b", m => $"सुबह {m.Groups[1].Value} बजे", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(\d{1,2})(?::00)?\s*PM\b", m => $"शाम {m.Groups[1].Value} बजे", RegexOptions.IgnoreCase);
        return text.Replace("  ", " ").Trim();
    }

    private static string FormatHindiClock(int hour, string minutes)
    {
        var displayHour = hour % 12;
        if (displayHour == 0) displayHour = 12;
        var period = hour < 12 ? "सुबह" : "शाम";
        if (hour == 0) period = "रात";
        var minuteText = minutes == "00" ? string.Empty : $":{minutes}";
        return $"{period} {displayHour}{minuteText} बजे";
    }

    private static bool TryParseDate(string? value, out DateTime date)
    {
        if (DateTime.TryParse(value, EnglishCulture, DateTimeStyles.AssumeLocal, out date)) return true;
        date = default; return false;
    }
    private static bool IsHindi(string? language) => string.Equals(language, "hi", StringComparison.OrdinalIgnoreCase) || string.Equals(language, "hi-IN", StringComparison.OrdinalIgnoreCase);
    private static string CleanForbiddenLabels(string value) => Regex.Replace(value, @"\b(listed|local) viewing window:?\s*", string.Empty, RegexOptions.IgnoreCase).Replace("during December", "in December", StringComparison.OrdinalIgnoreCase).Trim();
}
