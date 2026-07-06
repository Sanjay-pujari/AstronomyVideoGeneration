using Astronomy.MediaFactory.Contracts;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record FamilyCreativeProfileResult
{
    public required ContractEventFamily EventFamily { get; init; }
    public required IReadOnlyList<string> PrimaryObjects { get; init; }
    public required IReadOnlyList<string> SupportingObjects { get; init; }
    public required string Intent { get; init; }
    public required string Hero { get; init; }
    public required string SupportingText { get; init; }
    public required string Hierarchy { get; init; }
    public required CompositionStyle CompositionStyle { get; init; }
    public required string SubjectTreatment { get; init; }
    public IReadOnlyList<CdlDirective> CdlDirectives { get; init; } = [];
    public Dictionary<string, object?> ContractExtensions { get; init; } = [];
    public NegativeConstraints NegativeConstraints { get; init; } = new();
    public QualityTargets QualityTargets { get; init; } = new();
    public IReadOnlyList<DiagnosticMessage> Diagnostics { get; init; } = [];
}

public interface IFamilyCreativeProfile
{
    string ProfileName { get; }
    IReadOnlySet<ContractEventFamily> SupportedFamilies { get; }
    bool Supports(VisualIntelligenceOrchestrationContext context);
    FamilyCreativeProfileResult Create(VisualIntelligenceOrchestrationContext context);
}

public interface IFamilyCreativeProfileResolver
{
    IFamilyCreativeProfile Resolve(VisualIntelligenceOrchestrationContext context, IList<DiagnosticMessage> diagnostics);
}

public sealed class FamilyCreativeProfileResolver : IFamilyCreativeProfileResolver
{
    private readonly IReadOnlyList<IFamilyCreativeProfile> profiles;
    private readonly GenericAstronomyCreativeProfile fallback;

    public FamilyCreativeProfileResolver(IEnumerable<IFamilyCreativeProfile> profiles)
    {
        this.profiles = profiles.ToList();
        fallback = this.profiles.OfType<GenericAstronomyCreativeProfile>().FirstOrDefault() ?? new GenericAstronomyCreativeProfile();
    }

    public IFamilyCreativeProfile Resolve(VisualIntelligenceOrchestrationContext context, IList<DiagnosticMessage> diagnostics)
    {
        var profile = profiles.Where(p => p is not GenericAstronomyCreativeProfile).FirstOrDefault(p => p.Supports(context)) ?? fallback;
        diagnostics.Add(DiagnosticSeverity.Info, "visual_director.profile_selected", $"Family creative profile selected: {profile.ProfileName}.", profile.ProfileName);
        if (profile == fallback)
            diagnostics.Add(DiagnosticSeverity.Warning, "visual_director.fallback_profile_used", "Unsupported or unknown event family; generic astronomy creative profile used.", profile.ProfileName);
        return profile;
    }
}

public abstract class FamilyCreativeProfileBase : IFamilyCreativeProfile
{
    public abstract string ProfileName { get; }
    public abstract IReadOnlySet<ContractEventFamily> SupportedFamilies { get; }
    public virtual bool Supports(VisualIntelligenceOrchestrationContext context) => SupportedFamilies.Contains(context.EventFamily);
    public abstract FamilyCreativeProfileResult Create(VisualIntelligenceOrchestrationContext context);

    protected FamilyCreativeProfileResult Result(VisualIntelligenceOrchestrationContext c, ContractEventFamily family, string intent, string hero, string supporting, string hierarchy, CompositionStyle composition, string treatment, IEnumerable<CdlDirective>? directives = null, NegativeConstraints? negative = null, QualityTargets? quality = null)
    {
        var primary = Normalize(c.PrimaryObjects).DefaultIfEmpty(hero).ToList();
        var secondary = Normalize(c.SupportingObjects).ToList();
        var diagnostics = new List<DiagnosticMessage>();
        diagnostics.Add(DiagnosticSeverity.Info, "visual_director.profile_applied", $"{ProfileName} applied family creative direction.", ProfileName);
        if (c.PrimaryObjects.Count == 0) diagnostics.Add(DiagnosticSeverity.Warning, "visual_director.missing_primary_subject", "Primary subject was missing; family profile default was applied.", ProfileName);
        if (c.SupportingObjects.Count == 0) diagnostics.Add(DiagnosticSeverity.Info, "visual_director.missing_supporting_subjects", "Supporting subjects were missing; family profile guidance supplied supporting context.", ProfileName);
        if (c.ObservationDateTime is null && string.IsNullOrWhiteSpace(c.VisibilityGuidance) && string.IsNullOrWhiteSpace(c.Location) && string.IsNullOrWhiteSpace(c.Region)) diagnostics.Add(DiagnosticSeverity.Info, "visual_director.missing_observation_details", "Observation details were missing; observation-card placeholders remain non-invented.", ProfileName);
        diagnostics.Add(DiagnosticSeverity.Info, "visual_director.defaults_applied", "Drashyam brand, rendering, typography, observation-card, negative-constraint, and quality defaults composed.", ProfileName);
        return new FamilyCreativeProfileResult { EventFamily = family, PrimaryObjects = primary, SupportingObjects = secondary, Intent = intent, Hero = hero, SupportingText = supporting, Hierarchy = hierarchy, CompositionStyle = composition, SubjectTreatment = treatment, CdlDirectives = directives?.ToList() ?? [], NegativeConstraints = negative ?? DefaultNegative(), QualityTargets = quality ?? DefaultQuality(), Diagnostics = diagnostics, ContractExtensions = new() { ["familyCreativeProfile"] = ProfileName } };
    }

    protected static List<string> Normalize(IEnumerable<string> values) => values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    protected static bool TextHas(VisualIntelligenceOrchestrationContext c, string value) => $"{c.EventFamily} {c.EventType} {c.EventName}".Contains(value, StringComparison.OrdinalIgnoreCase);
    protected static CdlDirective D(string name, string value, int priority) => new(name, value, priority);
    protected static NegativeConstraints DefaultNegative(params string[] scientific) => new() { Scientific = [..scientific, "no stretched or distorted planets", "no fake glow", "no cartoon planets", "no incorrect unsafe solar viewing cues"], Brand = ["not generic AI poster", "not horoscope/zodiac style"], Typography = ["no cluttered text", "no tiny unreadable labels"], Provider = ["no prompt generation in director"] };
    protected static QualityTargets DefaultQuality() => new() { Dimensions = [new() { Name = QualityCategory.AstronomicalPlausibility, MinimumScore = .86, Weight = .3, Blocking = true }, new() { Name = QualityCategory.BrandCompliance, MinimumScore = .84, Weight = .25 }, new() { Name = QualityCategory.TextReadability, MinimumScore = .82, Weight = .2 }, new() { Name = QualityCategory.PlatformSuitability, MinimumScore = .82, Weight = .25 }] };
}

public sealed class PlanetPairingCreativeProfile : FamilyCreativeProfileBase
{
    public override string ProfileName => nameof(PlanetPairingCreativeProfile);
    public override IReadOnlySet<ContractEventFamily> SupportedFamilies { get; } = new HashSet<ContractEventFamily> { ContractEventFamily.PlanetConjunction, ContractEventFamily.PlanetOpposition };
    public override bool Supports(VisualIntelligenceOrchestrationContext c) => base.Supports(c) && !TextHas(c, "grouping");
    public override FamilyCreativeProfileResult Create(VisualIntelligenceOrchestrationContext c)
    {
        var relationship = string.Join(" and ", Normalize(c.PrimaryObjects.Concat(c.SupportingObjects)).DefaultIfEmpty("bright planets"));
        return Result(c, ContractEventFamily.PlanetConjunction, "Show the planetary conjunction as the hero relationship, with both planets visually connected in a premium observational sky composition.", relationship, "balanced companion planet, calm twilight atmosphere, clean star field", "story first, then planetary relationship, then beauty, then scale; balanced visual prominence with close-approach separation", CompositionStyle.RuleOfThirds, "editorial documentary planet pairing", [D("familyCreativeDirection", "conjunction is the hero; balanced visual prominence; planets feel visually connected; no tiny secondary planet; perfect circular planets; premium telescope realism", 99)]);
    }
}

public sealed class PlanetGroupingCreativeProfile : FamilyCreativeProfileBase
{
    public override string ProfileName => nameof(PlanetGroupingCreativeProfile);
    public override IReadOnlySet<ContractEventFamily> SupportedFamilies { get; } = new HashSet<ContractEventFamily> { ContractEventFamily.PlanetConjunction };
    public override bool Supports(VisualIntelligenceOrchestrationContext c) => TextHas(c, "grouping") || Normalize(c.PrimaryObjects).Count > 2;
    public override FamilyCreativeProfileResult Create(VisualIntelligenceOrchestrationContext c) => Result(c, ContractEventFamily.PlanetConjunction, "Show multiple visible objects in a balanced sky-view astronomy composition.", string.Join(" and ", Normalize(c.PrimaryObjects).DefaultIfEmpty("planet group")), "multiple visible objects, subtle labels, constellation support", "balanced hierarchy across the grouped objects with readable sky-view composition", CompositionStyle.WideNegativeSpace, "planet grouping sky-view realism", [D("familyCreativeDirection", "multiple visible objects; balanced hierarchy; sky-view composition; subtle labels/constellation support", 99)]);
}

public sealed class MeteorShowerCreativeProfile : FamilyCreativeProfileBase { public override string ProfileName => nameof(MeteorShowerCreativeProfile); public override IReadOnlySet<ContractEventFamily> SupportedFamilies { get; } = new HashSet<ContractEventFamily> { ContractEventFamily.MeteorShower }; public override FamilyCreativeProfileResult Create(VisualIntelligenceOrchestrationContext c) => Result(c, ContractEventFamily.MeteorShower, "Convey meteor activity from a dark sky field with radiant-aware meteor streaks and observation-card utility.", "meteor shower radiant", "radiant region, sparse meteor streaks, horizon context", "radiant and sky dome first, meteor streaks supporting not chaotic", CompositionStyle.WideNegativeSpace, "dark-sky meteor shower documentary", [D("familyCreativeDirection", "dark sky field; radiant-aware meteor streaks; no exaggerated fireball unless indicated; observation-card friendly", 99)], DefaultNegative("no exaggerated fireball unless indicated")); }
public sealed class NamedFullMoonCreativeProfile : FamilyCreativeProfileBase { public override string ProfileName => nameof(NamedFullMoonCreativeProfile); public override IReadOnlySet<ContractEventFamily> SupportedFamilies { get; } = new HashSet<ContractEventFamily> { ContractEventFamily.LunarEvent }; public override bool Supports(VisualIntelligenceOrchestrationContext c) => base.Supports(c) && !TextHas(c, "eclipse"); public override FamilyCreativeProfileResult Create(VisualIntelligenceOrchestrationContext c) => Result(c, ContractEventFamily.LunarEvent, "Make the named full Moon the calm premium hero subject with realistic phase, maria, and crater texture.", "Moon", "thin clouds, horizon silhouette, date and viewing cue", "large circular Moon with restrained maria/crater texture and calm premium full-moon tone", CompositionStyle.HeroSubject, "premium full Moon portrait", [D("familyCreativeDirection", "moon as hero subject; realistic phase/maria/craters; calm premium full-moon tone; avoid horoscope/zodiac style", 99)], DefaultNegative("avoid horoscope/zodiac style")); }
public sealed class SolarEclipseCreativeProfile : FamilyCreativeProfileBase { public override string ProfileName => nameof(SolarEclipseCreativeProfile); public override IReadOnlySet<ContractEventFamily> SupportedFamilies { get; } = new HashSet<ContractEventFamily> { ContractEventFamily.SolarEvent, ContractEventFamily.Eclipse }; public override bool Supports(VisualIntelligenceOrchestrationContext c) => TextHas(c, "solar") && TextHas(c, "eclipse") || c.EventFamily == ContractEventFamily.SolarEvent; public override FamilyCreativeProfileResult Create(VisualIntelligenceOrchestrationContext c) => Result(c, ContractEventFamily.SolarEvent, "Show eclipse geometry-safe solar alignment with corona, chromosphere, or diamond-ring guidance where appropriate.", "eclipsed Sun", "corona, chromosphere, diamond ring, lunar silhouette", "black lunar disk, circular solar rim, corona as subtle structure", CompositionStyle.CenteredSubject, "solar eclipse corona realism", [D("familyCreativeDirection", "eclipse geometry-safe; corona/chromosphere/diamond ring guidance; no fake fire or impossible glow; safety/observation card friendly", 99)], DefaultNegative("no fake fire", "no impossible glow", "no unsafe solar viewing")); }
public sealed class LunarEclipseCreativeProfile : FamilyCreativeProfileBase { public override string ProfileName => nameof(LunarEclipseCreativeProfile); public override IReadOnlySet<ContractEventFamily> SupportedFamilies { get; } = new HashSet<ContractEventFamily> { ContractEventFamily.LunarEvent, ContractEventFamily.Eclipse }; public override bool Supports(VisualIntelligenceOrchestrationContext c) => TextHas(c, "lunar") && TextHas(c, "eclipse"); public override FamilyCreativeProfileResult Create(VisualIntelligenceOrchestrationContext c) => Result(c, ContractEventFamily.LunarEvent, "Present the lunar eclipse with umbra and penumbra awareness and a natural copper/red Moon tone.", "Moon", "Earth umbra, penumbra gradient, subtle stars, observation card", "red Moon or partial shadow remains circular with physically plausible shading and moon-focused hierarchy", CompositionStyle.CenteredSubject, "lunar eclipse realism", [D("familyCreativeDirection", "umbra/penumbra aware; natural copper/red moon tone; no artificial neon red glow; moon-focused hierarchy", 99)], DefaultNegative("no artificial neon red glow")); }
public sealed class GenericAstronomyCreativeProfile : FamilyCreativeProfileBase { public override string ProfileName => nameof(GenericAstronomyCreativeProfile); public override IReadOnlySet<ContractEventFamily> SupportedFamilies { get; } = new HashSet<ContractEventFamily> { ContractEventFamily.Unknown }; public override bool Supports(VisualIntelligenceOrchestrationContext c) => true; public override FamilyCreativeProfileResult Create(VisualIntelligenceOrchestrationContext c) => Result(c, c.EventFamily, "Create a generic premium astronomy documentary visual with minimal safe assumptions.", Normalize(c.PrimaryObjects).FirstOrDefault() ?? "night sky event", "night sky, subtle labels, observation context", "single clear hero astronomy subject with supporting context", CompositionStyle.HeroSubject, "premium astronomy documentary fallback", [D("familyCreativeDirection", "premium documentary astronomy fallback; minimal safe assumptions; warning diagnostic", 99)]); }

internal static class FamilyCreativeProfileDiagnostics
{
    public static void Add(this IList<DiagnosticMessage> diagnostics, DiagnosticSeverity severity, string code, string message, string source) => diagnostics.Add(new DiagnosticMessage { Severity = severity, Code = code, Message = message, Source = source });
}
