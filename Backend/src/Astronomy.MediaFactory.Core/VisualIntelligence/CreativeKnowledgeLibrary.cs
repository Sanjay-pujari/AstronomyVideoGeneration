using Astronomy.MediaFactory.Contracts;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public enum CreativeKnowledgeDomain
{
    EditorialPrinciples,
    CompositionPrinciples,
    ViewerPsychology,
    DocumentaryLanguage,
    VisualHierarchy,
    HumanContext,
    EnvironmentalContext
}

public enum CreativeKnowledgeFamily
{
    GenericAstronomy,
    PlanetPairing,
    PlanetGrouping,
    MeteorShower,
    SolarEclipse,
    LunarEclipse,
    NamedFullMoon
}

public sealed record CreativeKnowledge
{
    public required CreativeKnowledgeFamily Family { get; init; }
    public required string ViewerQuestion { get; init; }
    public required string StoryGoal { get; init; }
    public required string EmotionalGoal { get; init; }
    public IReadOnlyList<string> CompositionPreferences { get; init; } = [];
    public required string VisualBalance { get; init; }
    public required string DocumentaryGuidance { get; init; }
    public IReadOnlyList<string> AvoidPatterns { get; init; } = [];
    public IReadOnlyDictionary<CreativeKnowledgeDomain, IReadOnlyList<string>> Domains { get; init; } = new Dictionary<CreativeKnowledgeDomain, IReadOnlyList<string>>();

    public string CompositionStrategy => string.Join(" ", CompositionPreferences);
    public IReadOnlyList<string> EditorialNotes => Domains.SelectMany(d => d.Value.Select(v => $"{d.Key}: {v}")).ToList();
}

public interface ICreativeKnowledgeLibrary
{
    CreativeKnowledge Resolve(VisualIntelligenceOrchestrationContext context, FamilyCreativeProfileResult? profile = null, IList<DiagnosticMessage>? diagnostics = null);
    CreativeKnowledge Get(CreativeKnowledgeFamily family);
}

public sealed class CreativeKnowledgeLibrary : ICreativeKnowledgeLibrary
{
    private readonly IReadOnlyDictionary<CreativeKnowledgeFamily, CreativeKnowledge> entries;

    public CreativeKnowledgeLibrary()
    {
        entries = BuildEntries().ToDictionary(e => e.Family);
    }

    public CreativeKnowledge Get(CreativeKnowledgeFamily family) => entries.TryGetValue(family, out var entry) ? entry : entries[CreativeKnowledgeFamily.GenericAstronomy];

    public CreativeKnowledge Resolve(VisualIntelligenceOrchestrationContext context, FamilyCreativeProfileResult? profile = null, IList<DiagnosticMessage>? diagnostics = null)
    {
        var family = ResolveFamily(context, profile);
        var knowledge = Get(family);
        diagnostics?.Add(new DiagnosticMessage { Severity = DiagnosticSeverity.Info, Code = "creative_knowledge.resolved", Message = $"Creative knowledge resolved: {knowledge.Family}.", Source = nameof(CreativeKnowledgeLibrary) });
        if (knowledge.Family == CreativeKnowledgeFamily.GenericAstronomy)
            diagnostics?.Add(new DiagnosticMessage { Severity = DiagnosticSeverity.Warning, Code = "creative_knowledge.fallback", Message = "No specific creative knowledge found; generic astronomy knowledge used.", Source = nameof(CreativeKnowledgeLibrary) });
        return knowledge;
    }

    private static CreativeKnowledgeFamily ResolveFamily(VisualIntelligenceOrchestrationContext context, FamilyCreativeProfileResult? profile)
    {
        var text = $"{context.EventFamily} {context.EventType} {context.EventName}".ToLowerInvariant();
        var eventFamily = profile?.EventFamily ?? context.EventFamily;
        if (text.Contains("solar") && text.Contains("eclipse") || eventFamily == ContractEventFamily.SolarEvent) return CreativeKnowledgeFamily.SolarEclipse;
        if (text.Contains("lunar") && text.Contains("eclipse")) return CreativeKnowledgeFamily.LunarEclipse;
        if ((text.Contains("full moon") || text.Contains("supermoon") || text.Contains("moon")) && !text.Contains("eclipse")) return CreativeKnowledgeFamily.NamedFullMoon;
        if (eventFamily == ContractEventFamily.MeteorShower || text.Contains("meteor")) return CreativeKnowledgeFamily.MeteorShower;
        if (text.Contains("grouping") || context.PrimaryObjects.Count > 2) return CreativeKnowledgeFamily.PlanetGrouping;
        if (eventFamily is ContractEventFamily.PlanetConjunction or ContractEventFamily.PlanetOpposition || text.Contains("conjunction") || text.Contains("pair")) return CreativeKnowledgeFamily.PlanetPairing;
        return CreativeKnowledgeFamily.GenericAstronomy;
    }

    private static IEnumerable<CreativeKnowledge> BuildEntries()
    {
        yield return Entry(CreativeKnowledgeFamily.PlanetPairing, "What makes these two planets worth looking for together?", "The conjunction is the hero: make the relationship between the two planets the subject, so the viewer understands this as one observable sky moment rather than two isolated dots.", "Create quiet wonder, recognition, and the feeling of being invited outside at the right time.", ["Use paired placement, clean separation, and shared negative space to make the conjunction read instantly.", "Prefer a calm twilight or clean sky field; add horizon context only when it helps the viewer imagine observing it."], "Balanced prominence: one planet may be larger or brighter, but the companion must feel intentional and emotionally present.", "Speak visually like an astronomy documentary: relationship, observability, scale, and restraint before spectacle.", ["largest-object-wins staging", "tiny forgotten secondary planet", "fake glow connecting planets", "crowded poster layout", "prompt syntax or provider instructions"], PairingDomains());
        yield return Entry(CreativeKnowledgeFamily.PlanetGrouping, "How do I read several objects in the same part of the sky?", "Organize multiple bodies into a calm sky map-like story with clear grouping and observational readability.", "Give the viewer confidence and curiosity rather than visual clutter.", ["Use wide negative space and a simple visual path across the objects.", "Subtle labels or constellation context may help when they clarify relationships."], "Distribute attention without making every object compete for hero status.", "Frame the scene as a guided sky observation, not a decorative planet collage.", ["overcrowded labels", "equal-size planets regardless of role", "astrology chart language"]);
        yield return Entry(CreativeKnowledgeFamily.MeteorShower, "Where should I look, and what will the sky feel like?", "Convey radiant direction, dark-sky patience, and sparse motion without turning the event into fantasy fireballs.", "Build anticipation, quiet patience, and night-sky immersion.", ["Favor dark sky, horizon scale, and radiant-aware streak direction.", "Leave enough stillness that meteors feel discovered, not pasted on."], "The sky dome and radiant are primary; meteor streaks support the story without chaos.", "Use observational field language: waiting, looking up, dark adaptation, and sky direction.", ["meteor storm exaggeration", "chaotic fireworks", "oversaturated trails"]);
        yield return Entry(CreativeKnowledgeFamily.SolarEclipse, "What is happening to the Sun, and how do I experience it safely?", "Explain alignment and eclipse phase through geometry, contrast, and credible solar detail.", "Create awe with caution, rarity, and scientific clarity.", ["Center strong circular geometry and corona structure when relevant.", "Use human/environment context only to express safe observation and scale."], "The solar-lunar alignment is the hero; foreground must never overpower or imply unsafe viewing.", "Document the event as rare alignment and safe observation, not disaster spectacle.", ["unsafe naked-eye viewing cues", "fake fire", "apocalyptic city scenes"]);
        yield return Entry(CreativeKnowledgeFamily.LunarEclipse, "Why does the Moon look different tonight?", "Show Earth-shadow storytelling with umbra, penumbra, and natural copper lunar color.", "Create calm mystery and recognition of a familiar Moon transformed.", ["Keep the Moon circular and textural even under shadow.", "Use shadow gradient as the narrative device."], "Moon remains the hero; red color should be natural, not neon.", "Use documentary language around Earth's shadow, phases, and patient observation.", ["neon blood moon", "horror tone", "distorted lunar disk"]);
        yield return Entry(CreativeKnowledgeFamily.NamedFullMoon, "What makes this full Moon special enough to notice?", "Turn a named full Moon into an observable, seasonal, emotionally grounded sky moment.", "Create calm beauty, familiarity, and a reason to step outside.", ["Let the Moon be a clean circular hero with restrained horizon or seasonal context.", "Use minimal context to support the name without becoming folklore poster art."], "Large Moon presence with restrained environment; name supports the observation rather than replacing it.", "Document seasonal visibility and lunar texture with premium restraint.", ["horoscope style", "mythic fantasy symbols", "oversized fake Moon"]);
        yield return Entry(CreativeKnowledgeFamily.GenericAstronomy, "What should the viewer understand about this sky event?", "Find one clear astronomy subject and support it with factual, low-clutter context.", "Create trustworthy curiosity.", ["Prioritize one subject, clean hierarchy, and non-invented observation context."], "Single clear hero with supporting context held back.", "Use premium astronomy documentary restraint.", ["generic AI poster", "clutter", "invented observation details"]);
    }

    private static CreativeKnowledge Entry(CreativeKnowledgeFamily family, string question, string story, string emotion, IReadOnlyList<string> composition, string balance, string documentary, IReadOnlyList<string> avoid, IReadOnlyDictionary<CreativeKnowledgeDomain, IReadOnlyList<string>>? domains = null) => new() { Family = family, ViewerQuestion = question, StoryGoal = story, EmotionalGoal = emotion, CompositionPreferences = composition, VisualBalance = balance, DocumentaryGuidance = documentary, AvoidPatterns = avoid, Domains = domains ?? DefaultDomains(story, balance, documentary) };

    private static IReadOnlyDictionary<CreativeKnowledgeDomain, IReadOnlyList<string>> PairingDomains() => new Dictionary<CreativeKnowledgeDomain, IReadOnlyList<string>>
    {
        [CreativeKnowledgeDomain.EditorialPrinciples] = ["Make the conjunction relationship the headline idea; avoid turning either planet into a solo portrait."],
        [CreativeKnowledgeDomain.CompositionPrinciples] = ["Use proximity, diagonal flow, and shared negative space to communicate pairing."],
        [CreativeKnowledgeDomain.ViewerPsychology] = ["Answer why the viewer should look now and where their eye should land first."],
        [CreativeKnowledgeDomain.DocumentaryLanguage] = ["Prefer observed sky realism, restrained atmosphere, and factual viewing context."],
        [CreativeKnowledgeDomain.VisualHierarchy] = ["Story first, then relationship, then beauty, then scale."],
        [CreativeKnowledgeDomain.HumanContext] = ["Human context is optional and should imply observation, not dominate the event."],
        [CreativeKnowledgeDomain.EnvironmentalContext] = ["Twilight, horizon, or observatory context is useful only when it strengthens observability."]
    };

    private static IReadOnlyDictionary<CreativeKnowledgeDomain, IReadOnlyList<string>> DefaultDomains(string story, string balance, string documentary) => new Dictionary<CreativeKnowledgeDomain, IReadOnlyList<string>>
    {
        [CreativeKnowledgeDomain.EditorialPrinciples] = [story],
        [CreativeKnowledgeDomain.CompositionPrinciples] = [balance],
        [CreativeKnowledgeDomain.ViewerPsychology] = ["Answer the viewer's immediate why-look question before adding visual decoration."],
        [CreativeKnowledgeDomain.DocumentaryLanguage] = [documentary],
        [CreativeKnowledgeDomain.VisualHierarchy] = ["Keep the event readable through one clear hero idea and restrained supporting context."],
        [CreativeKnowledgeDomain.HumanContext] = ["Use people or observation cues only when they clarify scale, safety, or the act of skywatching."],
        [CreativeKnowledgeDomain.EnvironmentalContext] = ["Use horizon, weather, season, or location details only when they make the observation feel grounded and factual."]
    };
}
