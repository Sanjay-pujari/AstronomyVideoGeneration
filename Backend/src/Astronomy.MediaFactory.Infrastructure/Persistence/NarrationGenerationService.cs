using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class NarrationGenerationService : INarrationGenerationService
{
    private readonly NarrationTimeFormatter formatter = new();
    private readonly MediaFactoryDbContext? db;

    public NarrationGenerationService() { }

    public NarrationGenerationService(MediaFactoryDbContext db)
        => this.db = db;

    public async Task<NarrationPreviewResponse> GeneratePreviewAsync(NarrationPreviewRequest request, CancellationToken cancellationToken)
        => Generate(await HydrateAsync(request, cancellationToken), useNormalizer: true);

    public async Task<NarrationPreviewResponse> GenerateProductionNarrationAsync(NarrationPreviewRequest request, CancellationToken cancellationToken)
        => Generate(await HydrateAsync(request, cancellationToken), useNormalizer: false);

    private async Task<HydratedNarrationRequest> HydrateAsync(NarrationPreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PlanId)) return new(request, null);
        if (!Guid.TryParse(request.PlanId, out var planId)) throw new ArgumentException($"planId '{request.PlanId}' is not a valid content generation plan id.", nameof(request));
        if (db is null) throw new ArgumentException($"planId '{request.PlanId}' was provided, but plan hydration is not available in this runtime.", nameof(request));

        var plan = await db.ContentGenerationPlans
            .AsNoTracking()
            .Include(p => p.AstronomyEventIntelligence)!.ThenInclude(e => e!.Objects)
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken)
            ?? throw new ArgumentException($"ContentGenerationPlan '{request.PlanId}' was not found.", nameof(request));
        var intelligence = plan.AstronomyEventIntelligence
            ?? throw new ArgumentException($"ContentGenerationPlan '{request.PlanId}' is not linked to AstronomyEventIntelligence.", nameof(request));

        var metadata = BuildPlanMetadata(intelligence);
        var hydratedEventType = Clean(intelligence.EventType, Clean(plan.PrimaryAstronomyEventTypeCode, request.EventType));
        var hydratedEventName = Clean(intelligence.Title, Clean(plan.Title, request.EventName));
        var hydratedShortTitle = FirstNonEmpty(ReadEventJsonString(intelligence, "shortTitle", "ShortTitle"), intelligence.Summary, request.ShortTitle);
        var requestedEventType = Clean(request.EventType, string.Empty);
        var requestedEventName = Clean(request.EventName, string.Empty);
        var requestedShortTitle = Clean(request.ShortTitle, string.Empty);
        var authoritativeEventType = string.IsNullOrWhiteSpace(requestedEventType) ? hydratedEventType : requestedEventType;
        var authoritativeEventName = string.IsNullOrWhiteSpace(requestedEventName) ? hydratedEventName : requestedEventName;
        var authoritativeShortTitle = string.IsNullOrWhiteSpace(requestedShortTitle) ? hydratedShortTitle : requestedShortTitle;
        var conflictDetected =
            (!string.IsNullOrWhiteSpace(requestedEventType) && !string.Equals(requestedEventType, hydratedEventType, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(requestedShortTitle) && !string.Equals(requestedShortTitle, hydratedShortTitle, StringComparison.OrdinalIgnoreCase));
        var hydrated = request with
        {
            EventType = authoritativeEventType,
            EventName = authoritativeEventName,
            ShortTitle = authoritativeShortTitle,
            Language = string.IsNullOrWhiteSpace(request.Language) ? Clean(plan.Language, Clean(intelligence.Language, request.Language)) : request.Language,
            RegionId = Clean(plan.RegionId, Clean(intelligence.RegionId, request.RegionId)),
            Format = Clean(plan.PlannedFormat, request.Format),
            EventMetadata = metadata
        };
        return new(hydrated, new(request.PlanId, true, true, false, hydrated.EventType, hydrated.EventName, hydrated.RegionId,
            requestedEventType, requestedEventType, hydratedEventType, authoritativeEventType,
            requestedShortTitle, hydratedShortTitle, authoritativeShortTitle, conflictDetected,
            conflictDetected ? "CurrentEventLockWins" : null));
    }

    private NarrationPreviewResponse Generate(HydratedNarrationRequest hydratedRequest, bool useNormalizer)
    {
        var request = hydratedRequest.Request;
        ArgumentNullException.ThrowIfNull(request);
        var language = IsHindi(request.Language) ? "hi" : "en";
        var eventType = Clean(request.EventType, "astronomy event");
        var eventName = Clean(request.EventName, Clean(request.ShortTitle, "this sky event"));
        var regionId = Clean(request.RegionId, string.Empty);
        var metadata = Metadata.From(request.EventMetadata);
        var dateResolution = ResolveEventDate(metadata);
        var date = formatter.FormatEventDate(dateResolution.Value, language);
        var peak = formatter.FormatPeakTime(metadata.PeakTime, language);
        var window = formatter.FormatViewingWindow(metadata.ViewingWindow ?? metadata.PeakTime, language);
        var direction = formatter.FormatDirection(metadata.Direction, language);
        var context = useNormalizer
            ? NarrationEventNormalizer.Normalize(request, metadata, date, peak, window, direction, language)
            : LegacyContext(eventType, eventName, regionId, date, peak, window, direction, language);
        var scenes = new[]
        {
            Scene("hook", "Hook", Hook(context)),
            Scene("interesting-fact", "InterestingFact", InterestingFact(context)),
            Scene("best-time", "BestTime", BestTime(context)),
            Scene("final-reminder", "FinalReminder", FinalReminder(context))
        };
        var validated = scenes.Select(s => s with { Validation = ValidateScene(s.ScenePurpose, s.Narration, eventName, language, request.ShortTitle, context, request.Language, dateResolution) }).ToArray();
        var errors = validated.SelectMany(s => s.Validation.Errors).ToList();
        var warnings = validated.SelectMany(s => s.Validation.Warnings).ToList();
        if (validated.Select(s => NormalizeSentence(s.Narration)).GroupBy(s => s).Any(g => g.Count() > 1)) errors.Add("Duplicate sentence appears in narration.");
        var hookText = validated.FirstOrDefault(s => s.ScenePurpose == "Hook")?.Narration ?? string.Empty;
        var factText = validated.FirstOrDefault(s => s.ScenePurpose == "InterestingFact")?.Narration ?? string.Empty;
        if (ShareSameFactPhrase(hookText, factText)) errors.Add("Hook and InterestingFact share the same fact phrase.");
        var overall = new NarrationValidationResult(errors.Count == 0, errors, warnings);
        var diagnostics = new NarrationFormattingDiagnostics(date, peak, window, direction,
            ["FormatEventDate(language)", "FormatPeakTime(language)", "FormatViewingWindow(language)", "FormatDirection(language)", useNormalizer ? "NarrationEventNormalizer" : "Legacy narration context", "No SRT/TTS/video/Phase14 execution"], []);
        var contextDiagnostics = new NarrationContextDiagnostics(useNormalizer, eventName, Clean(request.ShortTitle, string.Empty), context.DisplayTitle, context.DisplayLocation, context.DisplayDate, context.DisplayViewingWindow, context.DisplayDirection, context.Family, context.ObservationContextDiagnostics);
        return new NarrationPreviewResponse(request.PlanId, eventType, eventName, language, regionId, request.Format, request.ReturnScenes ? validated : [], overall, diagnostics, Clean(request.ShortTitle, null!), hydratedRequest.Diagnostics, contextDiagnostics);
    }

    private static NarrationPreviewScene Scene(string id, string purpose, string narration) => new(id, purpose, narration, new(true, [], []));

    private static string Clean(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static NarrationContext LegacyContext(string eventType, string eventName, string regionId, string date, string peak, string window, string direction, string language)
        => new(FamilyFrom(eventType, eventName), EventDisplayName(eventName, eventType), EventShortName(eventName), ExtractObjects(eventName), RegionName(regionId), date, peak, window, direction, window, direction, string.Empty, peak, window, null, null, EventFact(eventType, eventName, language), EventFact(eventType, eventName, language), HistoricalContextFor(FamilyFrom(eventType, eventName), eventName), RarityContextFor(FamilyFrom(eventType, eventName), eventName), language);

    private static string Hook(NarrationContext context) => IsHindi(context.Language)
        ? $"{context.DisplayDate} को {HindiName(context.DisplayTitle)} अपने चरम पर होगा, इसलिए साफ आसमान में इसे देखने का यह अच्छा मौका है।"
        : $"On {context.DisplayDate}, {context.DisplayTitle} will reach its peak, offering one of the year's best chances to see {ViewerBenefit(context.Family, context.DisplayTitle)}.";

    private static string InterestingFact(NarrationContext context) => IsHindi(context.Language)
        ? $"{HindiFact(context.DisplayTitle, context.InterestingFact)}।"
        : context.InterestingFact;

    private static string BestTime(NarrationContext context)
    {
        if (context.Family == "NamedFullMoon")
            return IsHindi(context.Language)
                ? "चंद्रोदय के समय पूर्वी आकाश की ओर देखें, फिर रात बढ़ने के साथ चंद्रमा को ऊपर उठते हुए देखें।"
                : "Look toward the eastern sky near moonrise, then watch the Moon climb higher through the night.";
        if (IsHindi(context.Language))
            return $"{HindiRegion(context.DisplayLocation)} में देखने का सुझाया समय {HindiBestTimeWindow(context)} है। {NormalizeDirectionForSentence(context.ObservationDirection)} देखें।";
        if (context.Family == "PlanetConjunction")
            return context.ViewerBestTime;
        if (context.Family == "SolarEclipse" && !string.IsNullOrWhiteSpace(context.SafetyNote))
            return $"For observers in {context.DisplayLocation}, the eclipse viewing window runs {context.ObservationWindow} on {context.DisplayDate}. Look {NormalizeDirectionForSentence(context.ObservationDirection)}.";
        return $"For observers in {context.DisplayLocation}, the recommended viewing window runs {context.ObservationWindow} on {context.DisplayDate}. Look toward {NormalizeDirectionForSentence(context.ObservationDirection)} as the night deepens.";
    }

    private static string HindiBestTimeWindow(NarrationContext context)
    {
        var window = context.DisplayViewingWindow.Trim();
        return ContainsDisplayDate(window, context.DisplayDate) ? window : $"{context.DisplayDate} की {window}";
    }

    private static bool ContainsDisplayDate(string text, string displayDate)
        => !string.IsNullOrWhiteSpace(displayDate) && text.Contains(displayDate, StringComparison.OrdinalIgnoreCase);

    private static string FinalReminder(NarrationContext context) => IsHindi(context.Language)
        ? $"अगर आसमान साफ रहे, तो {HindiShortName(context.ShortDisplayTitle)} साल के यादगार आकाश-दृश्यों में से एक बन सकता है। कुछ शांत मिनट बाहर बिताइए और रात के आसमान को आपको चौंकाने दीजिए।"
        : $"If skies remain clear, {context.ShortDisplayTitle} could become one of the most rewarding skywatching moments of the year. {context.RarityContext}";

    private static string EventFact(string type, string name, string lang)
    {
        var n = name.ToLowerInvariant(); var t = type.ToLowerInvariant();
        if (n.Contains("geminid")) return "the Geminids come from asteroid 3200 Phaethon rather than a traditional comet";
        if (n.Contains("perseid")) return "the Perseids are fed by debris from comet Swift-Tuttle";
        if (n.Contains("strawberry")) return "the Strawberry Moon name comes from June seasonal traditions around ripening strawberries";
        if (n.Contains("wolf")) return "the Wolf Moon name is tied to winter traditions and the sound of wolves in the cold season";
        if (n.Contains("jupiter") && n.Contains("venus")) return "Jupiter and Venus are the two brightest planets, so their close pairing after twilight is especially striking";
        if (n.Contains("mars") && n.Contains("saturn")) return "Mars and Saturn contrast color and steadier golden light, making their conjunction visually different";
        if (t.Contains("meteor")) return "meteor showers are best when you watch a wide, dark sky instead of staring only at the radiant";
        if (t.Contains("conjunction")) return "a conjunction is a line-of-sight pairing, so each planet pair has its own brightness, color, and timing";
        if (t.Contains("moon")) return "full Moon names preserve seasonal observing traditions, not just astronomy labels";
        return $"this {type} has specific timing, geometry, and observing conditions for this event";
    }

    private static NarrationValidationResult ValidateScene(string purpose, string text, string eventName, string language, string? rawShortTitle, NarrationContext context, string? requestedLanguage, DateResolution dateResolution)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        var hookDateValidation = purpose == "Hook" ? ValidateHookExactDate(text, language, requestedLanguage, dateResolution) : null;
        var rawIsoDateAllowedAsFallback = hookDateValidation?.Passed == true && !IsHindi(language) && Regex.IsMatch(text, @"\b\d{4}-\d{2}-\d{2}\b");
        if (!rawIsoDateAllowedAsFallback && Regex.IsMatch(text, @"\b\d{4}-\d{2}-\d{2}\b")) errors.Add("Raw ISO date appears.");
        if (!rawIsoDateAllowedAsFallback && Regex.IsMatch(text, @"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?Z?)?\b")) errors.Add("Raw ISO date or UTC timestamp appears.");
        if (Regex.IsMatch(text, @"\bUTC\b", RegexOptions.IgnoreCase)) errors.Add("UTC timestamp appears.");
        if (Regex.IsMatch(text, @"peaks\s+2026|minimum angular separation|consolidated from", RegexOptions.IgnoreCase)) errors.Add("Raw production metadata phrase appears.");
        if (Regex.IsMatch(text, @"\b[A-Z]{2}-[A-Z]{2}-[A-Z0-9-]+\b")) errors.Add("Raw region code appears.");
        if (!string.IsNullOrWhiteSpace(rawShortTitle) && !string.Equals(rawShortTitle, context.ShortDisplayTitle, StringComparison.OrdinalIgnoreCase) && ContainsUnapprovedRawShortTitle(text, rawShortTitle, context)) errors.Add("Raw short title appears.");
        if (!string.IsNullOrWhiteSpace(eventName) && !string.Equals(eventName, context.DisplayTitle, StringComparison.OrdinalIgnoreCase) && !ContainsOnlyApprovedRawEventTitle(text, eventName, context)) errors.Add("Raw internal event title appears.");
        if (Regex.IsMatch(text, @"[+-]\d{2}:\d{2}")) errors.Add("Timezone offset appears.");
        if (Regex.IsMatch(text, "placeholder|listed viewing window|local viewing window|during December", RegexOptions.IgnoreCase)) errors.Add("Placeholder or forbidden phrase appears.");
        if (text.Length > 0 && char.IsLower(text[0])) errors.Add("Scene narration starts with lowercase letter.");
        if (hookDateValidation is not null)
        {
            warnings.Add(hookDateValidation.Diagnostics);
            if (!hookDateValidation.Passed) errors.Add("Hook lacks exact date. " + hookDateValidation.Diagnostics);
        }
        if (Regex.IsMatch(text, @"^\s*(Interesting fact|Best time):", RegexOptions.IgnoreCase)) errors.Add("Scene starts with a forbidden label.");
        if (Regex.IsMatch(text, @"\b" + Regex.Escape(eventName) + @"\s+matters because", RegexOptions.IgnoreCase)) errors.Add("Scene uses awkward full event title phrasing.");
        if (purpose == "BestTime" && (Regex.IsMatch(text, @"\b(metadata|window unavailable|not available)\b", RegexOptions.IgnoreCase) || !Regex.IsMatch(text, @"\b(AM|PM|midnight|sunrise|sunset|twilight|evening|dawn|moonrise|maximum eclipse|सुबह|शाम|रात|बजे|चंद्रोदय|ग्रहण)\b", RegexOptions.IgnoreCase))) errors.Add("BestTime lacks real formatted window.");
        if (purpose == "BestTime" && Regex.IsMatch(text, @"\b\d{1,2}:\d{2}\s*[–-]\s*\d{1,2}:\d{2}\b")) errors.Add("BestTime contains raw numeric time range.");
        if (purpose == "BestTime" && context.Family == "PlanetConjunction" && Regex.IsMatch(text.Trim(), @"^(?:.*\s)?\d{1,2}:\d{2}(?:\s*(?:AM|PM|IST))?\.?$", RegexOptions.IgnoreCase)) errors.Add("Planet Conjunction BestTime uses only a raw time.");
        if (purpose == "BestTime" && !ContainsViewerUsefulDirection(text, language, context, out var directionDiagnostics)) errors.Add("BestTime lacks viewer-useful direction. " + directionDiagnostics);
        if (Regex.IsMatch(text, @"toward the open sky", RegexOptions.IgnoreCase)) errors.Add("Forbidden generic direction appears.");
        if (purpose == "BestTime" && Regex.IsMatch(text, @"use\s+.+?\s+as your peak-time cue", RegexOptions.IgnoreCase)) errors.Add("BestTime contains peak-time cue phrasing.");
        if (purpose == "BestTime" && IsHindi(language) && ContainsRepeatedHindiDatePhrase(text)) errors.Add("BestTime contains the same Hindi date phrase twice.");
        if (purpose == "FinalReminder" && text.Contains("come back for the next sky event", StringComparison.OrdinalIgnoreCase)) errors.Add("FinalReminder is generic.");
        if (purpose == "InterestingFact" && !ContainsSpecificFact(text, eventName, context, out var factDiagnostics)) errors.Add($"InterestingFact lacks event-specific fact. {factDiagnostics}");
        if (IsHindi(language) && Regex.IsMatch(text, @"\b(Jupiter and Venus|northeast after midnight|eastern sky|open sky|after 10 PM|early evening|before sunrise|after sunset|eastern horizon|PM|AM|raw English direction phrases)\b|\s+to\s+|\b(eastern|western|northern|southern)\s+(?:horizon|sky)\b|\b(?:toward|overhead|open sky)\b", RegexOptions.IgnoreCase)) errors.Add("Hindi contains English leakage outside approved proper nouns.");
        if (IsHindi(language) && Regex.IsMatch(text, @"(?:पूर्व|पूर्वी|पश्चिम|सिर के ऊपर).*(?:\s+to\s+|after|before|PM|AM|East|overhead)|(?:\s+to\s+|after|before|PM|AM|East|overhead).*(?:पूर्व|पूर्वी|पश्चिम|सिर के ऊपर)", RegexOptions.IgnoreCase)) errors.Add("Hindi contains mixed Hindi-English direction phrasing.");
        return new(errors.Count == 0, errors, warnings);
    }

    private static HookDateValidation ValidateHookExactDate(string text, string resolvedLanguage, string? requestedLanguage, DateResolution dateResolution)
    {
        var normalizedText = NormalizeDateText(text);
        var candidates = BuildExpectedDateCandidates(dateResolution.Value, resolvedLanguage).ToArray();
        var normalizedCandidates = candidates.Select(NormalizeDateText).ToArray();
        var passed = normalizedCandidates.Any(c => !string.IsNullOrWhiteSpace(c) && normalizedText.Contains(c, StringComparison.OrdinalIgnoreCase));
        var diagnostics = JsonSerializer.Serialize(new
        {
            requestedLanguage = Clean(requestedLanguage, resolvedLanguage),
            resolvedLanguage,
            expectedDateCandidates = candidates,
            actualHookText = text,
            normalizedActualHookText = normalizedText,
            dateValidationPassed = passed,
            dateSourceUsed = dateResolution.Source
        });
        return new(passed, diagnostics);
    }

    private static IEnumerable<string> BuildExpectedDateCandidates(string? value, string language)
    {
        if (!TryParseDate(value, out var date)) yield break;
        if (IsHindi(language))
        {
            var month = HindiMonthName(date.Month);
            yield return $"{date.Day} {month} {date.Year}";
            yield return $"{ToDevanagari(date.Day.ToString("00", CultureInfo.InvariantCulture))} {month} {ToDevanagari(date.Year.ToString(CultureInfo.InvariantCulture))}";
            yield return date.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-US"));
            yield break;
        }

        yield return date.ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));
        yield return date.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));
        yield return date.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-US"));
        yield return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateResolution ResolveEventDate(Metadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.LocalPeakTime)) return new(metadata.LocalPeakTime, "localPeakTime");
        if (!string.IsNullOrWhiteSpace(metadata.EventDate)) return new(metadata.EventDate, "eventDate");
        if (!string.IsNullOrWhiteSpace(metadata.PeakUtc)) return new(metadata.PeakUtc, "peakUtc");
        return new(metadata.PeakDate, "fallback");
    }

    private static bool TryParseDate(string? value, out DateTime date)
    {
        if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AssumeLocal, out date)) return true;
        date = default;
        return false;
    }

    private static string NormalizeDateText(string value)
    {
        var text = ToAsciiDigits(value ?? string.Empty).Normalize(NormalizationForm.FormC);
        text = Regex.Replace(text, @"[\u200c\u200d]", string.Empty);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private static string ToAsciiDigits(string value) => string.Concat((value ?? string.Empty).Select(ch => ch >= '०' && ch <= '९' ? (char)('0' + ch - '०') : ch));
    private static string ToDevanagari(string value) => string.Concat((value ?? string.Empty).Select(ch => ch >= '0' && ch <= '9' ? (char)('०' + ch - '0') : ch));
    private static string HindiMonthName(int month) => month is >= 1 and <= 12 ? HindiMonths[month - 1] : string.Empty;
    private static readonly string[] HindiMonths = ["जनवरी", "फ़रवरी", "मार्च", "अप्रैल", "मई", "जून", "जुलाई", "अगस्त", "सितंबर", "अक्टूबर", "नवंबर", "दिसंबर"];
    private sealed record DateResolution(string? Value, string Source);
    private sealed record HookDateValidation(bool Passed, string Diagnostics);

    private static bool ContainsUnapprovedRawShortTitle(string text, string rawShortTitle, NarrationContext context)
    {
        var remaining = text;
        foreach (var approved in ApprovedShortTitlePhrases(rawShortTitle, context).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(value => value.Length))
        {
            remaining = Regex.Replace(remaining, Regex.Escape(approved), string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return remaining.Contains(rawShortTitle, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ApprovedShortTitlePhrases(string rawShortTitle, NarrationContext context)
    {
        yield return context.DisplayTitle;
        yield return context.ShortDisplayTitle;

        if (context.Family == "MeteorShower")
        {
            var clean = Regex.Replace(rawShortTitle.Trim(), @"\s+(?:meteor\s+shower|peak)$", string.Empty, RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrWhiteSpace(clean))
            {
                yield return $"the {clean} meteor shower";
                yield return $"the {clean}";
                yield return $"{clean} meteors";
            }

            if (clean.Equals("Geminids", StringComparison.OrdinalIgnoreCase))
            {
                yield return "जेमिनिड्स";
                yield return "जेमिनिड्स (Geminids)";
            }
        }

        if (context.Family == "SolarEclipse")
        {
            yield return "the total solar eclipse";
            yield return "total solar eclipse";
            yield return "the solar eclipse";
        }
    }

    private static bool ContainsOnlyApprovedRawEventTitle(string text, string rawEventTitle, NarrationContext context)
    {
        if (!text.Contains(rawEventTitle, StringComparison.OrdinalIgnoreCase)) return true;
        if (context.Family != "SolarEclipse") return false;

        var remaining = text;
        foreach (var approved in ApprovedSolarEclipseDisplayTitlePhrases().OrderByDescending(value => value.Length))
        {
            remaining = Regex.Replace(remaining, Regex.Escape(approved), string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return !remaining.Contains(rawEventTitle, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ApprovedSolarEclipseDisplayTitlePhrases()
    {
        yield return "the total solar eclipse";
        yield return "total solar eclipse";
        yield return "the solar eclipse";
    }

    private static string EventDisplayName(string name, string type)
    {
        var shortName = EventShortName(name);
        if (type.Contains("meteor", StringComparison.OrdinalIgnoreCase) || name.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return $"the {shortName} meteor shower";
        return name;
    }

    private static string EventShortName(string name)
    {
        var cleaned = Regex.Replace(name ?? string.Empty, @"\s+(Meteor\s+Shower\s+Peak|Meteor\s+Shower|Peak)$", string.Empty, RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "this sky event" : cleaned;
    }

    private static string ViewerBenefit(string type, string name)
    {
        if (type.Contains("meteor", StringComparison.OrdinalIgnoreCase) || name.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "bright meteors streak across the night sky";
        if (type.Contains("moon", StringComparison.OrdinalIgnoreCase) || name.Contains("moon", StringComparison.OrdinalIgnoreCase)) return "the Moon at its most photogenic";
        if (type.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "the changing Sun and Moon safely";
        return "a memorable skywatching view";
    }

    private static string FamilyFrom(string eventType, string eventName)
    {
        var text = $"{eventType} {eventName}";
        if (text.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "MeteorShower";
        if (IsPlanetPairingFamilyText(text)) return "PlanetConjunction";
        if (text.Contains("planetgrouping", StringComparison.OrdinalIgnoreCase) || text.Contains("groupedplanets", StringComparison.OrdinalIgnoreCase) || text.Contains("planet grouping", StringComparison.OrdinalIgnoreCase)) return "PlanetGrouping";
        if (text.Contains("solar", StringComparison.OrdinalIgnoreCase) && text.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "SolarEclipse";
        if (text.Contains("moon", StringComparison.OrdinalIgnoreCase)) return "NamedFullMoon";
        return "AstronomyEvent";
    }

    private static bool IsPlanetPairingFamilyText(string text)
        => Regex.IsMatch(text ?? string.Empty, @"\b(PlanetPairing|PlanetConjunction|PLANET_CONJUNCTION|Conjunction|PlanetaryEncounter|CloseApproach|pairing)\b", RegexOptions.IgnoreCase);

    private static IReadOnlyList<string> ExtractObjects(string text)
    {
        var known = new[] { "Jupiter", "Venus", "Mars", "Saturn", "Mercury", "Moon", "Sun" };
        var objects = known.Where(o => text.Contains(o, StringComparison.OrdinalIgnoreCase)).ToArray();
        return objects.Length == 0 ? ["Sky"] : objects;
    }

    private static string HistoricalContextFor(string family, string name)
    {
        if (family == "NamedFullMoon" && name.Contains("Strawberry", StringComparison.OrdinalIgnoreCase)) return "The Strawberry Moon name is tied to June seasonal traditions and ripening strawberries.";
        if (family == "NamedFullMoon" && name.Contains("Wolf", StringComparison.OrdinalIgnoreCase)) return "The Wolf Moon name comes from winter folklore and the sound of wolves in the cold season.";
        if (family == "SolarEclipse") return "Solar eclipses have helped observers study the Sun's outer atmosphere during totality.";
        return "Skywatchers have long used moments like this as seasonal markers.";
    }

    private static string RarityContextFor(string family, string name)
    {
        if (family == "SolarEclipse") return "Use proper solar filters outside totality, and if totality occurs, watch for the Sun's corona only during the safe total phase.";
        if (family == "PlanetConjunction") return "The apparent pairing is brief, because both planets keep moving against the background stars.";
        if (family == "MeteorShower") return "The best memories often come from patient watching under a dark, open sky.";
        return "The name and timing make this event feel different from an ordinary night outside.";
    }

    private static string RegionName(string regionId)
    {
        if (regionId.Contains("UDAIPUR", StringComparison.OrdinalIgnoreCase)) return "Udaipur";
        return string.IsNullOrWhiteSpace(regionId) ? "your location" : regionId;
    }

    private static string HindiRegion(string regionId) => RegionName(regionId) == "Udaipur" ? "उदयपुर" : "आपके स्थान";

    private static string NormalizeDirectionForSentence(string direction)
    {
        var text = direction.Trim();
        text = Regex.Replace(text, @"^east\s+to\s+overhead\s+after\s+10\s*PM$", "eastern sky toward overhead after 10 PM", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^from\s+", string.Empty, RegexOptions.IgnoreCase);
        return text;
    }

    private static string FormatHindiTerminology(string value, bool firstMention = false)
    {
        var text = value ?? string.Empty;
        text = Regex.Replace(text, @"\bJupiter\s+and\s+Venus\b", "बृहस्पति और शुक्र", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bJupiter\b", "बृहस्पति", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bVenus\b", "शुक्र", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bMars\b", "मंगल", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bMercury\b", "बुध", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bSaturn\b", "शनि", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bGeminids\b", "जेमिनिड्स", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bPhaethon\b", firstMention ? "फेथॉन (Phaethon)" : "फेथॉन", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bWolf Moon\b", "वुल्फ मून", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bStrawberry Moon\b", "स्ट्रॉबेरी मून", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bCorona\b", "सूर्य का कोरोना", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bTotal Solar Eclipse\b", "पूर्ण सूर्य ग्रहण", RegexOptions.IgnoreCase);
        return text;
    }

    private static string FormatHindiObservation(string value)
    {
        var text = value ?? string.Empty;
        text = Regex.Replace(text, @"\bsouth[-–— ]?east(?:ern)?(?:\s+sky)?\b", "दक्षिण-पूर्वी आकाश", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bSE\b", "दक्षिण-पूर्व", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bnorth[-–— ]?east\s+after\s+midnight\b", "आधी रात के बाद उत्तर-पूर्व दिशा", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bnorth[-–— ]?east\b", "उत्तर-पूर्व", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bafter\s+midnight\b", "आधी रात के बाद", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bearly evening\b", "सूर्यास्त के बाद शुरुआती शाम", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bbefore sunrise\b", "सूर्योदय से पहले", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bafter sunset\b", "सूर्यास्त के बाद", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\beastern horizon\b", "पूर्वी क्षितिज", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\beastern sky\b", "पूर्वी आकाश", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\boverhead\b", "सिर के ऊपर", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bopen sky\b", "खुले आकाश", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bafter 10\s*PM\b", "रात 10 बजे के बाद", RegexOptions.IgnoreCase);
        return text;
    }


    private static bool ContainsViewerUsefulDirection(string text, string language, NarrationContext context, out string diagnostics)
    {
        var expected = IsHindi(language)
            ? new[] { "दक्षिण-पूर्व", "उत्तर-पूर्व", "उत्तर पूर्व", "पूर्व", "आधी रात के बाद", "रात 12 बजे के बाद", "सुबह", "दिशा", "आकाश", "क्षितिज", "ओर", "सूर्य", "चंद्रोदय", "सिर के ऊपर" }
            : new[] { "southeast", "south-east", "northeast", "north-east", "after midnight", "eastern", "southern", "northern", "east", "south", "north", "sky", "direction", "horizon", "toward", "sun", "moonrise", "overhead" };
        var normalizedText = NormalizeValidationText(text, language);
        var detected = expected.Where(token => normalizedText.Contains(NormalizeValidationText(token, language), StringComparison.OrdinalIgnoreCase)).ToArray();
        var passed = detected.Length > 0;
        diagnostics = passed ? string.Empty : BuildNarrationValidationDiagnostics(context, text, expected, detected, "BestTimeDirectionMissing");
        return passed;
    }

    private static string NormalizeValidationText(string value, string language)
    {
        var text = (value ?? string.Empty).Trim();
        text = Regex.Replace(text, "[\u2010\u2011\u2012\u2013\u2014]", "-");
        text = Regex.Replace(text, @"[^\p{L}\p{N}\s/-]+", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return IsHindi(language) ? text : text.ToLowerInvariant();
    }

    private static string BuildNarrationValidationDiagnostics(NarrationContext context, string generatedText, IReadOnlyCollection<string> expected, IReadOnlyCollection<string> detected, string failedRule)
        => JsonSerializer.Serialize(new
        {
            family = context.Family,
            language = context.Language,
            eventName = context.DisplayTitle,
            shortTitle = context.ShortDisplayTitle,
            localizedShortTitle = IsHindi(context.Language) ? HindiShortName(context.ShortDisplayTitle) : context.ShortDisplayTitle,
            generatedScenePurpose = "BestTime",
            generatedText,
            expectedValidationTokens = expected,
            localizedExpectedValidationTokens = expected,
            detectedTokens = detected,
            failedRule,
            sourceDirection = context.ObservationContextDiagnostics?.DirectionSource,
            localizedDirection = context.ObservationDirection,
            sourceViewingWindow = context.ObservationContextDiagnostics?.WindowSource,
            localizedViewingWindow = context.ObservationWindow
        });

    private static bool ContainsRepeatedHindiDatePhrase(string text)
    {
        var matches = Regex.Matches(text, @"\b\d{1,2}\s+(?:जनवरी|फ़रवरी|मार्च|अप्रैल|मई|जून|जुलाई|अगस्त|सितंबर|अक्टूबर|नवंबर|दिसंबर)\s+\d{4}\b");
        return matches.Select(m => m.Value).GroupBy(v => v).Any(g => g.Count() > 1);
    }

    private static bool ShareSameFactPhrase(string hook, string fact)
    {
        var factPhrases = new[] { "3200 Phaethon", "Swift-Tuttle", "debris", "traditional comet", "typical comet", "brightest planets", "seasonal traditions" };
        return factPhrases.Any(phrase => hook.Contains(phrase, StringComparison.OrdinalIgnoreCase) && fact.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }
    private static bool ContainsSpecificFact(string text, string eventName, NarrationContext context, out string diagnostics)
    {
        diagnostics = string.Empty;
        if (context.Family == "MeteorShower") return ContainsMeteorShowerSpecificFact(text, eventName, context, out diagnostics);
        if (context.Family == "NamedFullMoon") return ContainsNamedFullMoonSpecificFact(text, context, out diagnostics);
        if (context.Family is "PlanetConjunction" or "PlanetGrouping") return ContainsPlanetConjunctionSpecificFact(text, context, out diagnostics);
        if (context.Family == "SolarEclipse") return ContainsSolarEclipseSpecificFact(text, context, out diagnostics);

        var passed = text.Contains("debris", StringComparison.OrdinalIgnoreCase) || text.Contains("seasonal traditions", StringComparison.OrdinalIgnoreCase) || text.Contains("specific timing", StringComparison.OrdinalIgnoreCase);
        if (!passed) diagnostics = BuildFactValidationDiagnostics(context.Family, eventName, context.ShortDisplayTitle, text, [context.ShortDisplayTitle, context.DisplayTitle], [], null, null, "No family-specific fact token found.");
        return passed;
    }

    private static bool ContainsMeteorShowerSpecificFact(string text, string eventName, NarrationContext context, out string diagnostics)
    {
        var intelligence = MeteorShowerIntelligence.For(context.DisplayTitle, eventName);
        var expected = MeteorFactTokens(intelligence, context.Language).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var detected = expected.Where(token => ContainsNormalizedToken(text, token, context.Language)).ToArray();
        var meteorScience = (IsHindi(context.Language)
            ? Regex.IsMatch(text, "मलबे|धूमकेतु|क्षुद्रग्रह|उल्क|कण|पर्सियस|मिथुन", RegexOptions.IgnoreCase)
            : Regex.IsMatch(text, @"\b(debris|comet|asteroid|radiant|meteors?|particles?)\b", RegexOptions.IgnoreCase)) && detected.Length > 0;
        var passed = detected.Length > 0 || meteorScience;
        diagnostics = passed ? string.Empty : BuildFactValidationDiagnostics("MeteorShower", eventName, context.ShortDisplayTitle, text, expected, detected, intelligence.ParentBody, intelligence.Radiant, "Meteor shower fact must mention this shower, its parent body, or its radiant.");
        return passed;
    }

    private static bool ContainsNamedFullMoonSpecificFact(string text, NarrationContext context, out string diagnostics)
    {
        var tokens = (IsHindi(context.Language)
            ? new[] { HindiShortName(context.ShortDisplayTitle), "मून", "चंद्रमा", "मौसमी", "परंपरा", "लोक", "स्ट्रॉबेरी", "सर्दियों", "भेड़ियों" }
            : new[] { context.ShortDisplayTitle, Regex.Replace(context.ShortDisplayTitle, @"^the\s+", string.Empty, RegexOptions.IgnoreCase), "Moon", "seasonal", "traditions", "folklore", "farmers", "ripening", "winter" }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var detected = tokens.Where(token => ContainsNormalizedToken(text, token, context.Language)).ToArray();
        var moonToken = IsHindi(context.Language) ? "मून" : "Moon";
        var passed = detected.Contains(moonToken, StringComparer.OrdinalIgnoreCase) && detected.Any(t => !t.Equals(moonToken, StringComparison.OrdinalIgnoreCase));
        diagnostics = passed ? string.Empty : BuildFactValidationDiagnostics("NamedFullMoon", context.DisplayTitle, context.ShortDisplayTitle, text, tokens, detected, null, null, "Named full Moon fact must mention the name or a seasonal naming tradition.");
        return passed;
    }

    private static bool ContainsPlanetConjunctionSpecificFact(string text, NarrationContext context, out string diagnostics)
    {
        var objects = context.DisplayObjects.Where(o => !o.Equals("Sky", StringComparison.OrdinalIgnoreCase)).ToArray();
        var tokens = (IsHindi(context.Language)
            ? objects.Select(LocalizedObjectToken).Concat(["युति", "दृष्टि-रेखा", "पास", "ग्रह", "चमक", "रंग", "समूह", "आकाश"])
            : objects.Concat(["conjunction", "line-of-sight", "pairing", "planet", "brightness", "color", "twilight", "horizon"])).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var detected = tokens.Where(token => ContainsNormalizedToken(text, token, context.Language)).ToArray();
        var passed = objects.Length >= 2 && !IsHindi(context.Language) ? objects.All(o => ContainsToken(text, o)) || detected.Length >= 2 : detected.Length >= 2;
        diagnostics = passed ? string.Empty : BuildFactValidationDiagnostics("PlanetConjunction", context.DisplayTitle, context.ShortDisplayTitle, text, tokens, detected, null, null, "Planet conjunction fact must mention the pair or conjunction geometry.");
        return passed;
    }

    private static string LocalizedObjectToken(string value) => FormatHindiTerminology(value);

    private static bool ContainsSolarEclipseSpecificFact(string text, NarrationContext context, out string diagnostics)
    {
        var variant = Regex.Match(context.DisplayTitle, @"\b(total|partial|annular|hybrid)\b", RegexOptions.IgnoreCase).Value;
        var tokens = new[] { variant, "solar eclipse", "Moon", "Sun", "corona", "totality", "annular", "partial", "hybrid", "safety", "solar filters", "सोलर ग्रहण चश्मे" }.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var detected = tokens.Where(token => ContainsToken(text, token)).ToArray();
        var passed = detected.Length >= 2 || Regex.IsMatch(text, "corona|totality|safety|कोरोना|पूर्णता|सुरक्षा|सोलर ग्रहण चश्मे", RegexOptions.IgnoreCase);
        diagnostics = passed ? string.Empty : BuildFactValidationDiagnostics("SolarEclipse", context.DisplayTitle, context.ShortDisplayTitle, text, tokens, detected, null, null, "Solar eclipse fact must mention the variant, Sun-Moon geometry, or safe-viewing science.");
        return passed;
    }

    private static bool ContainsToken(string text, string token) => !string.IsNullOrWhiteSpace(token) && text.Contains(token, StringComparison.OrdinalIgnoreCase);
    private static bool ContainsNormalizedToken(string text, string token, string language) => !string.IsNullOrWhiteSpace(token) && NormalizeValidationText(text, language).Contains(NormalizeValidationText(token, language), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> MeteorFactTokens(MeteorShowerIntelligence intelligence, string language)
    {
        yield return intelligence.ShortTitle;
        if (!string.IsNullOrWhiteSpace(intelligence.ParentBody)) yield return intelligence.ParentBody;
        if (!string.IsNullOrWhiteSpace(intelligence.Radiant)) yield return intelligence.Radiant;
        if (!IsHindi(language)) yield break;
        if (intelligence.ShortTitle.Contains("Geminid", StringComparison.OrdinalIgnoreCase)) { yield return "जेमिनिड्स"; yield return "क्षुद्रग्रह 3200 फेथॉन"; yield return "Phaethon"; yield return "मिथुन"; }
        if (intelligence.ShortTitle.Contains("Perseid", StringComparison.OrdinalIgnoreCase)) { yield return "पर्सिड्स"; yield return "धूमकेतु 109P/स्विफ्ट-टटल"; yield return "Swift-Tuttle"; yield return "पर्सियस"; }
    }

    private static string BuildFactValidationDiagnostics(string family, string eventName, string shortTitle, string fact, IReadOnlyCollection<string> expected, IReadOnlyCollection<string> detected, string? parentBody, string? radiant, string reason)
        => JsonSerializer.Serialize(new { family, eventName, shortTitle, generatedInterestingFact = fact, expectedFactTokens = expected, detectedFactTokens = detected, parentBodyUsed = parentBody, radiantUsed = radiant, validationReason = reason });
    private static string HindiName(string name) => name.Contains("Geminid", StringComparison.OrdinalIgnoreCase) ? "जेमिनिड्स" : name.Contains("Perseid", StringComparison.OrdinalIgnoreCase) ? "पर्सिड्स" : Regex.Replace(FormatHindiTerminology(name), @"^the\s+", string.Empty, RegexOptions.IgnoreCase);
    private static string HindiShortName(string name) => name.Contains("Geminid", StringComparison.OrdinalIgnoreCase) ? "जेमिनिड्स" : name.Contains("Perseid", StringComparison.OrdinalIgnoreCase) ? "पर्सिड्स" : HindiName(name);
    private static string HindiFact(string name, string fact)
    {
        if (name.Contains("Geminid", StringComparison.OrdinalIgnoreCase)) return "जेमिनिड्स का संबंध सामान्य धूमकेतु से नहीं, क्षुद्रग्रह 3200 फेथॉन (Phaethon) से जुड़े मलबे से है";
        if (name.Contains("Perseid", StringComparison.OrdinalIgnoreCase)) return "पर्सिड्स धूमकेतु 109P/स्विफ्ट-टटल (Swift-Tuttle) के छोड़े मलबे से बनते हैं और पर्सियस से निकलते दिखते हैं";
        if (name.Contains("Strawberry", StringComparison.OrdinalIgnoreCase)) return "स्ट्रॉबेरी मून का नाम जून में स्ट्रॉबेरी पकने की मौसमी परंपरा से जुड़ा है";
        if (name.Contains("Wolf", StringComparison.OrdinalIgnoreCase)) return "वुल्फ मून का नाम सर्दियों और भेड़ियों से जुड़ी लोक परंपरा से आता है";
        if (name.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) && name.Contains("Venus", StringComparison.OrdinalIgnoreCase)) return "बृहस्पति और शुक्र दो बेहद चमकीले ग्रह हैं, इसलिए उनका पास दिखना खास लगता है";
        if (name.Contains("Mars", StringComparison.OrdinalIgnoreCase) && name.Contains("Jupiter", StringComparison.OrdinalIgnoreCase)) return "मंगल और बृहस्पति केवल हमारी दृष्टि-रेखा में पास दिखते हैं, इसलिए रंग और चमक का फर्क साफ दिख सकता है";
        if (name.Contains("planet grouping", StringComparison.OrdinalIgnoreCase) || name.Contains("Mercury", StringComparison.OrdinalIgnoreCase) && name.Contains("Venus", StringComparison.OrdinalIgnoreCase) && name.Contains("Mars", StringComparison.OrdinalIgnoreCase)) return "बुध, शुक्र और मंगल का समूह आकाश में ग्रहों की बदलती स्थिति को एक साथ दिखाता है";
        if (name.Contains("Eclipse", StringComparison.OrdinalIgnoreCase)) return "पूर्ण सूर्य ग्रहण में पूर्णता के समय सूर्य का कोरोना दिख सकता है, लेकिन सुरक्षा सबसे जरूरी है";
        return "इस घटना की अपनी खास समय-रेखा और देखने की दिशा है";
    }

    private sealed record MeteorShowerIntelligence(string ShortTitle, string? ParentBody, string? Radiant)
    {
        public static MeteorShowerIntelligence For(string displayTitle, string eventName)
        {
            var text = $"{displayTitle} {eventName}";
            foreach (var known in Known)
                if (text.Contains(known.ShortTitle, StringComparison.OrdinalIgnoreCase) || text.Contains(known.ShortTitle.TrimEnd('s'), StringComparison.OrdinalIgnoreCase))
                    return known;
            var shortTitle = Regex.Replace(displayTitle, @"^the\s+|\s+Meteor\s+Shower$", string.Empty, RegexOptions.IgnoreCase).Trim();
            shortTitle = string.IsNullOrWhiteSpace(shortTitle) ? Regex.Replace(eventName, @"\s+Meteor\s+Shower.*$", string.Empty, RegexOptions.IgnoreCase).Trim() : shortTitle;
            return new(string.IsNullOrWhiteSpace(shortTitle) ? "this meteor shower" : shortTitle, null, null);
        }

        private static readonly MeteorShowerIntelligence[] Known =
        [
            new("Geminids", "asteroid 3200 Phaethon", "Gemini"),
            new("Perseids", "comet 109P/Swift-Tuttle", "Perseus"),
            new("Leonids", "comet 55P/Tempel-Tuttle", "Leo"),
            new("Orionids", "Halley's Comet", "Orion"),
            new("Eta Aquariids", "Halley's Comet", "Aquarius"),
            new("Quadrantids", "2003 EH1", "Boötes/Quadrans Muralis region"),
            new("Lyrids", "comet C/1861 G1 Thatcher", "Lyra")
        ];
    }
    private static string NormalizeSentence(string text) => Regex.Replace(text, @"\s+", " ").Trim().ToLowerInvariant();
    private static bool IsHindi(string? language) => string.Equals(language, "hi", StringComparison.OrdinalIgnoreCase) || string.Equals(language, "hi-IN", StringComparison.OrdinalIgnoreCase);

    private static class NarrationEventNormalizer
    {
        public static NarrationContext Normalize(NarrationPreviewRequest request, Metadata metadata, string date, string peak, string window, string direction, string language)
        {
            var rawName = Clean(request.EventName, Clean(request.ShortTitle, "this sky event"));
            var eventType = Clean(request.EventType, string.Empty);
            var family = FamilyFrom(eventType, rawName);
            var displayTitle = BuildDisplayTitle(family, rawName, eventType);
            var shortTitle = ShortDisplayTitle(family, displayTitle);
            var location = RegionName(Clean(request.RegionId, string.Empty));
            IReadOnlyList<string> objects = family == "PlanetConjunction" ? ExtractObjects(displayTitle).DefaultIfEmpty("Mars").Take(2).ToArray() : ExtractObjects(displayTitle);

            var observation = BuildObservationContext(family, displayTitle, location, date, peak, window, direction, metadata, language);

            return new NarrationContext(
                family,
                displayTitle,
                shortTitle,
                objects,
                location,
                date,
                peak,
                observation.Window,
                observation.Direction,
                observation.Window,
                observation.Direction,
                observation.TimingNote,
                observation.GeometricPeakTime,
                observation.ViewerBestTime,
                observation.DisplayObjectPair,
                observation.SafetyNote,
                ScientificSummaryFor(family, displayTitle),
                InterestingFactFor(family, displayTitle, observation),
                HistoricalContextFor(family, displayTitle),
                RarityContextFor(family, displayTitle),
                language,
                observation.Diagnostics);
        }


        private static ObservationContext BuildObservationContext(string family, string title, string location, string date, string peak, string window, string direction, Metadata metadata, string language)
        {
            var humanWindow = HumanizeWindow(window);
            var cleanDirection = CleanDirection(direction);
            var windowSource = !string.IsNullOrWhiteSpace(metadata.ViewingWindow) ? "metadata.bestViewingWindowLocal" : "derivedFromPeakTime";
            var directionSource = !string.IsNullOrWhiteSpace(metadata.Direction) ? "metadata.skyDirectionHint" : "familyFallback";
            var fallback = string.IsNullOrWhiteSpace(metadata.ViewingWindow) || string.IsNullOrWhiteSpace(metadata.Direction);
            string? geometricPeak = family == "PlanetConjunction" ? peak : null;
            string timingNote = string.Empty;
            string? pair = null;
            string? safety = null;

            if (family is "PlanetConjunction" or "PlanetGrouping")
            {
                var planetObjects = ExtractObjects(title).Take(2).ToArray();
                pair = planetObjects.Length >= 2 ? (IsHindi(language) ? $"{HindiPlanetName(planetObjects[0])} और {HindiPlanetName(planetObjects[1])}" : $"{planetObjects[0]} and {planetObjects[1]}") : null;
                if (string.IsNullOrWhiteSpace(metadata.ViewingWindow))
                {
                    humanWindow = DeriveConjunctionWindowPhrase(metadata.PeakTime ?? peak, cleanDirection);
                    windowSource = "derivedFromPeakTime";
                }
                if (IsOpenSky(cleanDirection))
                {
                    cleanDirection = DeriveConjunctionDirection(title, metadata.PeakTime ?? peak, metadata.Direction);
                    directionSource = "familyFallback";
                    fallback = true;
                }
                timingNote = "Use the darker twilight window while both planets are above the horizon.";
                var article = string.IsNullOrWhiteSpace(pair) ? "the conjunction" : $"the {pair} pairing";
                var viewer = IsHindi(language)
                    ? $"{LocalizeWindow(humanWindow, language)} सबसे अच्छा समय है। {LocalizeDirection(cleanDirection, language)} की ओर देखें और क्षितिज के पास खुला दृश्य रखें।"
                    : !string.IsNullOrWhiteSpace(metadata.ViewingWindow)
                        ? $"For observers in {location}, the best viewing window runs {humanWindow}. Look toward {NormalizeDirectionForSentence(cleanDirection)} while both planets are above the horizon."
                        : $"For observers in {location}, the best chance to see {article} is {humanWindow}. Look toward {NormalizeDirectionForSentence(cleanDirection)}, and use a clear, low view near the horizon.";
                return Pack(family, geometricPeak, viewer, LocalizeWindow(humanWindow, language), LocalizeDirection(cleanDirection, language), directionSource, windowSource, fallback, timingNote, pair, safety);
            }

            if (family == "MeteorShower")
            {
                if (IsOpenSky(cleanDirection)) { cleanDirection = "the eastern sky toward overhead after 10 PM"; directionSource = "familyFallback"; fallback = true; }
                return Pack(family, null, string.Empty, LocalizeWindow(humanWindow, language), LocalizeDirection(cleanDirection, language), directionSource, windowSource, fallback, "Give your eyes time to adapt to darkness.", null, null);
            }

            if (family == "NamedFullMoon")
            {
                if (string.IsNullOrWhiteSpace(metadata.ViewingWindow)) { humanWindow = "after moonrise and throughout the night"; windowSource = "familyFallback"; fallback = true; }
                if (IsOpenSky(cleanDirection)) { cleanDirection = "the eastern horizon at moonrise, then higher across the sky"; directionSource = "familyFallback"; fallback = true; }
                return Pack(family, null, string.Empty, LocalizeWindow(humanWindow, language), LocalizeDirection(cleanDirection, language), directionSource, windowSource, fallback, "The Moon is most dramatic near moonrise.", null, null);
            }

            if (family == "SolarEclipse")
            {
                humanWindow = IsHindi(language) ? "यदि आपके स्थान से दिखाई दे, तो अधिकतम ग्रहण के आसपास" : "around maximum eclipse, if visible from your location";
                cleanDirection = IsHindi(language) ? "केवल प्रमाणित सोलर ग्रहण चश्मे के साथ सूर्य की ओर" : "toward the Sun only with certified solar eclipse glasses";
                directionSource = string.IsNullOrWhiteSpace(metadata.Direction) ? "familyFallback" : "eventSpecificOverride";
                windowSource = string.IsNullOrWhiteSpace(metadata.ViewingWindow) ? "familyFallback" : "eventSpecificOverride";
                safety = IsHindi(language) ? "प्रमाणित सोलर ग्रहण चश्मा" : "certified solar eclipse glasses";
                return Pack(family, null, string.Empty, humanWindow, cleanDirection, directionSource, windowSource, true, "Track maximum eclipse safely when local timings are available.", null, safety);
            }

            return Pack(family, null, string.Empty, LocalizeWindow(humanWindow, language), LocalizeDirection(cleanDirection, language), directionSource, windowSource, fallback, string.Empty, null, null);
        }

        private static ObservationContext Pack(string family, string? geometricPeak, string viewerBestTime, string window, string direction, string directionSource, string windowSource, bool fallbackUsed, string timingNote, string? pair, string? safety)
            => new(window, direction, timingNote, geometricPeak, viewerBestTime, pair, safety, new(family, geometricPeak, viewerBestTime, window, direction, directionSource, windowSource, fallbackUsed));

        private static string LocalizeWindow(string value, string language) => IsHindi(language) ? FormatHindiObservation(value) : value;
        private static string LocalizeDirection(string value, string language) => IsHindi(language) ? FormatHindiObservation(value) : value;

        private static string HindiPlanetName(string name) => name.ToLowerInvariant() switch { "mars" => "मंगल", "jupiter" => "बृहस्पति", "venus" => "शुक्र", "mercury" => "बुध", "saturn" => "शनि", "moon" => "चंद्रमा", "sun" => "सूर्य", _ => name };

        private static string CleanDirection(string direction) => IsOpenSky(direction) ? "toward the open sky" : Regex.Replace(direction.Trim(), @"^toward\s+", string.Empty, RegexOptions.IgnoreCase);
        private static bool IsOpenSky(string? direction) => string.IsNullOrWhiteSpace(direction) || direction.Contains("open sky", StringComparison.OrdinalIgnoreCase);

        private static string DeriveConjunctionDirection(string title, string peak, string? rawDirection)
        {
            var text = $"{rawDirection} {peak}";
            if (Regex.IsMatch(text, @"\bSE\b|south[- ]?east|southeast", RegexOptions.IgnoreCase)) return "the southeastern sky before sunrise";
            if (Regex.IsMatch(text, "sunrise|dawn|morning|AM", RegexOptions.IgnoreCase)) return "the eastern horizon before sunrise";
            if (Regex.IsMatch(text, "sunset|evening|PM|west", RegexOptions.IgnoreCase)) return "low in the western sky after sunset";
            return title.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) && title.Contains("Venus", StringComparison.OrdinalIgnoreCase) ? "the eastern horizon before sunrise" : "low near the horizon during twilight";
        }

        private static string DeriveConjunctionWindowPhrase(string peak, string direction)
        {
            var text = $"{peak} {direction}";
            if (Regex.IsMatch(text, "sunrise|dawn|morning|AM|east", RegexOptions.IgnoreCase)) return "before sunrise";
            if (Regex.IsMatch(text, "sunset|evening|PM|west", RegexOptions.IgnoreCase)) return "shortly after sunset";
            return "early evening";
        }

        private sealed record ObservationContext(string Window, string Direction, string TimingNote, string? GeometricPeakTime, string ViewerBestTime, string? DisplayObjectPair, string? SafetyNote, ObservationContextDiagnostics Diagnostics);

        private static string BuildDisplayTitle(string family, string rawName, string eventType)
        {
            if (family == "PlanetConjunction")
            {
                var planets = ExtractObjects(rawName).Take(2).ToArray();
                if (planets.Length >= 2) return $"the {planets[0]}–{planets[1]} conjunction";
            }
            if (family == "PlanetGrouping") return Regex.Replace(rawName, @"\s+(?:planet\s+)?grouping.*$", " planet grouping", RegexOptions.IgnoreCase).Trim();
            if (family == "MeteorShower")
            {
                if (rawName.Contains("Geminid", StringComparison.OrdinalIgnoreCase)) return "the Geminids meteor shower";
                if (rawName.Contains("Perseid", StringComparison.OrdinalIgnoreCase)) return "Perseids Meteor Shower";
                return Regex.Replace(rawName, @"\s+Peak\b.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            }
            if (family == "NamedFullMoon")
            {
                var match = Regex.Match(rawName, @"\b([A-Z][a-z]+)\s+Moon\b");
                if (match.Success) return $"the {match.Groups[1].Value} Moon";
            }
            if (family == "SolarEclipse")
            {
                if (rawName.Contains("total", StringComparison.OrdinalIgnoreCase) || eventType.Contains("total", StringComparison.OrdinalIgnoreCase)) return "the total solar eclipse";
                if (rawName.Contains("annular", StringComparison.OrdinalIgnoreCase)) return "Annular Solar Eclipse";
                if (rawName.Contains("partial", StringComparison.OrdinalIgnoreCase)) return "Partial Solar Eclipse";
                return "Solar Eclipse";
            }

            var cleaned = Regex.Replace(rawName, @"\s+(?:on|for|in)\s+\d{4}[-\s].*$", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s*[-–—]\s*(?:\d{4}.*|[A-Z]{2}-[A-Z]{2}-[A-Z0-9-]+|Udaipur.*)$", string.Empty, RegexOptions.IgnoreCase);
            return string.IsNullOrWhiteSpace(cleaned) ? "Astronomy Event" : cleaned.Trim();
        }

        private static string ShortDisplayTitle(string family, string displayTitle)
            => family == "MeteorShower" ? Regex.Replace(displayTitle, @"\s+Meteor\s+Shower$", string.Empty, RegexOptions.IgnoreCase) : displayTitle;

        private static string ScientificSummaryFor(string family, string title)
            => family switch
            {
                "PlanetConjunction" => $"{title} is an apparent close pairing in our line of sight, not a physical meeting in space.",
                "PlanetGrouping" => $"{title} is an apparent grouping in our line of sight, not a physical meeting in space.",
                "MeteorShower" => $"{title} happens when Earth crosses a stream of small particles that burn brightly in the atmosphere.",
                "SolarEclipse" => $"{title} occurs when the Moon passes between Earth and the Sun.",
                "NamedFullMoon" => $"{title} is a seasonal full Moon with a name rooted in observing traditions.",
                _ => $"{title} has specific timing and viewing conditions."
            };

        private static string InterestingFactFor(string family, string title, ObservationContext observation)
        {
            if (family == "MeteorShower")
            {
                var intelligence = MeteorShowerIntelligence.For(title, title);
                if (!string.IsNullOrWhiteSpace(intelligence.ParentBody) && !string.IsNullOrWhiteSpace(intelligence.Radiant))
                    return $"The {intelligence.ShortTitle} come from debris left by {intelligence.ParentBody}, and the meteors appear to radiate from {intelligence.Radiant}.";
                return $"{title} is event-specific because its meteors trace back to this shower's own debris stream; use {observation.Window} and look {observation.Direction} to connect the fact to this event.";
            }
            if (family is "PlanetConjunction" or "PlanetGrouping")
            {
                var objects = ExtractObjects(title).Where(o => !o.Equals("Sky", StringComparison.OrdinalIgnoreCase)).ToArray();
                return objects.Length >= 2
                    ? $"{objects[0]} and {objects[1]} only appear close from Earth's line of sight; the planets remain far apart in space while their changing positions create the conjunction."
                    : $"{title} is a line-of-sight pairing, so its appearance depends on the planets' brightness, color, separation, and twilight timing.";
            }
            if (family == "NamedFullMoon") return $"{title} carries a seasonal observing name, tying this full Moon to local traditions, weather, wildlife, harvests, or other calendar markers.";
            if (family == "SolarEclipse")
            {
                var variant = Regex.Match(title, @"\b(total|partial|annular|hybrid)\b", RegexOptions.IgnoreCase).Value.ToLowerInvariant();
                return variant switch
                {
                    "partial" => "In a partial solar eclipse, the Moon covers only part of the Sun, so certified solar filters are required for the entire event.",
                    "annular" => "In an annular solar eclipse, the Moon appears too small to cover the Sun fully, leaving a bright ring that still requires certified solar filters.",
                    "hybrid" => "A hybrid solar eclipse shifts between annular and total along different parts of its path, making local eclipse geometry especially important.",
                    _ => "During a total solar eclipse, the Moon can reveal the Sun's faint corona, but safe eye protection is essential outside totality."
                };
            }
            return ScientificSummaryFor(family, title);
        }

        private static string HumanizeWindow(string window)
        {
            var cleaned = Regex.Replace(window, @"\b(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+\d{4}\s+", string.Empty);
            return Regex.Replace(cleaned, @"\b(?<h1>\d{1,2}):(?<m1>\d{2})\s*[–-]\s*(?<h2>\d{1,2}):(?<m2>\d{2})(?<suffix>\s*[A-Z]{2,4})?\b", m =>
            {
                var start = FormatClock(int.Parse(m.Groups["h1"].Value), m.Groups["m1"].Value);
                var end = FormatClock(int.Parse(m.Groups["h2"].Value), m.Groups["m2"].Value);
                return $"from {start} to {end}{m.Groups["suffix"].Value}";
            });
        }

        private static string FormatClock(int hour, string minutes)
        {
            var suffix = hour >= 12 ? "PM" : "AM";
            var displayHour = hour % 12;
            if (displayHour == 0) displayHour = 12;
            return $"{displayHour}:{minutes} {suffix}";
        }
    }

    private static JsonElement? BuildPlanMetadata(AstronomyEventIntelligence intelligence)
    {
        var values = new Dictionary<string, string?>
        {
            ["eventDate"] = FirstNonEmpty(ReadEventJsonString(intelligence, "eventDate", "EventDate", "localDate"), intelligence.PeakUtc?.ToString("yyyy-MM-dd"), intelligence.StartUtc.ToString("yyyy-MM-dd")),
            ["localPeakTime"] = FirstNonEmpty(ReadEventJsonString(intelligence, "localPeakTime", "LocalPeakTime", "peakTime"), intelligence.PeakUtc?.ToString("yyyy-MM-dd HH:mm zzz")),
            ["peakUtc"] = intelligence.PeakUtc?.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture),
            ["bestViewingWindowLocal"] = ReadEventJsonString(intelligence, "bestViewingWindowLocal", "BestViewingWindowLocal", "bestViewingWindow", "viewingWindow"),
            ["direction"] = ReadEventJsonString(intelligence, "skyDirectionHint", "SkyDirectionHint", "direction"),
            ["moonInterference"] = ReadEventJsonString(intelligence, "moonInterference", "MoonInterference")
        };
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value)));
        return doc.RootElement.Clone();
    }

    private static string? ReadEventJsonString(AstronomyEventIntelligence intelligence, params string[] names)
        => FirstNonEmpty(ReadJsonString(intelligence.MetadataJson, names), ReadJsonString(intelligence.RawDataJson, names));

    private static string? ReadJsonString(string? json, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var name in names)
                if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
                    return value.ToString();
        }
        catch (JsonException) { }
        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
    private sealed record HydratedNarrationRequest(NarrationPreviewRequest Request, NarrationPlanHydrationDiagnostics? Diagnostics);

    private sealed record Metadata(string? EventDate, string? PeakDate, string? PeakTime, string? LocalPeakTime, string? PeakUtc, string? ViewingWindow, string? Direction)
    {
        public static Metadata From(JsonElement? element)
        {
            if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object) return new(null, null, null, null, null, null, null);
            var e = element.Value;
            string? Get(params string[] names) => names.Select(n => e.TryGetProperty(n, out var p) ? p.ToString() : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            return new(Get("eventDate", "date", "localDate"), Get("peakDate"), Get("peakTime", "localPeakTime"), Get("localPeakTime"), Get("peakUtc", "peakUTC"), Get("viewingWindow", "bestViewingWindow", "bestViewingWindowLocal", "visibilityWindow", "localObservationWindow", "observationWindow"), Get("direction", "skyDirectionHint", "observationDirection", "visibilityDirection"));
        }
    }
}
