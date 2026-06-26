using System.Globalization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class NarrationV31Composer : INarrationV31Composer
{
    private static readonly IReadOnlyList<string> ShortSceneIds = SceneAssetsV3SceneContract.GetExpectedSceneIds("short");
    private static readonly IReadOnlyList<string> LongSceneIds = SceneAssetsV3SceneContract.GetExpectedSceneIds("long");
    private static readonly string[] AuthoringPhrases = ["open with", "explain", "describe", "focus on", "json", "metadata", "source answer"];
    public static IReadOnlyList<string> ExpectedSceneIds(string format)
        => SceneAssetsV3SceneContract.GetExpectedSceneIds(format);

    public static string ResolveScenePurpose(string sceneId)
        => ScenePurposeToNarrationSection.TryGetValue(sceneId, out var purpose) ? purpose : "what-you-will-see";

    private static readonly IReadOnlyDictionary<string, string> ScenePurposeToNarrationSection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["001-hook"] = "hook",
        ["002-what-is-it"] = "what-is-it",
        ["002-cause"] = "cause",
        ["003-cause"] = "cause",
        ["003-accurate-sky-guide"] = "accurate-sky-guide",
        ["004-interesting-fact"] = "interesting-fact",
        ["004-viewing-tip"] = "viewing-tips",
        ["005-best-time"] = "best-time",
        ["006-accurate-sky-guide"] = "accurate-sky-guide",
        ["007-what-you-will-see"] = "what-you-will-see",
        ["008-viewing-tips"] = "viewing-tips",
        ["009-final-reminder"] = "final-reminder"
    };
    private static readonly IReadOnlyDictionary<string, string> HindiTerms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Jupiter and Venus"] = "बृहस्पति और शुक्र", ["Jupiter"] = "बृहस्पति", ["Venus"] = "शुक्र", ["Geminids"] = "जेमिनिड्स",
        ["Phaethon"] = "फेथॉन (Phaethon)", ["Wolf Moon"] = "वुल्फ मून", ["Strawberry Moon"] = "स्ट्रॉबेरी मून",
        ["Corona"] = "सूर्य का कोरोना", ["Total Solar Eclipse"] = "पूर्ण सूर्य ग्रहण", ["Moon"] = "चंद्रमा", ["Mars"] = "मंगल",
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
        var title = HumanizeEventText(FirstNonEmpty(request.Title, request.ProductionContext?.ProductionEventIntelligence?.Title, "this sky event"));
        var eventType = HumanizeEventText(FirstNonEmpty(request.EventType, request.ProductionContext?.ProductionEventIntelligence?.EventType, "sky event"));
        var window = FormatObservationWindow(FirstNonEmpty(request.BestViewingWindowLocal, request.LocalPeakTime, request.ProductionContext?.ProductionEventIntelligence?.BestViewingWindowLocal, request.ProductionContext?.ProductionEventIntelligence?.LocalPeakTime, "tonight"), language);
        var direction = FirstNonEmpty(request.SkyDirectionHint, request.ProductionContext?.ProductionEventIntelligence?.SkyDirectionHint, "the clearest open sky");

        var shortScenes = BuildScenes(eventId, regionId, language, title, eventType, window, direction, shortForm: true);
        var longScenes = BuildScenes(eventId, regionId, language, title, eventType, window, direction, shortForm: false);
        var quality = Validate(shortScenes, longScenes, language);
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
        var sceneIds = shortForm ? ShortSceneIds : LongSceneIds;
        return sceneIds.Select((sceneId, i) =>
        {
            var text = ComposeText(language, sceneId, title, eventType, window, direction, i, shortForm);
            var purpose = ScenePurpose(sceneId);
            return new QuestionDrivenNarrationSceneDto(i + 1, sceneId, purpose, $"What should viewers know about {title}?", Caption(text), string.Empty, "NarrationGenerationServiceV31", text, EstimateSeconds(text, language), "Warm documentary host", Caption(text), sceneId, purpose);
        }).ToArray();
    }

    private static string ComposeText(string language, string section, string title, string eventType, string window, string direction, int index, bool shortForm)
    {
        if (language == "hi")
        {
            title = ApplyHindiTerms(title); eventType = ApplyHindiTerms(eventType); direction = ApplyHindiObservationTerms(ApplyHindiTerms(direction));
            return section switch
            {
                "001-hook" => $"आज रात {title} आकाश में एक शांत लेकिन यादगार दृश्य बना रहा है।",
                "002-what-is-it" => $"यह {eventType} पृथ्वी से दिखने वाला वास्तविक आकाशीय पल है।",
                "002-cause" => $"जब पृथ्वी धूल भरे कणों की धारा से गुजरती है, तो उल्काएं रोशनी की छोटी लकीरें बनाती हैं।",
                "003-cause" => $"जब पृथ्वी से देखने की रेखा सही बनती है, तो अलग-अलग पिंड हमें एक ही कहानी का हिस्सा लगते हैं।",
                "003-accurate-sky-guide" => $"{direction} की ओर देखें और आंखों को अंधेरे में ढलने दें।",
                "004-interesting-fact" => $"इसकी सुंदरता दूरी में नहीं बल्कि हमारी नज़र की दिशा और समय में छिपी है।",
                "005-best-time" => $"सबसे अच्छा समय {window} है, जब आकाश का अंतर साफ दिखता है।",
                "006-accurate-sky-guide" => $"{direction} की ओर देखें और आंखों को अंधेरे में ढलने दें।",
                "007-what-you-will-see" => $"धीरे-धीरे देखने पर चमक, रंग और स्थिति का बदलाव यह दृश्य जीवंत बना देता है।",
                "004-viewing-tip" => $"फोन की तेज रोशनी से बचें और कुछ मिनट शांत होकर आकाश देखें।",
                "008-viewing-tips" => $"फोन की तेज रोशनी से बचें और कुछ मिनट शांत होकर आकाश देखें।",
                _ => $"अगर मौसम साफ है, यह आकाश गाइड सेव करें और अगली खगोलीय घटना के लिए जुड़े रहें।"
            } + (shortForm ? string.Empty : $" यह दृश्य कहानी को पिछले पल से आगे ले जाता है।");
        }
        return section switch
        {
            "001-hook" => $"Look up for {title}, a sky event with a clear story to follow rather than just another date on a calendar.",
            "002-what-is-it" => $"This {eventType} is a real observing moment, visible because Earth gives us the right point of view.",
            "002-cause" => $"Meteor showers happen when Earth crosses a stream of dusty particles, and those grains burn into brief streaks of light.",
            "003-cause" => $"From Earth, the line of sight changes quickly, so the event is about timing, geometry, and patience.",
            "003-accurate-sky-guide" => $"Use {direction} as your guide, then scan slowly and let your eyes adjust before using binoculars.",
            "004-interesting-fact" => $"What makes this {eventType} interesting is the way perspective turns separate objects into one shared scene.",
            "005-best-time" => $"The best time to look is around {window}, when the sky is dark enough for the contrast to stand out.",
            "006-accurate-sky-guide" => $"Use {direction} as your guide, then scan slowly and let your eyes adjust before using binoculars.",
            "007-what-you-will-see" => $"You should notice brightness, spacing, and motion changing subtly as the minutes pass.",
            "004-viewing-tip" => $"Keep your phone dim, stay comfortable, and give your eyes several quiet minutes to adjust.",
            "008-viewing-tips" => $"For the best chance, watch around {window}, face {direction}, and give your eyes time to adjust.",
            _ => $"If your sky is clear, save this guide, step outside at the right time, and follow for more astronomy events."
        } + (shortForm ? string.Empty : $" This scene keeps the narration connected to the visual moment.");
    }


    private static string HumanizeEventText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var value = Regex.Replace(text, "(?<=[a-z])(?=[A-Z])", " ").Trim();
        value = Regex.Replace(value, @"\bMeteorShower\b", "meteor shower", RegexOptions.IgnoreCase);
        return value;
    }

    private static string ScenePurpose(string sceneId)
        => ScenePurposeToNarrationSection.TryGetValue(sceneId, out var section) ? section : sceneId;

    private static QuestionDrivenNarrationDto BuildDto(string eventId, string regionId, string language, IReadOnlyList<QuestionDrivenNarrationSceneDto> scenes)
    {
        var sectionCounts = scenes
            .GroupBy(s => s.ScenePurpose, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var mapping = scenes
            .ToDictionary(s => s.Section, s => s.ScenePurpose, StringComparer.OrdinalIgnoreCase);
        var format = scenes.Count == ShortSceneIds.Count ? "short" : "long";
        var keyedMapping = scenes
            .ToDictionary(s => $"{format}:{s.Section}", s => s.ScenePurpose, StringComparer.OrdinalIgnoreCase);
        var scopedPurposeMapping = scenes
            .ToDictionary(s => $"{format}/{s.ScenePurpose}", s => s.Section, StringComparer.OrdinalIgnoreCase);
        return new(eventId, regionId, language, scenes, scenes.Sum(s => s.EstimatedDurationSeconds), DateTimeOffset.UtcNow, "V3.1", new QuestionDrivenNarrationDiagnosticsDto(true, true, true, true, true, "V3.1", 90, DynamicNarrationGenerated: true, HardcodedTemplateUsed: false, SourceEventFactsUsed: scenes.Select(s => s.NarrationText).ToArray(), ScenePurposeUsed: scenes.Select(s => s.ScenePurpose).ToArray(), ScenePurposeToNarrationSection: mapping, NarrationSectionAppearanceCounts: sectionCounts, V31NarrationKeysUsed: scenes.Select(s => $"{format}:{s.Section}").ToArray(), V31ScenePurposeLookupKeysUsed: scopedPurposeMapping.Keys.ToArray(), V31FormatScenePurposeToSceneId: scopedPurposeMapping, V31FormatSceneIdToScenePurpose: keyedMapping));
    }

    private static async Task<IReadOnlyList<string>> WriteAsync(string root, string format, QuestionDrivenNarrationDto dto, CancellationToken ct)
    {
        var language = NormalizeLanguage(dto.Language);
        var dir = Path.Combine(root, "narration", language, format);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var files = new List<string>();
        foreach (var scene in dto.Scenes)
        {
            var path = Path.Combine(dir, scene.Section + ".txt");
            await File.WriteAllTextAsync(path, scene.NarrationText, ct);
            files.Add(path.Replace('\\', '/'));
        }
        return files;
    }

    private static NarrationV31QualityReport Validate(IReadOnlyList<QuestionDrivenNarrationSceneDto> shortScenes, IReadOnlyList<QuestionDrivenNarrationSceneDto> longScenes, string language)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        var scenes = shortScenes.Select(s => new { Format = "short", Scene = s }).Concat(longScenes.Select(s => new { Format = "long", Scene = s })).ToArray();
        var noDup = scenes.Select(s => Normalize(s.Scene.NarrationText)).Where(s => s.Length > 0).GroupBy(s => s).All(g => g.Count() == 1);
        var noInstructions = !scenes.Any(s => AuthoringPhrases.Any(p => s.Scene.NarrationText.Contains(p, StringComparison.OrdinalIgnoreCase)));
        var counts = shortScenes.Count == ShortSceneIds.Count && longScenes.Count == LongSceneIds.Count;
        var localizedTime = language != "hi" || scenes.Any(s => Regex.IsMatch(s.Scene.NarrationText, "[०-९]|सुबह|शाम|रात|दोपहर|बजे"));
        var hindiTerms = language != "hi" || scenes.Any(s => s.Scene.NarrationText.Any(c => c >= '\u0900' && c <= '\u097F'));
        if (!counts) errors.Add($"Narration must contain {ShortSceneIds.Count} short scenes and {LongSceneIds.Count} long scenes.");
        if (!noDup) errors.Add("Duplicate narration text detected.");
        if (!noInstructions) errors.Add("Authoring instruction text detected.");
        foreach (var group in scenes.GroupBy(s => s.Format, StringComparer.OrdinalIgnoreCase))
        {
            var duplicateSceneIds = group
                .GroupBy(s => s.Scene.Section, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{group.Key}:{g.Key}")
                .ToArray();
            if (duplicateSceneIds.Length > 0) errors.Add("V3.1 narration scene IDs must be unique within each format: " + string.Join(", ", duplicateSceneIds) + ".");
        }
        var duplicateCompositeKeys = scenes
            .GroupBy(s => $"{s.Format}:{s.Scene.Section}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicateCompositeKeys.Length > 0) errors.Add("V3.1 narration keys must be unique by format and sceneId: " + string.Join(", ", duplicateCompositeKeys) + ".");
        if (!ShortSceneIds.All(id => shortScenes.Any(s => string.Equals(s.Section, id, StringComparison.OrdinalIgnoreCase)))) errors.Add("V3.1 short narration is missing required scene IDs.");
        if (!LongSceneIds.All(id => longScenes.Any(s => string.Equals(s.Section, id, StringComparison.OrdinalIgnoreCase)))) errors.Add("V3.1 long narration is missing required scene IDs.");
        if (!localizedTime) errors.Add("Hindi narration must use localized time formatting.");
        if (language == "hi" && scenes.Any(s => Regex.IsMatch(s.Scene.NarrationText, @"\b(Jupiter and Venus|early evening|before sunrise|after sunset|eastern horizon|PM|AM)\b|\s+to\s+|\b(eastern|western|northern|southern)\s+(?:horizon|sky)\b|\b(?:toward|overhead|open sky)\b", RegexOptions.IgnoreCase)))
            errors.Add("Hindi narration contains raw English direction or timing phrasing.");
        return new(counts && noDup && noInstructions && localizedTime && hindiTerms && errors.Count == 0, counts, noDup, noInstructions, localizedTime, hindiTerms, errors, warnings);
    }

    private static string FormatObservationWindow(string value, string language)
    {
        var text = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        text = Regex.Replace(text, @"\b(\d{1,2})(?::(\d{2}))?\s*(AM|PM)\b", m => FormatClock(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Success ? m.Groups[2].Value : "00", m.Groups[3].Value, language), RegexOptions.IgnoreCase);
        if (language != "hi") return text;
        text = ApplyHindiObservationTerms(text);
        return ApplyHindiDigits(text.Replace("tonight", "आज रात", StringComparison.OrdinalIgnoreCase).Replace("after", "के बाद", StringComparison.OrdinalIgnoreCase).Replace("around", "लगभग", StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatClock(int hour, string minute, string ampm, string language)
    {
        if (language != "hi") return minute == "00" ? $"{hour} {ampm.ToUpperInvariant()}" : $"{hour}:{minute} {ampm.ToUpperInvariant()}";
        var period = ampm.Equals("PM", StringComparison.OrdinalIgnoreCase) ? (hour >= 6 ? "शाम" : "दोपहर") : "सुबह";
        return ApplyHindiDigits(minute == "00" ? $"{period} {hour} बजे" : $"{period} {hour}:{minute} बजे");
    }

    private static string ApplyHindiTerms(string value) { foreach (var kv in HindiTerms.OrderByDescending(kv => kv.Key.Length)) value = Regex.Replace(value ?? string.Empty, Regex.Escape(kv.Key), kv.Value, RegexOptions.IgnoreCase); return value; }
    private static string ApplyHindiObservationTerms(string value)
    {
        var text = value ?? string.Empty;
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
    private static string ApplyHindiDigits(string value) => string.Concat((value ?? string.Empty).Select(c => c is >= '0' and <= '9' ? (char)('०' + c - '0') : c));
    private static int EstimateSeconds(string text, string language) => Math.Max(6, (int)Math.Ceiling(Regex.Matches(text ?? string.Empty, @"[\p{L}\p{Nd}]+", RegexOptions.CultureInvariant).Count / (language == "hi" ? 120.0 : 135.0) * 60.0));
    private static string Caption(string text) => text.Length <= 80 ? text : text[..80].TrimEnd() + "…";
    private static string NormalizeLanguage(string? language) => string.Equals(language, "hi", StringComparison.OrdinalIgnoreCase) ? "hi" : "en";
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    private static string Normalize(string value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
}
