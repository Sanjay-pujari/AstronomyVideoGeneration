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



public enum ObservationVisibilityStatus { Visible, NotVisibleFromLocation, Unverified, GlobalOnly }
public enum ObservationDisplayPolicy { ShowLocalWindow, ShowBestViewingWindow, ShowNotVisible, ShowCheckLocalCircumstances, HideTime }

public sealed record ObservationInfo(
    string EventType,
    string EventFamily,
    string RegionId,
    string LocationName,
    string Timezone,
    string GlobalPeakUtc,
    bool IsVisibleLocally,
    ObservationVisibilityStatus VisibilityStatus,
    ObservationDisplayPolicy DisplayPolicy,
    string DisplayDate,
    string DisplayTime,
    string DisplayWindowLocal,
    string BestViewingWindowLocal,
    string Direction,
    string AltitudeInfo,
    IReadOnlyList<string> SafetyNotes,
    string Reason,
    string Source,
    string Confidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record ObservationIntelligenceInput(
    string? EventType,
    string? EventFamily,
    string? EventPeakUtc,
    string? LocalPeakTime,
    string? DisplayWindowLocal,
    string? BestViewingWindowLocal,
    string? RegionId,
    string? LocationName,
    string? Timezone,
    string? Language,
    bool? IsVisibleLocally = null,
    string? VisibilityStatus = null,
    string? Direction = null,
    string? AltitudeInfo = null,
    bool? LocalVisibilityVerified = null,
    string? Source = null,
    string? Confidence = null);

public static class ObservationIntelligenceResolver
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    public static ObservationInfo Resolve(ObservationIntelligenceInput input)
    {
        var language = input.Language;
        var isHindi = LocalizationResolver.IsHindi(language);
        var eventType = input.EventType ?? string.Empty;
        var family = string.IsNullOrWhiteSpace(input.EventFamily) ? eventType : input.EventFamily!;
        var tz = string.IsNullOrWhiteSpace(input.Timezone) ? "Asia/Kolkata" : input.Timezone!.Trim();
        var date = GalleryDisplayDateTimeFormatter.Format(input.EventPeakUtc, null, tz, language).DateText;
        var warnings = new List<string>();
        var errors = new List<string>();
        var confidence = string.IsNullOrWhiteSpace(input.Confidence) ? "low" : input.Confidence!.Trim();
        var visible = input.IsVisibleLocally == true || input.LocalVisibilityVerified == true || ParseStatus(input.VisibilityStatus) == ObservationVisibilityStatus.Visible;
        var status = visible ? ObservationVisibilityStatus.Visible : ParseStatus(input.VisibilityStatus);
        if (!visible && string.IsNullOrWhiteSpace(input.VisibilityStatus)) status = ObservationVisibilityStatus.Unverified;
        var localWindow = FirstNonEmpty(input.DisplayWindowLocal, input.BestViewingWindowLocal);

        if (ContainsAny(family + eventType, "Meteor"))
        {
            var night = isHindi ? "आधी रात के बाद से भोर तक" : "After midnight to pre-dawn";
            return Build(ObservationVisibilityStatus.Visible, ObservationDisplayPolicy.ShowBestViewingWindow, night, night, "MeteorShowerNightWindow", "Meteor shower peaks are translated into practical night observing guidance.", warnings, errors, confidence);
        }

        if (ContainsAny(family + eventType, "SolarEclipse", "Solar Eclipse"))
        {
            if (visible && !string.IsNullOrWhiteSpace(localWindow))
            {
                if (IsNightText(localWindow)) errors.Add("Solar eclipse local window is nighttime; verified daylight contacts are required.");
                return Build(ObservationVisibilityStatus.Visible, ObservationDisplayPolicy.ShowLocalWindow, localWindow, localWindow, "VerifiedSolarEclipseLocalWindow", "Verified local eclipse contact/window supplied.", warnings, errors, confidence);
            }
            warnings.Add("Local solar eclipse circumstances unavailable; global peak not used as display time.");
            var text = status == ObservationVisibilityStatus.NotVisibleFromLocation ? NotVisible(isHindi) : CheckLocal(isHindi);
            return Build(status == ObservationVisibilityStatus.NotVisibleFromLocation ? status : ObservationVisibilityStatus.Unverified, status == ObservationVisibilityStatus.NotVisibleFromLocation ? ObservationDisplayPolicy.ShowNotVisible : ObservationDisplayPolicy.ShowCheckLocalCircumstances, text, string.Empty, "SolarEclipseRequiresVerifiedLocalContacts", "Solar eclipse display requires verified daylight local circumstances.", warnings, errors, "low");
        }

        if (visible && !string.IsNullOrWhiteSpace(localWindow))
            return Build(ObservationVisibilityStatus.Visible, ObservationDisplayPolicy.ShowLocalWindow, localWindow, localWindow, "VerifiedLocalWindow", "Local visibility/window supplied.", warnings, errors, confidence);

        if (ContainsAny(family + eventType, "Comet", "Constellation", "DeepSkyObject", "Deep Sky", "SpecialEvent"))
            warnings.Add("Future-family observation placeholder policy used; provider can be extended without Gallery renderer changes.");

        if (status == ObservationVisibilityStatus.NotVisibleFromLocation)
            return Build(status, ObservationDisplayPolicy.ShowNotVisible, NotVisible(isHindi), string.Empty, "NotVisibleFromLocation", "Existing metadata says event is not visible locally.", warnings, errors, confidence);

        warnings.Add("Local visibility unavailable; raw global peak conversion suppressed.");
        var fallback = ContainsAny(family + eventType, "Moon", "FullMoon", "SuperMoon") ? (isHindi ? "रात में स्थानीय चंद्रमा दृश्यता जाँचें" : "Check local moonrise/night visibility") : CheckLocal(isHindi);
        return Build(ObservationVisibilityStatus.Unverified, ObservationDisplayPolicy.ShowCheckLocalCircumstances, fallback, string.Empty, "LocalVisibilityUnverified", "Global peak is not a local observation time.", warnings, errors, "low");

        ObservationInfo Build(ObservationVisibilityStatus st, ObservationDisplayPolicy policy, string displayTime, string displayWindow, string provider, string reason, IReadOnlyList<string> warn, IReadOnlyList<string> err, string conf)
            => new(eventType, family, input.RegionId ?? string.Empty, input.LocationName ?? string.Empty, tz, input.EventPeakUtc ?? string.Empty, st == ObservationVisibilityStatus.Visible, st, policy, date, displayTime, displayWindow, policy == ObservationDisplayPolicy.ShowBestViewingWindow ? displayTime : input.BestViewingWindowLocal ?? string.Empty, input.Direction ?? string.Empty, input.AltitudeInfo ?? string.Empty, SafetyNotes(eventType, isHindi), reason, provider, conf, warn.ToArray(), err.ToArray());
    }
    private static ObservationVisibilityStatus ParseStatus(string? s) => Enum.TryParse<ObservationVisibilityStatus>(s, true, out var v) ? v : ObservationVisibilityStatus.Unverified;
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
    private static string FirstNonEmpty(params string?[] v) => v.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
    private static string CheckLocal(bool hi) => hi ? "स्थानीय दृश्यता जाँचें" : "Check local visibility";
    private static string NotVisible(bool hi) => hi ? "इस स्थान से दिखाई नहीं देगा" : "Not visible from this location";
    private static IReadOnlyList<string> SafetyNotes(string eventType, bool hi) => ContainsAny(eventType, "SolarEclipse", "Solar Eclipse") ? [hi ? "सूर्य को केवल प्रमाणित सोलर फिल्टर से देखें" : "Use certified solar filters for any direct Sun viewing"] : [];
    private static bool IsNightText(string text)
    {
        if (DateTimeOffset.TryParse(text, EnglishCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return parsed.Hour < 6 || parsed.Hour >= 18;
        return text.Contains("रात", StringComparison.OrdinalIgnoreCase) || text.Contains("midnight", StringComparison.OrdinalIgnoreCase);
    }
}

public static class ObservationDisplayTextResolver
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    public static ObservationDisplayText Resolve(ObservationInfo observation)
        => new(observation.DisplayDate, observation.DisplayTime, FirstNonEmpty(observation.DisplayWindowLocal, observation.BestViewingWindowLocal, observation.DisplayTime), observation.Timezone, observation.EventFamily, observation.GlobalPeakUtc, string.Empty, observation.DisplayTime, observation.Source, observation.Source);

    public static ObservationDisplayText Resolve(string? eventPeakUtc, string? localPeakTime, string? observationWindow, string? language, string? eventFamily, string? timezone)
    {
        var info = ObservationIntelligenceResolver.Resolve(new ObservationIntelligenceInput(eventFamily, eventFamily, eventPeakUtc, localPeakTime, observationWindow, observationWindow, null, null, timezone, language));
        return Resolve(info);
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
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

public sealed record ObservationDisplayText(string DisplayDate, string DisplayTime, string ObservationWindow, string Language, string EventFamily, string EventPeakUtc, string LocalPeakTime, string DisplayedObservationTime, string ObservationTimeSource, string EventFamilyRuleApplied);

public sealed record GalleryDisplayDateTime(string DateText, string TimeText);
