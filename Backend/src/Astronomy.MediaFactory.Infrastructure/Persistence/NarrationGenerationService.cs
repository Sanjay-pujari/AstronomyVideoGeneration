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
        => Generate(await HydrateAsync(request, cancellationToken));

    public async Task<NarrationPreviewResponse> GenerateProductionNarrationAsync(NarrationPreviewRequest request, CancellationToken cancellationToken)
        => Generate(await HydrateAsync(request, cancellationToken));

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

    private NarrationPreviewResponse Generate(HydratedNarrationRequest hydratedRequest)
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
        var fact = EventFact(eventType, eventName, language);
        var scenes = new[]
        {
            Scene("hook", "Hook", Hook(eventName, eventType, date, fact, language)),
            Scene("interesting-fact", "InterestingFact", InterestingFact(eventName, fact, language)),
            Scene("best-time", "BestTime", BestTime(eventName, window, peak, direction, language)),
            Scene("final-reminder", "FinalReminder", FinalReminder(eventName, language))
        };
        var validated = scenes.Select(s => s with { Validation = ValidateScene(s.ScenePurpose, s.Narration, eventName, language) }).ToArray();
        var errors = validated.SelectMany(s => s.Validation.Errors).ToList();
        var warnings = validated.SelectMany(s => s.Validation.Warnings).ToList();
        if (validated.Select(s => NormalizeSentence(s.Narration)).GroupBy(s => s).Any(g => g.Count() > 1)) errors.Add("Duplicate sentence appears in narration.");
        var overall = new NarrationValidationResult(errors.Count == 0, errors, warnings);
        var diagnostics = new NarrationFormattingDiagnostics(date, peak, window, direction,
            ["FormatEventDate(language)", "FormatPeakTime(language)", "FormatViewingWindow(language)", "FormatDirection(language)", "No SRT/TTS/video/Phase14 execution"], []);
        return new NarrationPreviewResponse(request.PlanId, eventType, eventName, language, regionId, request.Format, request.ReturnScenes ? validated : [], overall, diagnostics, Clean(request.ShortTitle, null!), hydratedRequest.Diagnostics);
    }

    private static NarrationPreviewScene Scene(string id, string purpose, string narration) => new(id, purpose, narration, new(true, [], []));

    private static string Clean(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Hook(string name, string type, string date, string fact, string lang) => IsHindi(lang)
        ? $"{date} को {HindiName(name)} देखने लायक है, क्योंकि {HindiFact(name, fact)}—और यही छोटी-सी बात इसे आसमान में खोजने की जिज्ञासा जगाती है।"
        : $"On {date}, {name} matters because {fact}, and that gives you a real reason to step outside and look up.";

    private static string InterestingFact(string name, string fact, string lang) => IsHindi(lang)
        ? $"रोचक तथ्य यह है कि {HindiFact(name, fact)}"
        : $"Interesting fact: {fact}.";

    private static string BestTime(string name, string window, string peak, string direction, string lang) => IsHindi(lang)
        ? $"सबसे अच्छा समय {window} है; {direction} देखें, और रोशनी से दूर थोड़ी देर आंखों को अंधेरे में ढलने दें।"
        : $"Best time: watch {window}; use {peak} as your peak-time cue, face {direction}, and give your eyes time to adjust.";

    private static string FinalReminder(string name, string lang) => IsHindi(lang)
        ? $"अगर आसमान साफ हो, तो यह समय याद रखें, परिवार के साथ बाहर निकलें, और अगली खगोलीय झलक के लिए तैयार रहें।"
        : $"If your sky is clear, save this reminder, share the moment with family, and come back for the next sky event.";

    private static string EventFact(string type, string name, string lang)
    {
        var n = name.ToLowerInvariant(); var t = type.ToLowerInvariant();
        if (n.Contains("geminid")) return "the Geminids come from debris linked to asteroid 3200 Phaethon, not a typical comet";
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

    private static NarrationValidationResult ValidateScene(string purpose, string text, string eventName, string language)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        if (Regex.IsMatch(text, @"\b\d{4}-\d{2}-\d{2}\b")) errors.Add("Raw ISO date appears.");
        if (Regex.IsMatch(text, @"[+-]\d{2}:\d{2}")) errors.Add("Timezone offset appears.");
        if (Regex.IsMatch(text, "placeholder|listed viewing window|local viewing window|during December", RegexOptions.IgnoreCase)) errors.Add("Placeholder or forbidden phrase appears.");
        if (text.Length > 0 && char.IsLower(text[0])) errors.Add("Scene narration starts with lowercase letter.");
        if (purpose == "Hook" && !Regex.IsMatch(text, @"\b(?:January|February|March|April|May|June|July|August|September|October|November|December|जनवरी|फ़रवरी|मार्च|अप्रैल|मई|जून|जुलाई|अगस्त|सितंबर|अक्टूबर|नवंबर|दिसंबर)\b")) errors.Add("Hook lacks exact date.");
        if (purpose == "BestTime" && (Regex.IsMatch(text, @"\b(metadata|window unavailable|not available)\b", RegexOptions.IgnoreCase) || !Regex.IsMatch(text, @"\b(AM|PM|midnight|सुबह|शाम|रात|बजे)\b"))) errors.Add("BestTime lacks real formatted window.");
        if (purpose == "InterestingFact" && !ContainsSpecificFact(text, eventName)) errors.Add("InterestingFact lacks event-specific fact.");
        if (IsHindi(language) && Regex.IsMatch(text, @"\b(December|from|midnight|eastern|sky|toward|overhead|viewing|window|meteor shower|comet|asteroid|typical|traditions|winter|family)\b", RegexOptions.IgnoreCase)) errors.Add("Hindi contains English leakage outside approved proper nouns.");
        return new(errors.Count == 0, errors, warnings);
    }

    private static bool ContainsSpecificFact(string text, string eventName) => text.Contains("3200 Phaethon", StringComparison.OrdinalIgnoreCase) || text.Contains("Swift-Tuttle", StringComparison.OrdinalIgnoreCase) || text.Contains("strawberr", StringComparison.OrdinalIgnoreCase) || text.Contains("wolf", StringComparison.OrdinalIgnoreCase) || text.Contains("brightest planets", StringComparison.OrdinalIgnoreCase) || text.Contains("रंग", StringComparison.OrdinalIgnoreCase) || text.Contains("मौसमी", StringComparison.OrdinalIgnoreCase);
    private static string HindiName(string name) => name.Contains("Geminid", StringComparison.OrdinalIgnoreCase) ? "जेमिनिड्स (Geminids)" : name.Contains("Perseid", StringComparison.OrdinalIgnoreCase) ? "पर्सिड्स (Perseids)" : name.Replace("Moon", "मून", StringComparison.OrdinalIgnoreCase).Replace("Conjunction", "संयोग", StringComparison.OrdinalIgnoreCase);
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
            return new(Get("eventDate", "date", "localDate"), Get("peakDate"), Get("peakTime", "localPeakTime"), Get("viewingWindow", "bestViewingWindow", "bestViewingWindowLocal"), Get("direction", "skyDirectionHint"));
        }
    }
}
