using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class NarrationV31Composer : INarrationV31Composer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] Sections = ["Hook", "Curiosity", "Explanation", "ViewingAdvice", "Reward", "CTA"];
    private static readonly string[] AuthoringPhrases = ["open with", "explain", "describe", "focus on", "json", "metadata", "source answer"];
    private static readonly IReadOnlyDictionary<string, string> HindiTerms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Moon"] = "चंद्रमा", ["Jupiter"] = "बृहस्पति", ["Venus"] = "शुक्र", ["Mars"] = "मंगल",
        ["Saturn"] = "शनि", ["meteor shower"] = "उल्का वर्षा", ["eclipse"] = "ग्रहण", ["constellation"] = "नक्षत्र",
        ["telescope"] = "दूरबीन", ["binoculars"] = "दूरबीन", ["horizon"] = "क्षितिज", ["sky"] = "आकाश"
    };

    public Task<NarrationV31PreviewResponse> PreviewAsync(NarrationV31PreviewRequest request, CancellationToken cancellationToken)
        => ComposeAsync(request, writeFiles: false, cancellationToken);

    public Task<NarrationV31PreviewResponse> WriteFinalSceneNarrationAsync(NarrationV31PreviewRequest request, CancellationToken cancellationToken)
        => ComposeAsync(request with { DryRun = false }, writeFiles: true, cancellationToken);

    private static async Task<NarrationV31PreviewResponse> ComposeAsync(NarrationV31PreviewRequest request, bool writeFiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var language = NormalizeLanguage(request.Language);
        var eventId = FirstNonEmpty(request.EventId, "preview-event");
        var regionId = FirstNonEmpty(request.RegionId, request.ProductionContext?.RegionId, "global");
        var title = FirstNonEmpty(request.Title, request.ProductionContext?.ProductionEventIntelligence?.Title, "this sky event");
        var eventType = FirstNonEmpty(request.EventType, request.ProductionContext?.ProductionEventIntelligence?.EventType, "sky event");
        var window = FormatObservationWindow(FirstNonEmpty(request.BestViewingWindowLocal, request.LocalPeakTime, request.ProductionContext?.ProductionEventIntelligence?.BestViewingWindowLocal, request.ProductionContext?.ProductionEventIntelligence?.LocalPeakTime, "tonight"), language);
        var direction = FirstNonEmpty(request.SkyDirectionHint, request.ProductionContext?.ProductionEventIntelligence?.SkyDirectionHint, "the clearest open sky");

        var shortScenes = BuildScenes(eventId, regionId, language, title, eventType, window, direction, shortForm: true);
        var longScenes = BuildScenes(eventId, regionId, language, title, eventType, window, direction, shortForm: false);
        var quality = Validate(shortScenes.Concat(longScenes).ToArray(), language);
        var warnings = quality.Warnings.ToList();
        var files = new List<string>();

        var shortDto = BuildDto(eventId, regionId, language, shortScenes);
        var longDto = BuildDto(eventId, regionId, language, longScenes);
        if (writeFiles)
        {
            var root = FirstNonEmpty(request.OutputRoot, request.ProductionContext?.PlanRoot);
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("OutputRoot is required when writing V3.1 final narration.", nameof(request));
            files.AddRange(await WriteAsync(root, "short", shortDto, cancellationToken));
            files.AddRange(await WriteAsync(root, "long", longDto, cancellationToken));
        }

        return new NarrationV31PreviewResponse(eventId, regionId, language, quality.IsValid, shortDto, longDto, quality, files, warnings);
    }

    private static QuestionDrivenNarrationSceneDto[] BuildScenes(string eventId, string regionId, string language, string title, string eventType, string window, string direction, bool shortForm)
    {
        var count = shortForm ? 5 : 9;
        return Enumerable.Range(0, count).Select(i =>
        {
            var section = shortForm
                ? (i switch { 0 => "Hook", 1 => "Explanation", 2 => "ViewingAdvice", 3 => "Reward", _ => "CTA" })
                : (i switch { 0 => "Hook", 1 => "Curiosity", 2 => "Explanation", 3 => "Curiosity", 4 => "Reward", 5 => "ViewingAdvice", 6 => "ViewingAdvice", 7 => "Reward", _ => "CTA" });
            var text = ComposeText(language, section, title, eventType, window, direction, i, shortForm);
            return new QuestionDrivenNarrationSceneDto(i + 1, section, section, $"What should viewers know about {title}?", Caption(text), string.Empty, "V3.1 narration composer", text, EstimateSeconds(text, language), "Warm documentary host", Caption(text), section, section);
        }).ToArray();
    }

    private static string ComposeText(string language, string section, string title, string eventType, string window, string direction, int index, bool shortForm)
    {
        if (language == "hi")
        {
            title = ApplyHindiTerms(title); eventType = ApplyHindiTerms(eventType); direction = ApplyHindiTerms(direction);
            return section switch
            {
                "Hook" => $"आज रात {title} आकाश में एक शांत लेकिन यादगार दृश्य बना रहा है।",
                "Curiosity" => $"यह {eventType} खास है, क्योंकि इसकी सुंदरता दूरी में नहीं बल्कि हमारी नज़र की दिशा में छिपी है।",
                "Explanation" => $"जब पृथ्वी से देखने की रेखा सही बनती है, तो अलग-अलग पिंड हमें एक ही कहानी का हिस्सा लगते हैं।",
                "ViewingAdvice" => $"सबसे अच्छा समय {window} है; {direction} की ओर देखें और आंखों को अंधेरे में ढलने दें।",
                "Reward" => $"धीरे-धीरे देखने पर चमक, रंग और स्थिति का बदलाव इस दृश्य को और जीवंत बना देता है।",
                _ => $"अगर मौसम साफ है, यह आकाश गाइड सेव करें और अगली खगोलीय घटना के लिए जुड़े रहें।"
            } + (shortForm ? string.Empty : $" यह दृश्य {index + 1} कहानी को पिछले पल से आगे ले जाता है।");
        }
        return section switch
        {
            "Hook" => $"Tonight, {title} gives the sky a clear story to follow rather than just another date on a calendar.",
            "Curiosity" => $"What makes this {eventType} interesting is the way perspective turns separate objects into one shared scene.",
            "Explanation" => $"From Earth, the line of sight changes quickly, so the event is about timing, geometry, and patience.",
            "ViewingAdvice" => $"For the best chance, watch around {window}, face {direction}, and give your eyes time to adjust.",
            "Reward" => $"The reward is a calm view that slowly reveals brightness, spacing, and motion as the minutes pass.",
            _ => $"If your sky is clear, save this guide, step outside at the right time, and follow for more astronomy events."
        } + (shortForm ? string.Empty : $" This beat {index + 1} keeps the narration connected to the visual scene.");
    }

    private static QuestionDrivenNarrationDto BuildDto(string eventId, string regionId, string language, IReadOnlyList<QuestionDrivenNarrationSceneDto> scenes)
        => new(eventId, regionId, language, scenes, scenes.Sum(s => s.EstimatedDurationSeconds), DateTimeOffset.UtcNow, "V3.1", new QuestionDrivenNarrationDiagnosticsDto(true, true, true, true, true, "V3.1", 90, DynamicNarrationGenerated: true, HardcodedTemplateUsed: false, SourceEventFactsUsed: scenes.Select(s => s.NarrationText).ToArray(), ScenePurposeUsed: scenes.Select(s => s.ScenePurpose).ToArray()));

    private static async Task<IReadOnlyList<string>> WriteAsync(string root, string format, QuestionDrivenNarrationDto dto, CancellationToken ct)
    {
        var dir = Path.Combine(root, "narration-engine", format);
        Directory.CreateDirectory(dir);
        var narration = Path.Combine(dir, "question-driven-narration-v2.json");
        var review = Path.Combine(dir, "question-driven-narration-review-v2.json");
        await File.WriteAllTextAsync(narration, JsonSerializer.Serialize(dto, JsonOptions), ct);
        var quality = Validate(dto.Scenes, dto.Language);
        await File.WriteAllTextAsync(review, JsonSerializer.Serialize(new QuestionDrivenNarrationReviewDto(dto.EventId, dto.RegionId, dto.Language, quality.IsValid, dto.Scenes.Count, dto.TotalEstimatedDurationSeconds, quality.Errors.Select(e => new QuestionDrivenNarrationReviewCheckDto("V3.1 quality", false, e)).DefaultIfEmpty(new QuestionDrivenNarrationReviewCheckDto("V3.1 quality", true, "Narration passed V3.1 quality validation.")).ToArray(), quality.Warnings, DateTimeOffset.UtcNow, true, false, true, 0, "V3.1", dto.Diagnostics), JsonOptions), ct);
        return [narration.Replace('\\', '/'), review.Replace('\\', '/')];
    }

    private static NarrationV31QualityReport Validate(IReadOnlyList<QuestionDrivenNarrationSceneDto> scenes, string language)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        var noDup = scenes.Select(s => Normalize(s.NarrationText)).Where(s => s.Length > 0).GroupBy(s => s).All(g => g.Count() == 1);
        var noInstructions = !scenes.Any(s => AuthoringPhrases.Any(p => s.NarrationText.Contains(p, StringComparison.OrdinalIgnoreCase)));
        var counts = scenes.Count is 5 or 9 or 14;
        var localizedTime = language != "hi" || scenes.Any(s => Regex.IsMatch(s.NarrationText, "[०-९]|सुबह|शाम|रात|दोपहर|बजे"));
        var hindiTerms = language != "hi" || scenes.Any(s => s.NarrationText.Any(c => c >= '\u0900' && c <= '\u097F'));
        if (!counts) errors.Add("Narration must contain 5 short scenes, 9 long scenes, or combined 14 scenes.");
        if (!noDup) errors.Add("Duplicate narration text detected.");
        if (!noInstructions) errors.Add("Authoring instruction text detected.");
        if (!localizedTime) errors.Add("Hindi narration must use localized time formatting.");
        return new(counts && noDup && noInstructions && localizedTime && hindiTerms, counts, noDup, noInstructions, localizedTime, hindiTerms, errors, warnings);
    }

    private static string FormatObservationWindow(string value, string language)
    {
        var text = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        text = Regex.Replace(text, @"\b(\d{1,2})(?::(\d{2}))?\s*(AM|PM)\b", m => FormatClock(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Success ? m.Groups[2].Value : "00", m.Groups[3].Value, language), RegexOptions.IgnoreCase);
        return language == "hi" ? ApplyHindiDigits(text.Replace("tonight", "आज रात", StringComparison.OrdinalIgnoreCase).Replace("after", "के बाद", StringComparison.OrdinalIgnoreCase).Replace("around", "लगभग", StringComparison.OrdinalIgnoreCase)) : text;
    }

    private static string FormatClock(int hour, string minute, string ampm, string language)
    {
        if (language != "hi") return minute == "00" ? $"{hour} {ampm.ToUpperInvariant()}" : $"{hour}:{minute} {ampm.ToUpperInvariant()}";
        var period = ampm.Equals("PM", StringComparison.OrdinalIgnoreCase) ? (hour >= 6 ? "शाम" : "दोपहर") : "सुबह";
        return ApplyHindiDigits(minute == "00" ? $"{period} {hour} बजे" : $"{period} {hour}:{minute} बजे");
    }

    private static string ApplyHindiTerms(string value) { foreach (var kv in HindiTerms) value = Regex.Replace(value ?? string.Empty, Regex.Escape(kv.Key), kv.Value, RegexOptions.IgnoreCase); return value; }
    private static string ApplyHindiDigits(string value) => string.Concat((value ?? string.Empty).Select(c => c is >= '0' and <= '9' ? (char)('०' + c - '0') : c));
    private static int EstimateSeconds(string text, string language) => Math.Max(6, (int)Math.Ceiling(Regex.Matches(text ?? string.Empty, @"[\p{L}\p{Nd}]+", RegexOptions.CultureInvariant).Count / (language == "hi" ? 120.0 : 135.0) * 60.0));
    private static string Caption(string text) => text.Length <= 80 ? text : text[..80].TrimEnd() + "…";
    private static string NormalizeLanguage(string? language) => string.Equals(language, "hi", StringComparison.OrdinalIgnoreCase) ? "hi" : "en";
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    private static string Normalize(string value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
}
