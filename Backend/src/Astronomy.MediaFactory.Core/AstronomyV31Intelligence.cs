using System.Text;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed record EventMetadata(
    string? VisibilityWindow = null,
    string? Direction = null,
    string? RecommendedTool = null,
    string? RarityDescription = null,
    string? HistoricalNote = null,
    string? NextOccurrence = null,
    string? ScientificNote = null,
    IReadOnlyDictionary<string, string>? AdditionalFacts = null);

public sealed record FactExpansionResult(
    string WhyImportant,
    string HowRare,
    string HistoricalContext,
    string WhenNext,
    string ObservationRelevance,
    string ScientificContext,
    IReadOnlyList<string> ViewerInterestFacts);

public sealed record NarrationIntelligenceContext(
    string HookStyle,
    string StoryArc,
    string TransitionStyle,
    IReadOnlyList<string> InterestingFactCandidates,
    string EmotionalTone,
    string AudienceLevel,
    string SuggestedSceneFocus);

public interface IFactExpansionService
{
    FactExpansionResult ExpandFacts(AstronomyEvent astronomyEvent, EventFamily family, EventMetadata metadata, string languageOrCulture = "en");
}

public interface INarrationIntelligenceService
{
    NarrationIntelligenceContext BuildContext(AstronomyEvent astronomyEvent, EventFamily family, string scenePurpose, FactExpansionResult facts, IReadOnlyDictionary<string, string>? sceneMetadata = null);
}

public interface IHindiNaturalizationService
{
    string Naturalize(string englishSceneNarrationOrIntent, string scenePurpose, EventFamily family, EventMetadata metadata, FactExpansionResult facts, IReadOnlyDictionary<string, string>? terminologyRules = null);
}

public sealed class FactExpansionService : IFactExpansionService
{
    private readonly AstronomyV3Options _options;
    public FactExpansionService(IOptions<AstronomyV3Options>? options = null) => _options = options?.Value ?? new AstronomyV3Options();

    public FactExpansionResult ExpandFacts(AstronomyEvent astronomyEvent, EventFamily family, EventMetadata metadata, string languageOrCulture = "en")
    {
        var eventName = SafeName(astronomyEvent);
        var why = family switch
        {
            EventFamily.PlanetGrouping => $"{eventName} is useful because it gives viewers an easy way to compare bright solar-system objects in the same part of the sky.",
            EventFamily.Meteor => $"{eventName} matters to skywatchers because it can turn patient naked-eye observing into a visible stream of brief meteors.",
            EventFamily.Moon => $"{eventName} is important because the Moon is bright, familiar, and easy for almost anyone to notice without equipment.",
            EventFamily.Eclipse => $"{eventName} is important because it shows the Sun, Moon, and Earth lining up in a way viewers can understand directly.",
            _ => $"{eventName} is a sky event worth explaining with clear viewing context."
        };

        var rarity = First(metadata.RarityDescription, astronomyEvent.RarityScore > 0 ? $"The available rarity score is {astronomyEvent.RarityScore:0.##} on the production scale, so avoid stronger claims without source metadata." : null,
            _options.AllowGenericFallbackFacts ? "Use cautious wording: this event is noteworthy, but no precise rarity cycle is provided." : "No rarity detail is available.");
        var history = First(metadata.HistoricalNote, _options.AllowGenericFallbackFacts ? FamilyHistoricalFallback(family) : "No historical context is available in the metadata.");
        var next = First(metadata.NextOccurrence, _options.AllowGenericFallbackFacts ? "The next comparable occurrence is not specified in the metadata, so do not state an exact future date." : "No next occurrence is available.");
        var obs = BuildObservationRelevance(metadata, family);
        var science = First(metadata.ScientificNote, FamilyScienceFallback(family));
        var facts = new List<string>();
        if (metadata.AdditionalFacts is not null) facts.AddRange(metadata.AdditionalFacts.Values.Where(v => !string.IsNullOrWhiteSpace(v)));
        facts.Add(science);
        facts.Add(obs);
        return new FactExpansionResult(why, rarity, history, next, obs, science, facts.Distinct(StringComparer.OrdinalIgnoreCase).Take(Math.Max(1, _options.MaxInterestingFactsPerVideo)).ToArray());
    }

    private static string SafeName(AstronomyEvent e) => !string.IsNullOrWhiteSpace(e.Title) ? e.Title : !string.IsNullOrWhiteSpace(e.EventType) ? e.EventType : "This event";
    private static string First(params string?[] values) => values.First(v => !string.IsNullOrWhiteSpace(v))!;
    private static string BuildObservationRelevance(EventMetadata m, EventFamily f) => $"Viewing guidance should emphasize {First(m.Direction, "the correct sky direction")}, {First(m.VisibilityWindow, "the local viewing window")}, and {First(m.RecommendedTool, f == EventFamily.Meteor ? "dark skies and patience" : "simple naked-eye viewing when safe")}.";
    private static string FamilyHistoricalFallback(EventFamily f) => f switch { EventFamily.PlanetGrouping => "Planet pairings have long been used as easy public markers for explaining planetary motion.", EventFamily.Meteor => "Meteor showers are recurring streams of debris, so past observations help viewers know what conditions matter.", EventFamily.Moon => "Named full moons are cultural sky markers; wording should avoid treating the name as a physical change in the Moon.", EventFamily.Eclipse => "Eclipses have historically been among the most memorable public astronomy events.", _ => "Historical context should stay general unless source metadata provides specifics." };
    private static string FamilyScienceFallback(EventFamily f) => f switch { EventFamily.PlanetGrouping => "The apparent closeness is line-of-sight geometry, not the planets being physically close together.", EventFamily.Meteor => "Meteors are tiny particles burning up as Earth passes through a debris stream.", EventFamily.Moon => "The full Moon appears opposite the Sun in the sky and reflects sunlight toward Earth.", EventFamily.Eclipse => "An eclipse depends on precise alignment and the observer's location within the visibility path.", _ => "Explain the observable astronomy without adding unsupported precision." };
}

public sealed class NarrationIntelligenceService : INarrationIntelligenceService
{
    private readonly AstronomyV3Options _options;
    public NarrationIntelligenceService(IOptions<AstronomyV3Options>? options = null) => _options = options?.Value ?? new AstronomyV3Options();
    public NarrationIntelligenceContext BuildContext(AstronomyEvent astronomyEvent, EventFamily family, string scenePurpose, FactExpansionResult facts, IReadOnlyDictionary<string, string>? sceneMetadata = null)
    {
        var purpose = scenePurpose.Trim();
        var hook = purpose.Contains("hook", StringComparison.OrdinalIgnoreCase) ? "curiosity-first cold open" : family switch { EventFamily.Eclipse => "safe wonder and alignment", EventFamily.Meteor => "patient-sky anticipation", EventFamily.Moon => "familiar object, fresh meaning", EventFamily.PlanetGrouping => "two-object comparison", _ => "clear observational curiosity" };
        var focus = purpose.ToLowerInvariant() switch { var p when p.Contains("cause") => facts.ScientificContext, var p when p.Contains("guide") => facts.ObservationRelevance, var p when p.Contains("fact") => facts.ViewerInterestFacts.FirstOrDefault() ?? facts.HistoricalContext, var p when p.Contains("final") => facts.WhenNext, _ => facts.WhyImportant };
        return new NarrationIntelligenceContext(hook, $"Move from why viewers should care, to what causes it, to how to observe it, then close with a grounded reminder for {family}.", "Use conversational handoffs that reference the previous scene without repeating it.", facts.ViewerInterestFacts, _options.NarrationTone, _options.AudienceLevel, focus);
    }
}

public sealed class HindiNaturalizationService : IHindiNaturalizationService
{
    public string Naturalize(string englishSceneNarrationOrIntent, string scenePurpose, EventFamily family, EventMetadata metadata, FactExpansionResult facts, IReadOnlyDictionary<string, string>? terminologyRules = null)
    {
        var term = terminologyRules ?? DefaultHindiTerms;
        string Term(string key) => term.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : DefaultHindiTerms[key];
        var eventWord = family switch { EventFamily.PlanetGrouping => Term("planetGrouping"), EventFamily.Meteor => Term("meteor"), EventFamily.Moon => Term("moon"), EventFamily.Eclipse => Term("eclipse"), _ => Term("event") };
        var focus = scenePurpose.ToLowerInvariant() switch
        {
            var p when p.Contains("hook") => $"आज आसमान में {eventWord} देखने का एक अच्छा मौका है—इसे सिर्फ तारीख नहीं, एक अनुभव की तरह देखिए।",
            var p when p.Contains("cause") => $"इसका कारण सरल है: {ToHindiScientific(family)} इसलिए दृश्य को बढ़ा-चढ़ाकर नहीं, सही संदर्भ में समझना ज़रूरी है।",
            var p when p.Contains("guide") => $"देखने के लिए {SafeHindi(metadata.Direction, "सही दिशा")} और {SafeHindi(metadata.VisibilityWindow, "स्थानीय समय")} पर ध्यान दें। {SafeHindi(metadata.RecommendedTool, "खुला आसमान")} मदद करेगा।",
            var p when p.Contains("fact") => $"एक दिलचस्प बात यह है कि {NaturalizeFact(facts.ViewerInterestFacts.FirstOrDefault() ?? facts.ScientificContext)}",
            var p when p.Contains("final") => "अगर मौसम साथ दे, तो कुछ मिनट रुककर आसमान को शांति से देखिए—यही छोटे पल astronomy को यादगार बनाते हैं।",
            _ => $"यह {eventWord} दर्शकों के लिए इसलिए खास है क्योंकि {NaturalizeFact(facts.WhyImportant)}"
        };
        return Regex.Replace(focus, "\\s+", " ").Trim();
    }
    private static readonly IReadOnlyDictionary<string, string> DefaultHindiTerms = new Dictionary<string, string> { ["planetGrouping"] = "ग्रहों की नज़दीकी", ["meteor"] = "उल्का-वर्षा", ["moon"] = "पूर्णिमा का चंद्रमा", ["eclipse"] = "सूर्य ग्रहण", ["event"] = "आकाशीय घटना" };
    private static string SafeHindi(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static string ToHindiScientific(EventFamily family) => family switch { EventFamily.PlanetGrouping => "ग्रह वास्तव में पास नहीं आते, वे हमारी दृष्टि-रेखा में पास दिखते हैं।", EventFamily.Meteor => "पृथ्वी धूल-कणों की धारा से गुजरती है और वे वातावरण में चमकते हैं।", EventFamily.Moon => "पूर्णिमा में चंद्रमा सूर्य के विपरीत दिशा में होता है और पूरा प्रकाशित दिखता है।", EventFamily.Eclipse => "सूर्य, चंद्रमा और पृथ्वी की सटीक alignment से छाया बनती है।", _ => "दृश्य खगोलीय geometry से बनता है।" };
    private static string NaturalizeFact(string text) => text.Replace("This event", "यह घटना", StringComparison.OrdinalIgnoreCase).Replace("viewers", "दर्शकों", StringComparison.OrdinalIgnoreCase);
}

public static class AstronomyV31PromptTemplate
{
    public static string BuildNarrationPrompt(AstronomyEvent astronomyEvent, EventFamily family, string scenePurpose, FactExpansionResult facts, NarrationIntelligenceContext intelligence, EventMetadata metadata, string language)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Write scene-level narration only. One scene equals one narration block and one MP3; subtitle cues are display-only.");
        sb.AppendLine($"Family: {family}");
        sb.AppendLine($"ScenePurpose: {scenePurpose}");
        sb.AppendLine($"Language: {language}");
        sb.AppendLine($"AudienceLevel: {intelligence.AudienceLevel}");
        sb.AppendLine($"Tone: {intelligence.EmotionalTone}");
        sb.AppendLine($"HookStyle: {intelligence.HookStyle}");
        sb.AppendLine($"StoryArc: {intelligence.StoryArc}");
        sb.AppendLine($"TransitionStyle: {intelligence.TransitionStyle}");
        sb.AppendLine($"SuggestedSceneFocus: {intelligence.SuggestedSceneFocus}");
        sb.AppendLine($"Facts: why={facts.WhyImportant}; rarity={facts.HowRare}; history={facts.HistoricalContext}; next={facts.WhenNext}; observe={facts.ObservationRelevance}; science={facts.ScientificContext}");
        sb.AppendLine($"Event metadata: title={astronomyEvent.Title}; type={astronomyEvent.EventType}; startUtc={astronomyEvent.StartUtc:O}; peakUtc={astronomyEvent.PeakUtc:O}; visibility={metadata.VisibilityWindow}; direction={metadata.Direction}; tool={metadata.RecommendedTool}");
        sb.AppendLine("Rules: documentary style; human presenter style; avoid robotic transitions; avoid duplicate scenes; avoid literal Hindi translation; preserve scene purpose; no fabricated dates or rare-event claims unless metadata exists.");
        return sb.ToString();
    }
}
