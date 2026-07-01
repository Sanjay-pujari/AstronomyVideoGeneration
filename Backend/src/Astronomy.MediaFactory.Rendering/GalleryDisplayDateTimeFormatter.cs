using System.Globalization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public static class GalleryDisplayDateTimeFormatter
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo HindiCulture = CultureInfo.GetCultureInfo("hi-IN");

    public static GalleryDisplayDateTime Format(string? eventDate, string? localTime, string? timezone, string? language)
    {
        var isHindi = LocalizationResolver.IsHindi(language);
        var culture = isHindi ? HindiCulture : EnglishCulture;
        var tz = ResolveTimeZone(timezone);
        var sourceInstant = TryParseInstant(localTime, out var localTimeInstant)
            ? localTimeInstant
            : TryParseInstant(eventDate, out var eventDateInstant)
                ? eventDateInstant
                : (DateTimeOffset?)null;
        var local = sourceInstant is null ? (DateTimeOffset?)null : TimeZoneInfo.ConvertTime(sourceInstant.Value, tz);
        var dateText = local is not null
            ? FormatDate(local.Value.DateTime, culture)
            : TryParseDateOnly(eventDate, out var dateOnly)
                ? FormatDate(dateOnly, culture)
                : (isHindi ? "तारीख उपलब्ध नहीं" : "Date TBD");
        var timeText = local is not null
            ? FormatTime(local.Value.DateTime, tz, isHindi)
            : FormatFreeTextTime(localTime, isHindi);
        return new(dateText, timeText);
    }

    private static string FormatDate(DateTime date, CultureInfo culture)
        => date.ToString(culture.Name.Equals("hi-IN", StringComparison.OrdinalIgnoreCase) ? "d MMM yyyy" : "MMM d, yyyy", culture);

    private static string FormatTime(DateTime local, TimeZoneInfo timezone, bool isHindi)
    {
        var suffix = TimeZoneAbbreviation(timezone);
        if (!isHindi)
            return $"{local:h:mm tt} {suffix}".Trim();
        var period = local.Hour < 4 ? "रात" : local.Hour < 12 ? "सुबह" : local.Hour < 17 ? "दोपहर" : local.Hour < 21 ? "शाम" : "रात";
        return $"{period} {local:h:mm} बजे {suffix}".Trim();
    }

    private static string FormatFreeTextTime(string? value, bool isHindi)
    {
        var text = string.IsNullOrWhiteSpace(value) ? (isHindi ? "स्थानीय समय उपलब्ध नहीं" : "best local window") : value.Trim();
        text = Regex.Replace(text, @"\b\d{4}-\d{2}-\d{2}T", string.Empty);
        text = Regex.Replace(text, @"(?:Z|[+-]\d{2}:?\d{2})\b", string.Empty);
        return text;
    }

    private static bool TryParseInstant(string? value, out DateTimeOffset instant)
    {
        if (DateTimeOffset.TryParse(value, EnglishCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out instant))
            return Regex.IsMatch(value ?? string.Empty, @"T|\d{1,2}:\d{2}");
        instant = default;
        return false;
    }

    private static bool TryParseDateOnly(string? value, out DateTime date)
        => DateTime.TryParse(value, EnglishCulture, DateTimeStyles.AssumeLocal, out date);

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timezone) ? "Asia/Kolkata" : timezone.Trim()); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
    }

    private static string TimeZoneAbbreviation(TimeZoneInfo timezone)
        => timezone.Id.Equals("Asia/Kolkata", StringComparison.OrdinalIgnoreCase) ? "IST" : timezone.Id;
}


public static class ObservationDisplayTextResolver
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    public static ObservationDisplayText Resolve(string? eventPeakUtc, string? localPeakTime, string? observationWindow, string? language, string? eventFamily, string? timezone)
    {
        var isHindi = LocalizationResolver.IsHindi(language);
        var formatter = GalleryDisplayDateTimeFormatter.Format(eventPeakUtc, localPeakTime, timezone, language);
        var tz = ResolveTimeZone(timezone);
        var localPeak = TryParseInstant(localPeakTime, out var parsedLocalPeak)
            ? TimeZoneInfo.ConvertTime(parsedLocalPeak, tz)
            : TryParseInstant(eventPeakUtc, out var parsedPeakUtc)
                ? TimeZoneInfo.ConvertTime(parsedPeakUtc, tz)
                : (DateTimeOffset?)null;

        if (IsMeteorShower(eventFamily))
        {
            var displayTime = isHindi ? "आधी रात के बाद से भोर तक" : "After midnight to pre-dawn";
            return new ObservationDisplayText(formatter.DateText, displayTime, displayTime, isHindi ? "hi" : "en", eventFamily ?? string.Empty, eventPeakUtc ?? string.Empty, localPeak?.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture) ?? localPeakTime ?? string.Empty, displayTime, "MeteorShowerNightWindow", "MeteorShowerNightWindow");
        }

        var fallbackWindow = string.IsNullOrWhiteSpace(observationWindow) ? formatter.TimeText : GalleryDisplayDateTimeFormatter.Format(null, observationWindow, timezone, language).TimeText;
        return new ObservationDisplayText(formatter.DateText, formatter.TimeText, fallbackWindow, isHindi ? "hi" : "en", eventFamily ?? string.Empty, eventPeakUtc ?? string.Empty, localPeak?.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture) ?? localPeakTime ?? string.Empty, formatter.TimeText, "LocalPeakTime", string.Empty);
    }

    public static bool ViolatesMeteorDaytimeRule(ObservationDisplayText display)
        => IsMeteorShower(display.EventFamily) && !display.EventFamilyRuleApplied.Equals("MeteorShowerNightWindow", StringComparison.OrdinalIgnoreCase) && IsDaytimeDisplay(display.DisplayedObservationTime);

    private static bool IsMeteorShower(string? eventFamily)
        => (eventFamily ?? string.Empty).Contains("Meteor", StringComparison.OrdinalIgnoreCase);

    private static bool IsDaytimeDisplay(string value)
    {
        if (!DateTimeOffset.TryParse(value, EnglishCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)) return false;
        return parsed.Hour >= 6 && parsed.Hour < 18;
    }

    private static bool TryParseInstant(string? value, out DateTimeOffset instant)
    {
        if (DateTimeOffset.TryParse(value, EnglishCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out instant))
            return Regex.IsMatch(value ?? string.Empty, @"T|\d{1,2}:\d{2}");
        instant = default;
        return false;
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timezone) ? "Asia/Kolkata" : timezone.Trim()); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
    }
}

public sealed record ObservationDisplayText(string DisplayDate, string DisplayTime, string ObservationWindow, string Language, string EventFamily, string EventPeakUtc, string LocalPeakTime, string DisplayedObservationTime, string ObservationTimeSource, string EventFamilyRuleApplied);

public sealed record GalleryDisplayDateTime(string DateText, string TimeText);
