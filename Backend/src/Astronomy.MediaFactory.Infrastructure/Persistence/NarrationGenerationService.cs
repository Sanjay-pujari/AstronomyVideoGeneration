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
        var hydrated = request with
        {
            EventType = Clean(intelligence.EventType, Clean(plan.PrimaryAstronomyEventTypeCode, request.EventType)),
            EventName = Clean(intelligence.Title, Clean(plan.Title, request.EventName)),
            ShortTitle = FirstNonEmpty(ReadEventJsonString(intelligence, "shortTitle", "ShortTitle"), intelligence.Summary, request.ShortTitle),
            Language = Clean(plan.Language, Clean(intelligence.Language, request.Language)),
            RegionId = Clean(plan.RegionId, Clean(intelligence.RegionId, request.RegionId)),
            Format = Clean(plan.PlannedFormat, request.Format),
            EventMetadata = metadata
        };
        return new(hydrated, new(request.PlanId, true, true, false, hydrated.EventType, hydrated.EventName, hydrated.RegionId));
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
        var date = formatter.FormatEventDate(metadata.EventDate ?? metadata.PeakDate, language);
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
        var validated = scenes.Select(s => s with { Validation = ValidateScene(s.ScenePurpose, s.Narration, eventName, language, request.ShortTitle, context) }).ToArray();
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
        if (IsHindi(context.Language))
            return $"{HindiRegion(context.DisplayLocation)} में देखने का सुझाया समय {context.DisplayDate} की {context.ObservationWindow} है। {NormalizeDirectionForSentence(context.ObservationDirection)} देखें।";
        if (context.Family == "PlanetConjunction")
            return context.ViewerBestTime;
        if (context.Family == "SolarEclipse" && !string.IsNullOrWhiteSpace(context.SafetyNote))
            return $"For observers in {context.DisplayLocation}, the eclipse viewing window runs {context.ObservationWindow} on {context.DisplayDate}. Look toward {NormalizeDirectionForSentence(context.ObservationDirection)} only with {context.SafetyNote}.";
        return $"For observers in {context.DisplayLocation}, the recommended viewing window runs {context.ObservationWindow} on {context.DisplayDate}. Look toward {NormalizeDirectionForSentence(context.ObservationDirection)} as the night deepens.";
    }

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

    private static NarrationValidationResult ValidateScene(string purpose, string text, string eventName, string language, string? rawShortTitle, NarrationContext context)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        if (Regex.IsMatch(text, @"\b\d{4}-\d{2}-\d{2}\b")) errors.Add("Raw ISO date appears.");
        if (Regex.IsMatch(text, @"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?Z?)?\b")) errors.Add("Raw ISO date or UTC timestamp appears.");
        if (Regex.IsMatch(text, @"\bUTC\b", RegexOptions.IgnoreCase)) errors.Add("UTC timestamp appears.");
        if (Regex.IsMatch(text, @"peaks\s+2026|minimum angular separation|consolidated from", RegexOptions.IgnoreCase)) errors.Add("Raw production metadata phrase appears.");
        if (Regex.IsMatch(text, @"\b[A-Z]{2}-[A-Z]{2}-[A-Z0-9-]+\b")) errors.Add("Raw region code appears.");
        if (!string.IsNullOrWhiteSpace(rawShortTitle) && !string.Equals(rawShortTitle, context.ShortDisplayTitle, StringComparison.OrdinalIgnoreCase) && text.Contains(rawShortTitle, StringComparison.OrdinalIgnoreCase)) errors.Add("Raw short title appears.");
        if (!string.IsNullOrWhiteSpace(eventName) && !string.Equals(eventName, context.DisplayTitle, StringComparison.OrdinalIgnoreCase) && text.Contains(eventName, StringComparison.OrdinalIgnoreCase)) errors.Add("Raw internal event title appears.");
        if (Regex.IsMatch(text, @"[+-]\d{2}:\d{2}")) errors.Add("Timezone offset appears.");
        if (Regex.IsMatch(text, "placeholder|listed viewing window|local viewing window|during December", RegexOptions.IgnoreCase)) errors.Add("Placeholder or forbidden phrase appears.");
        if (text.Length > 0 && char.IsLower(text[0])) errors.Add("Scene narration starts with lowercase letter.");
        if (purpose == "Hook" && !Regex.IsMatch(text, @"\b(?:January|February|March|April|May|June|July|August|September|October|November|December|जनवरी|फ़रवरी|मार्च|अप्रैल|मई|जून|जुलाई|अगस्त|सितंबर|अक्टूबर|नवंबर|दिसंबर)\b")) errors.Add("Hook lacks exact date.");
        if (Regex.IsMatch(text, @"^\s*(Interesting fact|Best time):", RegexOptions.IgnoreCase)) errors.Add("Scene starts with a forbidden label.");
        if (Regex.IsMatch(text, @"\b" + Regex.Escape(eventName) + @"\s+matters because", RegexOptions.IgnoreCase)) errors.Add("Scene uses awkward full event title phrasing.");
        if (purpose == "BestTime" && (Regex.IsMatch(text, @"\b(metadata|window unavailable|not available)\b", RegexOptions.IgnoreCase) || !Regex.IsMatch(text, @"\b(AM|PM|midnight|sunrise|sunset|twilight|evening|dawn|सुबह|शाम|रात|बजे)\b", RegexOptions.IgnoreCase))) errors.Add("BestTime lacks real formatted window.");
        if (purpose == "BestTime" && Regex.IsMatch(text, @"\b\d{1,2}:\d{2}\s*[–-]\s*\d{1,2}:\d{2}\b")) errors.Add("BestTime contains raw numeric time range.");
        if (purpose == "BestTime" && context.Family == "PlanetConjunction" && Regex.IsMatch(text.Trim(), @"^(?:.*\s)?\d{1,2}:\d{2}(?:\s*(?:AM|PM|IST))?\.?$", RegexOptions.IgnoreCase)) errors.Add("Planet Conjunction BestTime uses only a raw time.");
        if (purpose == "BestTime" && !Regex.IsMatch(text, @"\b(toward|horizon|sky|above the horizon|overhead|moonrise|sunrise|sunset|दिशा|ओर|आकाश|सिर के ऊपर)\b", RegexOptions.IgnoreCase)) errors.Add("BestTime lacks viewer-useful direction.");
        if (Regex.IsMatch(text, @"toward the open sky", RegexOptions.IgnoreCase)) errors.Add("Forbidden generic direction appears.");
        if (purpose == "BestTime" && Regex.IsMatch(text, @"use\s+.+?\s+as your peak-time cue", RegexOptions.IgnoreCase)) errors.Add("BestTime contains peak-time cue phrasing.");
        if (purpose == "FinalReminder" && text.Contains("come back for the next sky event", StringComparison.OrdinalIgnoreCase)) errors.Add("FinalReminder is generic.");
        if (purpose == "InterestingFact" && !ContainsSpecificFact(text, eventName)) errors.Add("InterestingFact lacks event-specific fact.");
        if (IsHindi(language) && Regex.IsMatch(text, @"\b(December|from|midnight|eastern|sky|toward|overhead|viewing|window|meteor shower|comet|asteroid|typical|traditions|winter|family)\b|\s+to\s+|after|before|PM|AM|East", RegexOptions.IgnoreCase)) errors.Add("Hindi contains English leakage outside approved proper nouns.");
        if (IsHindi(language) && Regex.IsMatch(text, @"(?:पूर्व|पूर्वी|पश्चिम|सिर के ऊपर).*(?:\s+to\s+|after|before|PM|AM|East|overhead)|(?:\s+to\s+|after|before|PM|AM|East|overhead).*(?:पूर्व|पूर्वी|पश्चिम|सिर के ऊपर)", RegexOptions.IgnoreCase)) errors.Add("Hindi contains mixed Hindi-English direction phrasing.");
        return new(errors.Count == 0, errors, warnings);
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
        if (text.Contains("conjunction", StringComparison.OrdinalIgnoreCase)) return "PlanetConjunction";
        if (text.Contains("solar", StringComparison.OrdinalIgnoreCase) && text.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "SolarEclipse";
        if (text.Contains("moon", StringComparison.OrdinalIgnoreCase)) return "NamedFullMoon";
        return "AstronomyEvent";
    }

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

    private static bool ShareSameFactPhrase(string hook, string fact)
    {
        var factPhrases = new[] { "3200 Phaethon", "Swift-Tuttle", "debris", "traditional comet", "typical comet", "brightest planets", "seasonal traditions" };
        return factPhrases.Any(phrase => hook.Contains(phrase, StringComparison.OrdinalIgnoreCase) && fact.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }
    private static bool ContainsSpecificFact(string text, string eventName) => text.Contains("3200 Phaethon", StringComparison.OrdinalIgnoreCase) || text.Contains("Swift-Tuttle", StringComparison.OrdinalIgnoreCase) || text.Contains("strawberr", StringComparison.OrdinalIgnoreCase) || text.Contains("wolf", StringComparison.OrdinalIgnoreCase) || text.Contains("brightest planets", StringComparison.OrdinalIgnoreCase) || text.Contains("Harvest Moon", StringComparison.OrdinalIgnoreCase) || text.Contains("farmers", StringComparison.OrdinalIgnoreCase) || text.Contains("रंग", StringComparison.OrdinalIgnoreCase) || text.Contains("मौसमी", StringComparison.OrdinalIgnoreCase);
    private static string HindiName(string name) => name.Contains("Geminid", StringComparison.OrdinalIgnoreCase) ? "जेमिनिड्स (Geminids)" : name.Contains("Perseid", StringComparison.OrdinalIgnoreCase) ? "पर्सिड्स (Perseids)" : name.Replace("Moon", "मून", StringComparison.OrdinalIgnoreCase).Replace("Conjunction", "संयोग", StringComparison.OrdinalIgnoreCase);
    private static string HindiShortName(string name) => name.Contains("Geminid", StringComparison.OrdinalIgnoreCase) ? "जेमिनिड्स" : name.Contains("Perseid", StringComparison.OrdinalIgnoreCase) ? "पर्सिड्स" : HindiName(name);
    private static string HindiFact(string name, string fact)
    {
        if (name.Contains("Geminid", StringComparison.OrdinalIgnoreCase)) return "जेमिनिड्स का संबंध सामान्य धूमकेतु से नहीं, क्षुद्रग्रह 3200 Phaethon से जुड़े मलबे से है";
        if (name.Contains("Perseid", StringComparison.OrdinalIgnoreCase)) return "पर्सिड्स धूमकेतु Swift-Tuttle के छोड़े मलबे से बनते हैं";
        if (name.Contains("Strawberry", StringComparison.OrdinalIgnoreCase)) return "स्ट्रॉबेरी मून का नाम जून में स्ट्रॉबेरी पकने की मौसमी परंपरा से जुड़ा है";
        if (name.Contains("Wolf", StringComparison.OrdinalIgnoreCase)) return "वुल्फ मून का नाम सर्दियों और भेड़ियों से जुड़ी लोक परंपरा से आता है";
        if (name.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) && name.Contains("Venus", StringComparison.OrdinalIgnoreCase)) return "बृहस्पति और शुक्र दो बेहद चमकीले ग्रह हैं, इसलिए उनका पास दिखना खास लगता है";
        return "इस घटना की अपनी खास समय-रेखा और देखने की दिशा है";
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
            var objects = family == "PlanetConjunction" && displayTitle.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) && displayTitle.Contains("Venus", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Jupiter", "Venus" }
                : ExtractObjects(displayTitle);

            var observation = BuildObservationContext(family, displayTitle, location, date, peak, window, direction, metadata);

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
                InterestingFactFor(family, displayTitle),
                HistoricalContextFor(family, displayTitle),
                RarityContextFor(family, displayTitle),
                language,
                observation.Diagnostics);
        }


        private static ObservationContext BuildObservationContext(string family, string title, string location, string date, string peak, string window, string direction, Metadata metadata)
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

            if (family == "PlanetConjunction")
            {
                pair = title.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) && title.Contains("Venus", StringComparison.OrdinalIgnoreCase) ? "Jupiter and Venus" : null;
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
                var article = title.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) && title.Contains("Venus", StringComparison.OrdinalIgnoreCase) ? "this conjunction" : "the conjunction";
                var viewer = !string.IsNullOrWhiteSpace(metadata.ViewingWindow)
                    ? $"For observers in {location}, the best viewing window runs {humanWindow}. Look toward {NormalizeDirectionForSentence(cleanDirection)} while both planets are above the horizon."
                    : $"For observers in {location}, the best chance to see {article} is during the darker twilight window when Jupiter and Venus are both above the horizon. Look toward {NormalizeDirectionForSentence(cleanDirection)}, and use a clear, low view for the best result.";
                return Pack(family, geometricPeak, viewer, humanWindow, cleanDirection, directionSource, windowSource, fallback, timingNote, pair, safety);
            }

            if (family == "MeteorShower")
            {
                if (IsOpenSky(cleanDirection)) { cleanDirection = "the eastern sky toward overhead after 10 PM"; directionSource = "familyFallback"; fallback = true; }
                return Pack(family, null, string.Empty, humanWindow, cleanDirection, directionSource, windowSource, fallback, "Give your eyes time to adapt to darkness.", null, null);
            }

            if (family == "NamedFullMoon")
            {
                if (string.IsNullOrWhiteSpace(metadata.ViewingWindow)) { humanWindow = "after moonrise and throughout the night"; windowSource = "familyFallback"; fallback = true; }
                if (IsOpenSky(cleanDirection)) { cleanDirection = "the eastern horizon at moonrise, then higher across the sky"; directionSource = "familyFallback"; fallback = true; }
                return Pack(family, null, string.Empty, humanWindow, cleanDirection, directionSource, windowSource, fallback, "The Moon is most dramatic near moonrise.", null, null);
            }

            if (family == "SolarEclipse")
            {
                if (IsOpenSky(cleanDirection)) { cleanDirection = "the Sun's position in the sky"; directionSource = "familyFallback"; fallback = true; }
                safety = "certified solar eclipse glasses required except during verified totality";
                return Pack(family, null, string.Empty, humanWindow, cleanDirection, directionSource, windowSource, fallback, "Track first contact, maximum eclipse, and end when local timings are available.", null, safety);
            }

            return Pack(family, null, string.Empty, humanWindow, cleanDirection, directionSource, windowSource, fallback, string.Empty, null, null);
        }

        private static ObservationContext Pack(string family, string? geometricPeak, string viewerBestTime, string window, string direction, string directionSource, string windowSource, bool fallbackUsed, string timingNote, string? pair, string? safety)
            => new(window, direction, timingNote, geometricPeak, viewerBestTime, pair, safety, new(family, geometricPeak, viewerBestTime, window, direction, directionSource, windowSource, fallbackUsed));

        private static string CleanDirection(string direction) => IsOpenSky(direction) ? "toward the open sky" : Regex.Replace(direction.Trim(), @"^toward\s+", string.Empty, RegexOptions.IgnoreCase);
        private static bool IsOpenSky(string? direction) => string.IsNullOrWhiteSpace(direction) || direction.Contains("open sky", StringComparison.OrdinalIgnoreCase);

        private static string DeriveConjunctionDirection(string title, string peak, string? rawDirection)
        {
            var text = $"{rawDirection} {peak}";
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
            if (family == "PlanetConjunction" && rawName.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) && rawName.Contains("Venus", StringComparison.OrdinalIgnoreCase))
                return "Jupiter and Venus Conjunction";
            if (family == "MeteorShower")
            {
                if (rawName.Contains("Geminid", StringComparison.OrdinalIgnoreCase)) return "Geminids Meteor Shower";
                if (rawName.Contains("Perseid", StringComparison.OrdinalIgnoreCase)) return "Perseids Meteor Shower";
                return Regex.Replace(rawName, @"\s+Peak\b.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            }
            if (family == "NamedFullMoon")
            {
                var match = Regex.Match(rawName, @"\b([A-Z][a-z]+)\s+Moon\b");
                if (match.Success) return $"{match.Groups[1].Value} Moon";
            }
            if (family == "SolarEclipse")
            {
                if (rawName.Contains("total", StringComparison.OrdinalIgnoreCase) || eventType.Contains("total", StringComparison.OrdinalIgnoreCase)) return "Total Solar Eclipse";
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
                "MeteorShower" => $"{title} happens when Earth crosses a stream of small particles that burn brightly in the atmosphere.",
                "SolarEclipse" => $"{title} occurs when the Moon passes between Earth and the Sun.",
                "NamedFullMoon" => $"{title} is a seasonal full Moon with a name rooted in observing traditions.",
                _ => $"{title} has specific timing and viewing conditions."
            };

        private static string InterestingFactFor(string family, string title)
        {
            if (family == "MeteorShower" && title.Contains("Geminids", StringComparison.OrdinalIgnoreCase))
                return "Unlike most major meteor showers, the Geminids come from asteroid 3200 Phaethon rather than a traditional comet.";
            if (family == "PlanetConjunction") return "Jupiter and Venus are the two brightest planets, so their close pairing after twilight is especially striking.";
            if (family == "NamedFullMoon" && title.Contains("Strawberry", StringComparison.OrdinalIgnoreCase)) return "The Strawberry Moon name comes from June traditions connected with ripening strawberries, not from the Moon turning pink.";
            if (family == "NamedFullMoon" && title.Contains("Wolf", StringComparison.OrdinalIgnoreCase)) return "The Wolf Moon name is linked with deep winter nights and wolf folklore.";
            if (family == "NamedFullMoon" && title.Contains("Harvest", StringComparison.OrdinalIgnoreCase)) return "The Harvest Moon is known for rising near sunset for several nights, historically helping farmers extend evening work.";
            if (family == "SolarEclipse") return "During a total solar eclipse, the Moon can reveal the Sun's faint corona, but safe eye protection is essential outside totality.";
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

    private sealed record Metadata(string? EventDate, string? PeakDate, string? PeakTime, string? ViewingWindow, string? Direction)
    {
        public static Metadata From(JsonElement? element)
        {
            if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object) return new(null, null, null, null, null);
            var e = element.Value;
            string? Get(params string[] names) => names.Select(n => e.TryGetProperty(n, out var p) ? p.ToString() : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            return new(Get("eventDate", "date", "localDate"), Get("peakDate"), Get("peakTime", "localPeakTime"), Get("viewingWindow", "bestViewingWindow", "bestViewingWindowLocal", "visibilityWindow", "localObservationWindow", "observationWindow"), Get("direction", "skyDirectionHint", "observationDirection", "visibilityDirection"));
        }
    }
}
