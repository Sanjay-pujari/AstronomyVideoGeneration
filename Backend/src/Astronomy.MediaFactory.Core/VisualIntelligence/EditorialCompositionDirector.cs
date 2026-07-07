using Astronomy.MediaFactory.Contracts;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record HeroCompositionTemplate
{
    public required string Name { get; init; }
    public required string SubjectPlacement { get; init; }
    public required string Balance { get; init; }
    public required string HorizonUsage { get; init; }
    public required string NegativeSpace { get; init; }
    public required string OverlaySafeArea { get; init; }
}

public sealed record PlanetRelationshipReview
{
    public string ReviewVersion { get; init; } = "4.4C.1";
    public required double RelationshipScore { get; init; }
    public required double VisualBalanceScore { get; init; }
    public required double DocumentaryScore { get; init; }
    public required string RelationshipClarity { get; init; }
    public required string VisualBalance { get; init; }
    public required string StoryCommunication { get; init; }
    public required string DocumentaryAuthenticity { get; init; }
    public required string ScientificPlausibility { get; init; }
    public required string PlanetProminenceAssessment { get; init; }
    public required string CompositionRecommendation { get; init; }
    public IReadOnlyList<string> CreativeRecommendations { get; init; } = [];
    public IReadOnlyList<string> BenchmarkPreparation { get; init; } = [];
    public IReadOnlyList<string> CreativeNotes { get; init; } = [];
}

public sealed record EditorialCompositionDecision
{
    public required HeroCompositionTemplate Template { get; init; }
    public required string VisualBalance { get; init; }
    public required string StorytellingEmphasis { get; init; }
    public required string VisualHierarchy { get; init; }
    public required string DocumentaryComposition { get; init; }
    public required string EnvironmentalContext { get; init; }
    public double RelationshipScore { get; init; }
    public double DocumentaryScore { get; init; }
    public double AstronomyScore { get; init; }
    public double VisualHierarchyScore { get; init; }
    public IReadOnlyList<string> StorytellingNotes { get; init; } = [];
    public IReadOnlyList<string> Recommendations { get; init; } = [];
    public PlanetRelationshipReview? PlanetRelationshipReview { get; init; }
}

public interface IEditorialCompositionDirector
{
    EditorialCompositionDecision Decide(VisualIntelligenceOrchestrationContext context, FamilyCreativeProfileResult profile, CreativeKnowledge? knowledge = null);
}

public interface IPlanetRelationshipDirector
{
    PlanetRelationshipReview? Review(VisualIntelligenceOrchestrationContext context, FamilyCreativeProfileResult profile, CreativeKnowledge? knowledge, HeroCompositionTemplate template);
}

public sealed class PlanetRelationshipDirector : IPlanetRelationshipDirector
{
    public PlanetRelationshipReview? Review(VisualIntelligenceOrchestrationContext context, FamilyCreativeProfileResult profile, CreativeKnowledge? knowledge, HeroCompositionTemplate template)
    {
        var subjects = Normalize(profile.PrimaryObjects.Concat(profile.SupportingObjects));
        if (profile.EventFamily is not (ContractEventFamily.PlanetConjunction or ContractEventFamily.PlanetOpposition) || subjects.Count is < 2 or > 2) return null;
        var pair = string.Join(" + ", subjects);
        var prominence = $"Visual partnership for {pair}; the conjunction itself is the primary subject, with relative prominence expressed through balanced brightness, natural separation, shared center, and coordinated lighting rather than absolute planet dominance.";
        var composition = template.Name switch
        {
            "PlanetPairing_Twilight" => "Use twilight atmosphere with horizontal or gentle diagonal balance, a subtle low horizon only if it clarifies observation, and generous negative space.",
            "PlanetPairing_Horizon" => "Use horizontal balance above a subtle horizon while keeping both planets bright, intentional, and unusually close together.",
            _ => "Prefer diagonal balance or horizontal balance with generous negative space; avoid centered static layouts and tiny secondary planets."
        };
        var notes = new List<string>
        {
            "Primary story: relationship; two bright planets appear unusually close together before either planet reads as the hero.",
            "Target balanced prominence and visual partnership; avoid a huge primary with a tiny forgotten secondary.",
            "Use natural separation, shared visual center, coordinated lighting, and shared atmosphere to make the close approach readable.",
            "Maintain astronomical plausibility with calm premium documentary realism, twilight atmosphere, and subtle horizon context only when useful.",
            "Avoid generic AI poster styling, fantasy backgrounds, artificial nebulae, dramatic color explosions, and fake connecting glow."
        };
        if (knowledge is not null) notes.Add($"Knowledge guidance: {knowledge.CompositionStrategy}");
        return new PlanetRelationshipReview
        {
            RelationshipScore = .97,
            VisualBalanceScore = .95,
            DocumentaryScore = .94,
            RelationshipClarity = "Relationship > Balance > Wonder > Scale: the viewer should immediately read that the two planets appear unusually close together.",
            VisualBalance = "Prefer balanced pairing, natural separation, shared visual center, coordinated lighting, and shared atmosphere; avoid huge-primary/tiny-secondary hierarchy.",
            StoryCommunication = "The conjunction is the hero and the individual planets support that story rather than competing as isolated poster objects.",
            DocumentaryAuthenticity = "Favor twilight, natural atmosphere, observational realism, premium astrophotography restraint, and a subtle horizon only when it improves observability.",
            ScientificPlausibility = "Show an apparent line-of-sight conjunction with plausible round planetary disks, restrained brightness differences, no fake physical contact, and no artificial nebula spectacle.",
            PlanetProminenceAssessment = prominence,
            CompositionRecommendation = composition,
            CreativeRecommendations =
            [
                "Lead with the apparent closeness of the pair before individual planetary scale.",
                "Compose the planets as visual partners around a shared center of attention.",
                "Keep both objects emotionally present even when one is naturally brighter or larger.",
                "Use documentary twilight or clean sky realism instead of fantasy-space decoration."
            ],
            BenchmarkPreparation =
            [
                "scenario=bright-planet-conjunction",
                "compareRelationshipClarity=true",
                "compareBalancedProminence=true",
                "compareDocumentaryAuthenticity=true",
                "runnerStatus=metadata-only"
            ],
            CreativeNotes = notes
        };
    }

    private static List<string> Normalize(IEnumerable<string> values) => values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

public sealed class EditorialCompositionDirector : IEditorialCompositionDirector
{
    private readonly IPlanetRelationshipDirector planetRelationshipDirector;

    public EditorialCompositionDirector() : this(new PlanetRelationshipDirector()) { }

    public EditorialCompositionDirector(IPlanetRelationshipDirector planetRelationshipDirector)
    {
        this.planetRelationshipDirector = planetRelationshipDirector;
    }
    private static readonly HeroCompositionTemplate CloseApproach = new()
    {
        Name = "PlanetPairing_CloseApproach",
        SubjectPlacement = "Place the two planets close enough to read as a single conjunction relationship, slightly above center on a clean diagonal.",
        Balance = "Give both planets meaningful visual prominence; Jupiter may be larger, but Venus must remain bright and intentional rather than a tiny speck.",
        HorizonUsage = "No horizon unless observation context clearly improves the story.",
        NegativeSpace = "Preserve calm negative space around the pair for a premium sky-view composition.",
        OverlaySafeArea = "Keep the lower third and upper title area quiet for deterministic Hero overlays."
    };

    private static readonly HeroCompositionTemplate Twilight = new()
    {
        Name = "PlanetPairing_Twilight",
        SubjectPlacement = "Hold the conjunction in the upper-middle sky against a gentle twilight gradient.",
        Balance = "Balance planet prominence through brightness, spacing, and clean separation rather than exaggerated scale.",
        HorizonUsage = "Use a very low, simple horizon only if it grounds the observation.",
        NegativeSpace = "Leave broad uncluttered sky between the planet pair and any foreground silhouette.",
        OverlaySafeArea = "Reserve a clean lower-third band and avoid busy texture behind text."
    };

    private static readonly HeroCompositionTemplate Horizon = new()
    {
        Name = "PlanetPairing_Horizon",
        SubjectPlacement = "Frame the planet pair above a restrained horizon silhouette so the sky event remains the subject.",
        Balance = "Keep both planets visually connected and readable; do not let foreground scale overpower the conjunction.",
        HorizonUsage = "Use one documentary silhouette at most: horizon, mountain skyline, observatory, or tree line.",
        NegativeSpace = "Maintain uncluttered twilight sky around the pair.",
        OverlaySafeArea = "Keep silhouettes low and simple so overlays remain legible."
    };

    private static readonly HeroCompositionTemplate Cinematic = new()
    {
        Name = "PlanetPairing_Cinematic",
        SubjectPlacement = "Use an elegant off-center cinematic arrangement where the relationship leads the eye before scale does.",
        Balance = "Prioritize story, relationship, beauty, then scale; avoid largest-object-wins staging.",
        HorizonUsage = "Environmental context should be minimal and only included if it adds documentary emotion.",
        NegativeSpace = "Use deep, clean sky gradients and restrained stars.",
        OverlaySafeArea = "Protect title and lower-third safe areas with smooth low-detail background."
    };

    public EditorialCompositionDecision Decide(VisualIntelligenceOrchestrationContext context, FamilyCreativeProfileResult profile, CreativeKnowledge? knowledge = null)
    {
        var isPairing = profile.EventFamily is ContractEventFamily.PlanetConjunction or ContractEventFamily.PlanetOpposition && Normalize(profile.PrimaryObjects.Concat(profile.SupportingObjects)).Count <= 2;
        var text = $"{context.EventType} {context.EventName} {context.VisibilityGuidance} {context.Location} {context.Region}".ToLowerInvariant();
        var template = isPairing ? SelectPairingTemplate(text, context) : Cinematic;
        var contextChoice = SelectDocumentaryContext(text, template);
        var subjects = Normalize(profile.PrimaryObjects.Concat(profile.SupportingObjects));
        var relationship = isPairing ? string.Join(" + ", subjects.DefaultIfEmpty(profile.Hero)) : profile.Hero;
        var relationshipReview = planetRelationshipDirector.Review(context, profile, knowledge, template);

        return new EditorialCompositionDecision
        {
            Template = template,
            VisualBalance = knowledge?.VisualBalance ?? (isPairing ? "Balanced visual partnership for the conjunction; avoid huge primary/tiny secondary staging and make the apparent closeness the subject." : "Single clear astronomy subject with supporting context held back."),
            StorytellingEmphasis = knowledge is not null && isPairing ? $"{knowledge.StoryGoal} Relationship: {relationship}." : knowledge?.StoryGoal ?? (isPairing ? $"The conjunction is the hero: {relationship} should feel visually connected as one observable sky moment." : profile.Intent),
            VisualHierarchy = isPairing ? "Relationship first, then balance, then wonder, then scale." : "Story first, then relationship, then beauty, then scale.",
            DocumentaryComposition = knowledge is null ? contextChoice : $"{contextChoice} {knowledge.DocumentaryGuidance}",
            EnvironmentalContext = contextChoice,
            RelationshipScore = relationshipReview?.RelationshipScore ?? (isPairing ? .94 : .78),
            DocumentaryScore = relationshipReview?.DocumentaryScore ?? (contextChoice.Contains("No foreground", StringComparison.OrdinalIgnoreCase) ? .82 : .9),
            AstronomyScore = isPairing ? .92 : .88,
            VisualHierarchyScore = relationshipReview?.VisualBalanceScore ?? .93,
            StorytellingNotes = knowledge?.EditorialNotes ?? (isPairing
                ? ["Treat the conjunction itself as the hero, not Jupiter alone.", "Planets should feel paired through placement, brightness, natural separation, shared center, coordinated lighting, and shared atmosphere.", "Preserve circular planetary geometry and plausible relative appearance."]
                : ["Use documentary context only when it clarifies the observable event."]),
            Recommendations = knowledge?.AvoidPatterns.Select(p => $"Avoid {p}.").ToList() ?? (isPairing
                ? ["Target balanced visual partnership between Jupiter and Venus when both are present.", "Keep foreground silhouettes optional and minimal; prefer twilight realism and a subtle horizon only when useful.", "Avoid clutter, fake glow, stretched planets, tiny secondary objects, artificial nebulae, fantasy backgrounds, and technical prompt language."]
                : ["Keep the composition editorial and uncluttered."]),
            PlanetRelationshipReview = relationshipReview
        };
    }

    private static HeroCompositionTemplate SelectPairingTemplate(string text, VisualIntelligenceOrchestrationContext context)
    {
        if (text.Contains("horizon") || text.Contains("mountain") || text.Contains("observatory") || text.Contains("tree")) return Horizon;
        if (text.Contains("twilight") || text.Contains("dusk") || text.Contains("sunset") || text.Contains("evening")) return Twilight;
        if (context.Platform == Platform.Hero) return Cinematic;
        return CloseApproach;
    }

    private static string SelectDocumentaryContext(string text, HeroCompositionTemplate template)
    {
        if (template.Name == "PlanetPairing_CloseApproach") return "No foreground by default; let the clean sky and close approach carry the story.";
        if (text.Contains("observatory")) return "A single low observatory silhouette may be used if it strengthens the observational story.";
        if (text.Contains("mountain")) return "A restrained mountain skyline may ground the twilight observation without clutter.";
        if (text.Contains("tree")) return "A simple tree line may be used as a quiet documentary foreground.";
        if (text.Contains("horizon") || template.Name == "PlanetPairing_Horizon") return "A low horizon silhouette may be used sparingly to make the event feel observed from Earth.";
        if (template.Name == "PlanetPairing_Twilight") return "Calm twilight atmosphere is preferred; foreground remains optional and minimal.";
        return "Calm twilight atmosphere may be used only if it strengthens the story; never clutter the image.";
    }

    private static List<string> Normalize(IEnumerable<string> values) => values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
