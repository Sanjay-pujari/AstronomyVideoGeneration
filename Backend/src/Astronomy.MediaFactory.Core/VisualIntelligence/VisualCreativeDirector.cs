using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed class VisualCreativeDirector : IVisualCreativeDirector
{
    private readonly ILogger<VisualCreativeDirector> logger;

    public VisualCreativeDirector(ILogger<VisualCreativeDirector> logger) => this.logger = logger;

    public Task<VisualCreativeDirectorResult> CreateDirectionAsync(VisualIntelligenceOrchestrationContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<DiagnosticMessage> { Info("visual_director.started", "VisualCreativeDirector started.") };
        var family = ResolveFamily(context, diagnostics);
        logger.LogInformation("VisualCreativeDirector family resolved. CorrelationId={CorrelationId} EventFamily={EventFamily}", context.CorrelationId, family);

        var model = BuildModel(context, family);
        var cdl = context.FeatureFlags.UseCDL ? BuildCdl(context, model) : null;
        if (cdl is not null)
        {
            diagnostics.Add(Info("visual_director.cdl_generated", "Creative Direction Language generated."));
            logger.LogInformation("VisualCreativeDirector CDL generated. CorrelationId={CorrelationId} DirectiveCount={DirectiveCount}", context.CorrelationId, cdl.Directives.Count);
        }

        var contract = context.FeatureFlags.UseCreativeDirectionContract ? BuildContract(context, model, cdl) : null;
        if (contract is not null)
        {
            diagnostics.Add(Info("visual_director.contract_generated", "CreativeDirectionContract generated."));
            logger.LogInformation("VisualCreativeDirector contract generated. CorrelationId={CorrelationId} ContractId={ContractId}", context.CorrelationId, contract.ContractId);
        }

        return Task.FromResult(new VisualCreativeDirectorResult { Cdl = cdl, CreativeDirectionContract = contract, Diagnostics = diagnostics });
    }

    private static DirectionModel BuildModel(VisualIntelligenceOrchestrationContext context, ContractEventFamily family)
    {
        var primary = NormalizeObjects(context.PrimaryObjects).DefaultIfEmpty(DefaultPrimary(context, family)).ToList();
        var supporting = NormalizeObjects(context.SupportingObjects).ToList();
        var aspect = context.AspectRatio == AspectRatio.Unknown ? DefaultAspectRatio(context.Platform) : context.AspectRatio;
        var platform = context.Platform == Platform.Unknown ? Platform.YouTubeThumbnail : context.Platform;
        DirectionModel model = family switch
        {
            ContractEventFamily.PlanetConjunction => new(family, primary, supporting, "Show the close apparent pairing of planets as a premium astronomy documentary moment.", string.Join(" and ", primary), "nearby planetary companion(s), twilight gradient, clean star field", "multi-object hierarchy with bright circular planetary disks, clear separation, and primary/secondary emphasis", CompositionStyle.RuleOfThirds, "cinematic realistic planet pairing"),
            ContractEventFamily.MeteorShower => new(family, primary, supporting, "Convey meteor activity from a dark sky with radiant awareness and observation usefulness.", primary[0], "radiant region, sparse meteor streaks, horizon context", "radiant and sky dome first, meteor streaks supporting not chaotic", CompositionStyle.WideNegativeSpace, "dark-sky meteor shower documentary"),
            ContractEventFamily.LunarEvent when ContainsAny(context, "eclipse") => new(family, primary, supporting, "Present the lunar eclipse with umbra structure and a restrained red Moon treatment.", "Moon", "Earth umbra, subtle stars, observation card", "red Moon or partial shadow remains circular with physically plausible shading", CompositionStyle.CenteredSubject, "lunar eclipse realism"),
            ContractEventFamily.LunarEvent => new(family, primary, supporting, "Make the named full Moon the calm hero subject with scientifically respectful atmosphere.", "Moon", "thin clouds, horizon silhouette, date and viewing cue", "large circular Moon with restrained texture and readable negative space", CompositionStyle.HeroSubject, "premium full Moon portrait"),
            ContractEventFamily.SolarEvent => new(family, primary, supporting, "Show a solar eclipse safely with corona-focused documentary realism and no unsafe viewing cues.", "eclipsed Sun", "corona, lunar silhouette, minimal sky context", "black lunar disk, circular solar rim, corona as subtle structure", CompositionStyle.CenteredSubject, "solar eclipse corona realism"),
            _ => new(family, primary, supporting, "Create a generic premium astronomy documentary visual that explains the sky event clearly.", primary[0], "night sky, subtle labels, observation context", "single clear hero astronomy subject with supporting context", CompositionStyle.HeroSubject, "premium astronomy documentary")
        };

        return model with { Platform = platform, AspectRatio = aspect };
    }

    private static CDL BuildCdl(VisualIntelligenceOrchestrationContext c, DirectionModel m) => new()
    {
        DocumentId = $"cdl_{c.CorrelationId}",
        Directives =
        [
            D("creativeIntent", m.Intent, 100), D("heroSubject", m.Hero, 95), D("supportingSubjects", m.SupportingText, 90),
            D("visualHierarchy", m.Hierarchy, 90), D("composition", $"{m.CompositionStyle}; mobile-first safe zones; platform={m.Platform}", 85),
            D("framing", m.AspectRatio == AspectRatio.Portrait9x16 ? "vertical framing with central readable subject" : "cinematic widescreen framing with clean negative space", 80),
            D("lighting", "cinematic low-noise astrophotography lighting; physically plausible illumination where possible", 80),
            D("atmosphere", "calm, trustworthy, scientific but emotional; premium astronomy documentary", 75),
            D("typography", "minimal essential text, high contrast, mobile-first readability, no clutter", 75),
            D("observationCard", BuildObservationCard(c), 70), D("labels", "short factual labels only when helpful; subtle constellation overlays if used", 65),
            D("astronomicalRendering", "planets and moons perfectly circular; realistic telescope/astrophotography treatment; no stretched bodies; no fake glow; no cartoon planets; correct illumination where possible", 100),
            D("brandDesign", "Drashyam premium: cinematic, calm, trustworthy, not generic AI poster, not horoscope or zodiac style", 95),
            D("negativeConstraints", "no distorted planets, fake glow, cartoon planets, astrology symbols, cluttered poster text, unsafe solar viewing cues", 100),
            D("qualityTargets", "prioritize astronomical plausibility, brand compliance, text readability, platform suitability", 80),
            D("providerHints", "directional hints only; do not generate provider prompts in VisualCreativeDirector", 10)
        ],
        ExtensionFields = BuildCommonExtensions(c, m)
    };

    private static CreativeDirectionContract BuildContract(VisualIntelligenceOrchestrationContext c, DirectionModel m, CDL? cdl) => new()
    {
        ContractId = $"cdc_{c.CorrelationId}", SourceEventId = c.CorrelationId, EventFamily = m.Family, TargetPlatform = m.Platform, Language = Safe(c.Language, "en"), AspectRatio = m.AspectRatio,
        VisualIntent = new VisualIntent { PrimarySubject = m.Hero, SecondarySubjects = m.SupportingObjects, NarrativeRole = "event-intelligence-to-creative-direction", Mood = "scientific but emotional, cinematic, calm and trustworthy", Composition = m.Hierarchy, CreativeStyle = CreativeStyle.PremiumDocumentary, CompositionStyle = m.CompositionStyle },
        Cdl = cdl ?? BuildCdl(c, m), BrandRules = BrandRules(), PlanetRenderingRules = RenderingRules(m), TypographyRules = TypographyRules(), ObservationCardRules = ObservationRules(), ProviderHints = new ProviderHints { PromptStyle = "not-generated", RenderingHints = new Dictionary<string, object?> { ["visualDirectorOnly"] = true } }, QualityTargets = QualityTargets(), NegativeConstraints = NegativeRules(),
        ExtensionFields = BuildCommonExtensions(c, m)
    };

    private static ContractEventFamily ResolveFamily(VisualIntelligenceOrchestrationContext c, List<DiagnosticMessage> d)
    {
        var text = $"{c.EventFamily} {c.EventType} {c.EventName}".ToLowerInvariant();
        var resolved = c.EventFamily != ContractEventFamily.Unknown ? c.EventFamily : text switch { var t when t.Contains("meteor") => ContractEventFamily.MeteorShower, var t when t.Contains("solar") && t.Contains("eclipse") => ContractEventFamily.SolarEvent, var t when t.Contains("lunar") && t.Contains("eclipse") => ContractEventFamily.LunarEvent, var t when t.Contains("full moon") || t.Contains("supermoon") || t.Contains("moon") => ContractEventFamily.LunarEvent, var t when t.Contains("conjunction") || t.Contains("pair") || t.Contains("grouping") || t.Contains("planet") => ContractEventFamily.PlanetConjunction, _ => ContractEventFamily.Unknown };
        d.Add(Info("visual_director.family_resolved", $"Event family resolved as {resolved}."));
        if (resolved == ContractEventFamily.Unknown) d.Add(new DiagnosticMessage { Severity = DiagnosticSeverity.Warning, Code = "visual_director.unknown_family", Message = "Unknown event family; generic astronomy documentary CDL used.", Source = nameof(VisualCreativeDirector) });
        return resolved;
    }

    private static BrandRules BrandRules() => new() { VisualTone = "premium astronomy documentary; scientific but emotional; cinematic; calm and trustworthy", StylePrinciples = ["not generic AI poster", "not horoscope/zodiac style", "mobile-first readability", "minimal clutter"], ColorPalette = new ColorPalette { Primary = ["deep space navy", "soft moon white"], Accent = ["muted gold", "cool cyan"], Avoid = ["neon rainbow", "astrology purple overload"] } };
    private static PlanetRenderingRules RenderingRules(DirectionModel m) => new() { EventFamily = m.Family, Subjects = m.PrimaryObjects.Select(o => new PlanetRenderingSubjectRule { BodyName = o, BodyType = BodyType(o), RequiredShape = "perfectly circular disk when resolved", ColorBehavior = "naturalistic", SurfaceDetail = "realistic telescope/astrophotography", Illumination = "physically plausible where possible", ScalePolicy = "do not stretch or distort", ForbiddenArtifacts = ["fake glow", "cartoon surface", "oval planet", "melted rings"] }).ToList(), BackgroundRules = new Dictionary<string, string> { ["stars"] = "subtle and not noisy", ["constellationOverlays"] = "subtle when used" } };
    private static TypographyRules TypographyRules() => new() { AllowedTextElements = ["event name", "date/time", "location", "viewing direction"], ForbiddenText = ["horoscope claims", "zodiac predictions", "clickbait clutter"], TitleRules = new Dictionary<string, object?> { ["mobileFirst"] = true }, LabelRules = new Dictionary<string, object?> { ["subtle"] = true } };
    private static ObservationCardRules ObservationRules() => new() { AllowedFields = ["date", "time", "location", "visibility"], VisualStyle = new Dictionary<string, object?> { ["treatment"] = "premium translucent lower-third" }, DataIntegrity = new Dictionary<string, object?> { ["noInventedObservationData"] = true } };
    private static NegativeConstraints NegativeRules() => new() { Scientific = ["no stretched or distorted planets", "no fake glow", "no cartoon planets", "no incorrect unsafe solar viewing cues"], Brand = ["not generic AI poster", "not horoscope/zodiac style"], Typography = ["no cluttered text", "no tiny unreadable labels"], Provider = ["no prompt generation in director"] };
    private static QualityTargets QualityTargets() => new() { Dimensions = [new() { Name = QualityCategory.AstronomicalPlausibility, MinimumScore = .86, Weight = .3, Blocking = true }, new() { Name = QualityCategory.BrandCompliance, MinimumScore = .84, Weight = .25 }, new() { Name = QualityCategory.TextReadability, MinimumScore = .82, Weight = .2 }, new() { Name = QualityCategory.PlatformSuitability, MinimumScore = .82, Weight = .25 }] };

    private static Dictionary<string, object?> BuildCommonExtensions(VisualIntelligenceOrchestrationContext c, DirectionModel m) => new() { ["eventType"] = c.EventType, ["eventName"] = c.EventName, ["region"] = c.Region, ["location"] = c.Location, ["observationDateTime"] = c.ObservationDateTime, ["visibilityGuidance"] = c.VisibilityGuidance, ["creativeStyle"] = CreativeStyle.PremiumDocumentary.ToString(), ["compositionStyle"] = m.CompositionStyle.ToString(), ["subjectTreatment"] = m.SubjectTreatment, ["typographyStyle"] = "premium minimal mobile-first", ["observationCardStyle"] = "lower-third safe-zone when useful", ["negativeRules"] = NegativeRules() };
    private static string BuildObservationCard(VisualIntelligenceOrchestrationContext c) => string.Join("; ", new[] { c.ObservationDateTime?.ToString("u") ?? "date/time if available", Safe(c.Location, Safe(c.Region, "viewer region if available")), Safe(c.VisibilityGuidance, "visibility guidance if available") });
    private static List<string> NormalizeObjects(IEnumerable<string> values) => values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static string DefaultPrimary(VisualIntelligenceOrchestrationContext c, ContractEventFamily f) { var text = $"{c.EventType} {c.EventName}".ToLowerInvariant(); if (text.Contains("jupiter") && text.Contains("venus")) return "Jupiter and Venus"; return f switch { ContractEventFamily.MeteorShower => "meteor shower radiant", ContractEventFamily.LunarEvent => "Moon", ContractEventFamily.SolarEvent => "eclipsed Sun", ContractEventFamily.PlanetConjunction => "bright planets", _ => "night sky event" }; }
    private static AspectRatio DefaultAspectRatio(Platform p) => p is Platform.YouTubeShorts or Platform.InstagramReel or Platform.FacebookReel ? AspectRatio.Portrait9x16 : AspectRatio.Landscape16x9;
    private static bool ContainsAny(VisualIntelligenceOrchestrationContext c, string value) => $"{c.EventType} {c.EventName}".Contains(value, StringComparison.OrdinalIgnoreCase);
    private static string BodyType(string o) => o.Contains("moon", StringComparison.OrdinalIgnoreCase) ? "moon" : o.Contains("sun", StringComparison.OrdinalIgnoreCase) ? "star" : o.Contains("meteor", StringComparison.OrdinalIgnoreCase) ? "meteor" : "planet";
    private static CdlDirective D(string name, string value, int priority) => new(name, value, priority);
    private static string Safe(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static DiagnosticMessage Info(string code, string message) => new() { Severity = DiagnosticSeverity.Info, Code = code, Message = message, Source = nameof(VisualCreativeDirector) };

    private sealed record DirectionModel(ContractEventFamily Family, List<string> PrimaryObjects, List<string> SupportingObjects, string Intent, string Hero, string SupportingText, string Hierarchy, CompositionStyle CompositionStyle, string SubjectTreatment)
    {
        public Platform Platform { get; init; }
        public AspectRatio AspectRatio { get; init; }
    }
}
