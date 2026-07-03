namespace Astronomy.MediaFactory.Core;

public sealed record ThumbnailPromptContract(
    string ContractVersion,
    ThumbnailEventIdentity EventIdentity,
    ThumbnailDisplay Display,
    ThumbnailObjects Objects,
    ThumbnailObservation Observation,
    ThumbnailVisual Visual,
    ThumbnailPlatform Platform,
    ThumbnailPromptInstructions Prompt,
    ThumbnailBrand Brand,
    ThumbnailValidation Validation,
    ThumbnailPromptDiagnostics Diagnostics,
    IReadOnlyList<PromptSection>? PromptSections = null,
    VisualDirectingProfile? VisualDirectingProfile = null,
    FamilyDirector? FamilyDirector = null);

public sealed record ThumbnailEventIdentity(string EventId, string EventName, string EventAction, string EventFamily, string EventSubtype);
public sealed record ThumbnailDisplay(string DisplayTitle, string LocalizedTitle, string DisplayShortTitle, IReadOnlyList<string> TitleCandidates);
public sealed record ThumbnailObjects(IReadOnlyList<string> PrimaryObjects, IReadOnlyList<string> SecondaryObjects, IReadOnlyDictionary<string, string> LocalizedObjectNames);
public sealed record ThumbnailObservation(ProductionObservationInfo? ObservationInfo, string ObservationWindow, string BestViewingWindow, string Direction, string Visibility);
public sealed record ThumbnailVisual(string VisualIdentity, string EmotionalTone, string EducationalIntent, string CtrGoal);
public sealed record ThumbnailPlatform(string Platform, string AspectRatio, string CompositionProfile, int Width, int Height);
public sealed record ThumbnailPromptInstructions(string PositivePrompt, string NegativePrompt, IReadOnlyList<string> RequiredObjects, IReadOnlyList<string> ForbiddenObjects);
public sealed record ThumbnailBrand(string TypographyPolicy, string BrandStyle, IReadOnlyList<string> LocalizationRules);
public sealed record ThumbnailValidation(IReadOnlyList<string> ValidationRules, IReadOnlyList<string> ScientificRules, IReadOnlyList<string> PlatformRules);
public sealed record ThumbnailPromptDiagnostics(string Source, string SelectedPromptBuilder, string SelectedFamilyTemplate, string PromptSummary, DateTimeOffset GeneratedUtc);


public sealed record VisualDirectingProfile(
    string Name,
    string CameraLanguage,
    string LensLanguage,
    string ArtisticComposition,
    string DocumentaryDirection,
    string DominantObjectStrategy,
    string EnvironmentalStorytelling,
    string NegativeSpaceGuidance,
    IReadOnlyList<string> PromptAdditions,
    IReadOnlyList<string> AntiDistortionRules)
{
    public string PromptGuidance => string.Join(" ", PromptAdditions.Concat(AntiDistortionRules));
}

public sealed record FamilyDirector(
    string Name,
    string Family,
    IReadOnlyList<string> ArtisticVocabulary,
    IReadOnlyList<string> PromptAdditions)
{
    public string PromptGuidance => string.Join(" ", PromptAdditions);
}

public static class VisualDirectingProfiles
{
    public static VisualDirectingProfile Resolve(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return contract.VisualDirectingProfile ?? Resolve(contract.Platform.CompositionProfile, contract.Platform.AspectRatio);
    }

    public static VisualDirectingProfile Resolve(string profileName, string aspectRatio)
    {
        var normalizedProfile = Normalize(profileName);
        var normalizedAspect = Normalize(aspectRatio);
        if (normalizedProfile.Contains("landscape", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "16:9" or "16x9") return LandscapeDirector;
        if (normalizedProfile.Contains("portrait", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "9:16" or "9x16") return PortraitDirector;
        if (normalizedProfile.Contains("square", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "1:1" or "1x1") return SquareDirector;
        throw new InvalidOperationException($"Visual directing profile validation failed: unsupported profile '{profileName}' for aspect ratio '{aspectRatio}'.");
    }

    public static readonly IReadOnlyList<string> UniversalAntiDistortionRules =
    [
        "ANTI-DISTORTION: compose natively for the requested aspect ratio; never stretch, squeeze, pad, or crop another ratio.",
        "ANTI-DISTORTION: no stretched landscape, no squeezed portrait, no cropped square.",
        "ANTI-DISTORTION: all celestial bodies must remain circular with physically correct astronomical geometry."
    ];

    public static readonly VisualDirectingProfile LandscapeDirector = new("LandscapeDirector", "wide documentary establishing camera, horizon-aware lateral staging", "moderate wide cinema lens language with natural perspective", "intentionally wide left-to-right cinematic composition", "premium observational documentary still, calm but high-salience", "dominant subject uses width without becoming distorted", "landscape silhouette, horizon glow, and sky scale tell the viewing story", "reserve broad edge-safe breathing room for renderer-owned presentation", ["VISUAL DIRECTING PROFILE: LandscapeDirector.", "Compose as an intentionally wide 16:9 astronomy documentary frame with lateral sky scale and horizon context."], UniversalAntiDistortionRules);
    public static readonly VisualDirectingProfile PortraitDirector = new("PortraitDirector", "vertical mobile-first documentary camera with foreground-to-sky depth", "portrait telephoto/compressed depth language without squeezing objects", "intentionally vertical stacked composition with tall sky hierarchy", "phone-first observational documentary poster still", "dominant subject anchors the vertical frame and is not a landscape crop", "vertical atmosphere, horizon-to-zenith depth, and layered sky tell the story", "keep top and bottom breathing room; avoid side-panel landscape logic", ["VISUAL DIRECTING PROFILE: PortraitDirector.", "Compose as an intentionally vertical 9:16 mobile astronomy frame; never derive it from a landscape composition."], UniversalAntiDistortionRules);
    public static readonly VisualDirectingProfile SquareDirector = new("SquareDirector", "balanced centered documentary camera", "natural standard lens language with minimal edge distortion", "intentionally balanced 1:1 radial or centered composition", "compact feed-ready observational documentary still", "dominant subject is centered or center-weighted and fully visible", "environment supports symmetry and compact context", "balanced negative space on all sides; no cropped square feel", ["VISUAL DIRECTING PROFILE: SquareDirector.", "Compose as an intentionally balanced native 1:1 astronomy frame with fully visible celestial geometry."], UniversalAntiDistortionRules);

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace('_', ':');
}

public static class FamilyDirectors
{
    public static FamilyDirector Resolve(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return contract.FamilyDirector ?? Resolve(contract.EventIdentity.EventFamily);
    }

    public static FamilyDirector Resolve(string family)
    {
        var normalized = (family ?? string.Empty).Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (normalized.Contains("Eclipse", StringComparison.OrdinalIgnoreCase)) return EclipseDirector;
        if (normalized.Contains("Moon", StringComparison.OrdinalIgnoreCase)) return MoonDirector;
        if (normalized.Contains("Meteor", StringComparison.OrdinalIgnoreCase)) return MeteorDirector;
        return PlanetaryDirector;
    }

    public static readonly FamilyDirector EclipseDirector = new("EclipseDirector", "Eclipse", ["corona", "alignment", "limb light", "umbra", "dramatic sky"], ["FAMILY DIRECTOR: EclipseDirector contributes art vocabulary only: precise Sun-Moon-Earth alignment, corona/umbra drama, physically correct eclipse geometry."]);
    public static readonly FamilyDirector MoonDirector = new("MoonDirector", "Moon", ["lunar maria", "terminator detail", "silver texture", "moonrise atmosphere"], ["FAMILY DIRECTOR: MoonDirector contributes art vocabulary only: detailed lunar surface texture, circular Moon disk, atmospheric moonrise, calm silver contrast."]);
    public static readonly FamilyDirector MeteorDirector = new("MeteorDirector", "Meteor", ["radiant", "natural streaks", "dark sky", "anticipation", "wide sky"], ["FAMILY DIRECTOR: MeteorDirector contributes art vocabulary only: radiant-centered meteor streaks, natural dark-sky atmosphere, directional energy without invented planets."]);
    public static readonly FamilyDirector PlanetaryDirector = new("PlanetaryDirector", "Planetary", ["planetary pairing", "twilight ecliptic", "scale contrast", "clean separation"], ["FAMILY DIRECTOR: PlanetaryDirector contributes art vocabulary only: realistic planetary disks/points, twilight ecliptic spacing, clean conjunction hierarchy, no extra planets."]);
}

public sealed record PromptSection(
    string Id,
    string Category,
    int Priority,
    string Content,
    bool Required,
    IReadOnlyList<string>? SupportedPlatforms = null,
    IReadOnlyList<string>? SupportedLanguages = null,
    IReadOnlyList<string>? SupportedFamilies = null);

public sealed record PromptAssemblyReport(
    string Event,
    string Family,
    string Platform,
    string Language,
    string CompositionProfile,
    string StorytellingStrategy,
    IReadOnlyList<string> IncludedSections,
    IReadOnlyList<string> ExcludedSections,
    IReadOnlyDictionary<string, string> RemovedSectionsReason,
    int FinalPromptLength,
    int FinalPromptWordCount);

public sealed record ThumbnailPromptBuildResult(string Prompt, string NegativePrompt, PlatformStorytellingStrategy StorytellingStrategy, PromptAssemblyReport? AssemblyReport = null);

public sealed record ThumbnailCreativeDirection(
    string Hook,
    string EmotionalAngle,
    string CtrIntent,
    string ObjectProminence,
    string FamilyVisualStyle,
    string PlatformAspectCreativeStrategy);

public sealed class ThumbnailCreativeDirector
{
    public ThumbnailCreativeDirection Direct(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var strategy = PlatformStorytellingStrategies.Resolve(contract);
        var profile = ThumbnailCompositionProfiles.Resolve(contract);
        var family = FamilyDirectors.Resolve(contract);
        var objectProminence = contract.Platform.AspectRatio switch
        {
            "9:16" => "Portrait object prominence: native Shorts/Reels hero object at least 35% of the visual focus.",
            "1:1" => "Square object prominence: native feed hero object at least 25% of the visual focus.",
            _ => "Landscape object prominence: rich YouTube/social cover hero object at 25-45% visual focus."
        };

        return new ThumbnailCreativeDirection(
            contract.Display.DisplayTitle,
            contract.Visual.EmotionalTone,
            contract.Visual.CtrGoal,
            objectProminence,
            $"{family.Name}: {family.PromptGuidance}",
            $"{profile.Name} + {strategy.Name}: {profile.PromptGuidance} {strategy.PromptGuidance}");
    }
}

public sealed class ThumbnailPromptComposerV1
{
    private readonly ThumbnailCreativeDirector _creativeDirector = new();

    public ThumbnailPromptBuildResult Compose(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var direction = _creativeDirector.Direct(contract);
        var result = new PromptAssembler().Assemble(contract);
        var prompt = string.Join(Environment.NewLine,
            "THUMBNAIL CREATIVE DIRECTOR:",
            $"- Hook: {direction.Hook}",
            $"- Emotional angle: {direction.EmotionalAngle}",
            $"- CTR intent: {direction.CtrIntent}",
            $"- Object prominence: {direction.ObjectProminence}",
            $"- Family visual style: {direction.FamilyVisualStyle}",
            $"- Platform/aspect creative strategy: {direction.PlatformAspectCreativeStrategy}",
            result.Prompt);
        return result with { Prompt = prompt };
    }
}

public sealed record CompositionProfile(
    string Name,
    string AspectRatio,
    IReadOnlyList<string> PlatformTargets,
    IReadOnlyList<string> CompositionGuidance,
    IReadOnlyList<string> PromptAdditions,
    IReadOnlyList<string> ValidationNotes)
{
    public string PromptGuidance => string.Join(" ", PromptAdditions);
}

public static class ThumbnailCompositionProfiles
{
    public static CompositionProfile Resolve(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return Resolve(contract.Platform.CompositionProfile, contract.Platform.AspectRatio);
    }

    public static CompositionProfile Resolve(string profileName, string aspectRatio)
    {
        var normalizedProfile = Normalize(profileName);
        var normalizedAspect = Normalize(aspectRatio);
        if (normalizedProfile.Contains("landscape", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "16:9" or "16x9") return LandscapeProfile;
        if (normalizedProfile.Contains("portrait", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "9:16" or "9x16") return PortraitProfile;
        if (normalizedProfile.Contains("square", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "1:1" or "1x1") return SquareProfile;
        throw new InvalidOperationException($"Thumbnail composition profile validation failed: unsupported profile '{profileName}' for aspect ratio '{aspectRatio}'.");
    }

    public static readonly CompositionProfile LandscapeProfile = new("LandscapeProfile", "16:9", ["YouTube", "Website"], ["wide cinematic composition", "balanced horizon", "strong negative space", "object occupies roughly 35-45% of frame", "safe text area", "layout guidance only"], ["COMPOSITION PROFILE: LandscapeProfile for 16:9 YouTube and website surfaces.", "Use wide cinematic framing, balanced horizon, strong negative space, safe text area, and premium documentary layout guidance.", "Keep the dominant event object roughly 35-45% of the frame without squeezing, padding, or borrowing another aspect-ratio layout."], ["Landscape prompt must be generated natively for 16:9.", "CompositionProfile controls framing, negative space, object dominance, safe text area, and layout guidance only."]);
    public static readonly CompositionProfile PortraitProfile = new("PortraitProfile", "9:16", ["Shorts", "Instagram Reels"], ["vertical composition", "dominant subject", "large foreground object", "layered depth", "safe text area", "avoid squeezed landscape look"], ["COMPOSITION PROFILE: PortraitProfile for 9:16 Shorts and Instagram Reels surfaces.", "Use vertical cinematic framing, dominant foreground subject, layered depth, natural vertical framing, safe text areas, and phone-first layout guidance.", "Preserve sky storytelling while avoiding any squeezed or cropped landscape look."], ["Portrait prompt must be generated natively for 9:16.", "CompositionProfile does not decide which sections exist."]);
    public static readonly CompositionProfile SquareProfile = new("SquareProfile", "1:1", ["Facebook", "Mobile"], ["balanced composition", "centered hierarchy", "symmetrical framing", "safe text area", "no cropped dominant object"], ["COMPOSITION PROFILE: SquareProfile for 1:1 Facebook and mobile feed surfaces.", "Use centered balanced composition, equal visual weight, centered hierarchy, symmetrical framing, and compact safe text areas optimized for feed browsing.", "Keep the dominant object fully visible without cropped dominance or borrowed landscape/portrait layout."], ["Square prompt must be generated natively for 1:1.", "CompositionProfile controls layout only."]);
    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace('_', ':');
}

public sealed record PlatformStorytellingStrategy(string Name, string InformationDensity, IReadOnlyList<string> PlatformTargets, IReadOnlyList<string> AllowedSections, int MaximumTextBudget, int MaximumIconCount, string FooterPolicy, string ObservationCardPolicy, string CtaPolicy, IReadOnlyList<string> PromptAdditions, int? MaximumInformationItems = null)
{
    public bool FooterEnabled => FooterPolicy.Contains("enabled", StringComparison.OrdinalIgnoreCase) || FooterPolicy.Contains("allowed", StringComparison.OrdinalIgnoreCase);
    public bool ObservationCardEnabled => ObservationCardPolicy.Contains("enabled", StringComparison.OrdinalIgnoreCase) || ObservationCardPolicy.Contains("allowed", StringComparison.OrdinalIgnoreCase);
    public string PromptGuidance => string.Join(" ", PromptAdditions);
    public bool AllowsCategory(string category) => AllowedSections.Any(s => string.Equals(NormalizeSection(s), NormalizeSection(category), StringComparison.OrdinalIgnoreCase));
    private static string NormalizeSection(string value) => (value ?? string.Empty).Replace("Very large ", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("One ", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("Maximum one ", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("Compact ", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("celestial ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
}

public static class PlatformStorytellingStrategies
{
    public static PlatformStorytellingStrategy Resolve(ThumbnailPromptContract contract) { ArgumentNullException.ThrowIfNull(contract); return Resolve(contract.Platform.CompositionProfile, contract.Platform.AspectRatio); }
    public static PlatformStorytellingStrategy Resolve(string profileName, string aspectRatio)
    {
        var normalizedProfile = Normalize(profileName); var normalizedAspect = Normalize(aspectRatio);
        if (normalizedProfile.Contains("landscape", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "16:9" or "16x9") return LandscapeStrategy;
        if (normalizedProfile.Contains("portrait", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "9:16" or "9x16") return PortraitStrategy;
        if (normalizedProfile.Contains("square", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "1:1" or "1x1") return SquareStrategy;
        throw new InvalidOperationException($"Thumbnail storytelling strategy validation failed: unsupported profile '{profileName}' for aspect ratio '{aspectRatio}'.");
    }
    public static readonly PlatformStorytellingStrategy LandscapeStrategy = new("LandscapeStrategy", "Rich", ["YouTube", "Website"], ["Title", "Subtitle", "Observation Card", "Equipment", "Safety", "Footer", "CTA", "Dominant Object", "Visual Scene", "Quality Rules"], 42, 6, "Footer allowed", "Observation card allowed", "CTA allowed when secondary to educational content", ["STORYTELLING STRATEGY: LandscapeStrategy decides WHAT information exists for rich educational YouTube and website thumbnails.", "Allowed sections: Title, Subtitle, Observation Card, Equipment, Safety, Footer, CTA.", "Keep educational richness while maintaining mobile readability."], null);
    public static readonly PlatformStorytellingStrategy PortraitStrategy = new("PortraitStrategy", "Minimal", ["YouTube Shorts", "Instagram Reels"], ["Title", "Subtitle", "Dominant Object", "One Observation Hint", "CTA", "Visual Scene", "Quality Rules"], 14, 2, "Footer disabled", "Observation table disabled; maximum one observation hint", "CTA allowed only as short phone-first action cue", ["STORYTELLING STRATEGY: PortraitStrategy decides WHAT information exists for phone-first Shorts and Reels thumbnails.", "Allowed sections: Title, Subtitle, Dominant Object, One Observation Hint, CTA.", "Forbidden: Observation Table, Equipment Table, Footer, Dense Infographic."], 1);
    public static readonly PlatformStorytellingStrategy SquareStrategy = new("SquareStrategy", "Balanced", ["Facebook", "Mobile feed"], ["Title", "Subtitle", "Compact Observation", "CTA", "Dominant Object", "Visual Scene", "Quality Rules"], 24, 4, "Footer disabled", "Compact observation enabled with maximum two information items", "CTA allowed when compact and feed optimized", ["STORYTELLING STRATEGY: SquareStrategy decides WHAT information exists for balanced mobile-feed thumbnails.", "Allowed sections: Title, Subtitle, Compact Observation, CTA.", "Maximum two information items; do not reuse landscape density."], 2);
    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace('_', ':');
}

public sealed class PromptAssembler
{
    public ThumbnailPromptBuildResult Assemble(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract); ThumbnailPromptContractValidator.Validate(contract);
        var profile = ThumbnailCompositionProfiles.Resolve(contract); var strategy = PlatformStorytellingStrategies.Resolve(contract); var directing = VisualDirectingProfiles.Resolve(contract); var familyDirector = FamilyDirectors.Resolve(contract);
        var sections = contract.PromptSections is { Count: > 0 } ? contract.PromptSections : BuildLegacySections(contract);
        var removed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var included = new List<PromptSection>();
        foreach (var section in sections.OrderBy(s => s.Priority).ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!Supports(section.SupportedPlatforms, contract.Platform.CompositionProfile, contract.Platform.AspectRatio, contract.Platform.Platform)) { removed[section.Id] = "Unsupported platform/profile/aspect ratio"; continue; }
            if (!Supports(section.SupportedLanguages, contract.Brand.LocalizationRules.FirstOrDefault() ?? string.Empty)) { removed[section.Id] = "Unsupported language"; continue; }
            if (!Supports(section.SupportedFamilies, contract.EventIdentity.EventFamily)) { removed[section.Id] = "Unsupported family"; continue; }
            if (!strategy.AllowsCategory(section.Category)) { removed[section.Id] = $"Section category '{section.Category}' is not allowed by {strategy.Name}"; continue; }
            included.Add(section);
        }
        EnforceInformationBudget(strategy, included, removed);
        var prompt = string.Join(Environment.NewLine, included.Select(s => $"{s.Category.ToUpperInvariant()}: {s.Content.Trim()}"));
        prompt = string.Join(Environment.NewLine, prompt, strategy.PromptGuidance, profile.PromptGuidance, directing.PromptGuidance, familyDirector.PromptGuidance, ThumbnailArtworkPromptRules.PositiveArtworkOnlyInstruction).Trim();
        PromptValidator.Validate(contract, included, prompt, strategy);
        var report = new PromptAssemblyReport(contract.EventIdentity.EventName, contract.EventIdentity.EventFamily, contract.Platform.CompositionProfile, contract.Brand.LocalizationRules.FirstOrDefault() ?? string.Empty, profile.Name, strategy.Name, included.Select(s => s.Id).ToArray(), sections.Select(s => s.Id).Except(included.Select(s => s.Id), StringComparer.OrdinalIgnoreCase).ToArray(), removed, prompt.Length, CountWords(prompt));
        return new ThumbnailPromptBuildResult(prompt, AppendArtworkNegativeRules(contract.Prompt.NegativePrompt), strategy, report);
    }

    private static string AppendArtworkNegativeRules(string negativePrompt)
    {
        const string antiDistortion = "native aspect composition, no stretched landscape, no squeezed portrait, no cropped square, circular celestial bodies, physically correct astronomical geometry";
        return string.IsNullOrWhiteSpace(negativePrompt) ? ThumbnailArtworkPromptRules.NegativePrompt + ", " + antiDistortion : negativePrompt + ", " + ThumbnailArtworkPromptRules.NegativePrompt + ", " + antiDistortion;
    }

    private static IReadOnlyList<PromptSection> BuildLegacySections(ThumbnailPromptContract contract) =>
    [
        new PromptSection("legacy-title", "Title", 10, contract.Display.DisplayTitle, true),
        new PromptSection("legacy-dominant-object", "Dominant Object", 20, string.Join(" + ", contract.Objects.PrimaryObjects), true),
        new PromptSection("legacy-observation-card", "Observation Card", 30, $"{contract.Observation.BestViewingWindow}; {contract.Observation.Direction}; {contract.Observation.Visibility}", true),
        new PromptSection("legacy-one-observation-hint", "One Observation Hint", 30, $"{contract.Observation.Direction} / {contract.Observation.BestViewingWindow}", false),
        new PromptSection("legacy-compact-observation", "Compact Observation", 30, $"{contract.Observation.Direction} / {contract.Observation.BestViewingWindow}", false),
        new PromptSection("legacy-positive-prompt", "Quality Rules", 100, contract.Prompt.PositivePrompt, true)
    ];

    private static bool Supports(IReadOnlyList<string>? allowed, params string[] values) => allowed is null || allowed.Count == 0 || allowed.Any(a => values.Any(v => !string.IsNullOrWhiteSpace(v) && (string.Equals(a, v, StringComparison.OrdinalIgnoreCase) || v.Contains(a, StringComparison.OrdinalIgnoreCase) || a.Contains(v, StringComparison.OrdinalIgnoreCase))));
    private static void EnforceInformationBudget(PlatformStorytellingStrategy strategy, List<PromptSection> included, Dictionary<string, string> removed)
    {
        if (strategy.MaximumInformationItems is not int max) return;
        var info = included.Where(s => IsInformation(s.Category)).OrderBy(s => s.Priority).ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var section in info.Skip(max)) { included.Remove(section); removed[section.Id] = $"Information density exceeds {strategy.Name} maximum of {max} item(s)"; }
    }
    private static bool IsInformation(string category) => category.Contains("Observation", StringComparison.OrdinalIgnoreCase) || category.Contains("Equipment", StringComparison.OrdinalIgnoreCase) || category.Contains("Safety", StringComparison.OrdinalIgnoreCase) || category.Contains("Footer", StringComparison.OrdinalIgnoreCase);
    private static int CountWords(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

public sealed class ThumbnailPromptBuilder { public ThumbnailPromptBuildResult Build(ThumbnailPromptContract contract) => new ThumbnailPromptComposerV1().Compose(contract); }

public static class PromptValidator
{
    public static void Validate(ThumbnailPromptContract contract, IReadOnlyList<PromptSection> sections, string finalPrompt, PlatformStorytellingStrategy strategy)
    {
        if (sections.Count == 0) throw new InvalidOperationException("Prompt validation failed: no prompt sections remained after assembly.");
        if (strategy.Name == "LandscapeStrategy" && !sections.Any(s => s.Category.Equals("Observation Card", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Prompt validation failed: Landscape observation card missing.");
        if (strategy.Name == "PortraitStrategy")
        {
            FailIfPresent(sections, "Footer", "Portrait footer exists."); FailIfPresent(sections, "Equipment", "Portrait equipment table exists."); FailIfPresent(sections, "Observation Card", "Portrait observation table exists.");
        }
        if (strategy.Name == "SquareStrategy" && sections.Count(s => s.Category.Contains("Observation", StringComparison.OrdinalIgnoreCase) || s.Category.Contains("Equipment", StringComparison.OrdinalIgnoreCase) || s.Category.Contains("Safety", StringComparison.OrdinalIgnoreCase) || s.Category.Contains("Footer", StringComparison.OrdinalIgnoreCase)) > 2) throw new InvalidOperationException("Prompt validation failed: Square information density exceeds strategy.");
        if (ContainsAny(finalPrompt, "crop landscape")) throw new InvalidOperationException("Prompt validation failed: contradictory prompt instructions remain.");
        if (ContainsAny(finalPrompt, "AI image is NOT the final thumbnail", "will be added later", "background-only image", "background artwork only", "do not render text")) throw new InvalidOperationException("Prompt validation failed: prompt is not complete-thumbnail mode.");
        if (!ContainsAny(finalPrompt, "Generate final finished thumbnail image") || !ContainsAny(finalPrompt, "The AI image must be the complete final thumbnail") || !ContainsAny(finalPrompt, "No post-processing overlay will be added")) throw new InvalidOperationException("Prompt validation failed: complete-thumbnail instruction missing.");
        if (!ContainsAny(finalPrompt, contract.Display.DisplayTitle, contract.Display.LocalizedTitle)) throw new InvalidOperationException("Prompt validation failed: title text missing.");
        if (!ContainsAny(finalPrompt, "LOCALIZED SUBTITLE TO RENDER", "SUBTITLE:")) throw new InvalidOperationException("Prompt validation failed: subtitle text missing.");
        if (!ContainsAny(finalPrompt, $"{contract.Platform.Width}x{contract.Platform.Height}", $"OUTPUT SIZE: {contract.Platform.Width}x{contract.Platform.Height}")) throw new InvalidOperationException("Prompt validation failed: aspect output size missing.");
        if (!ContainsAny(finalPrompt, "ASPECT-SPECIFIC COMPOSITION", contract.Platform.AspectRatio, contract.Platform.CompositionProfile)) throw new InvalidOperationException("Prompt validation failed: aspect-specific composition missing.");
        if (!ContainsAny(finalPrompt, "OBJECT PROMINENCE RULE", "25-45%", "at least 25%", "at least 35%")) throw new InvalidOperationException("Prompt validation failed: object prominence rule missing.");
        if (!ContainsAny(finalPrompt, "TEXT READABILITY RULE", "mobile-readable")) throw new InvalidOperationException("Prompt validation failed: text readability rule missing.");
        if (!ContainsAny(finalPrompt, "TEXT STYLE RULE", "Natural title case", "typography")) throw new InvalidOperationException("Prompt validation failed: text style rule missing.");
        if (!ContainsAny(finalPrompt, "FAMILY-SPECIFIC TEMPLATE")) throw new InvalidOperationException("Prompt validation failed: family-specific template missing.");
        if (!ContainsAny(finalPrompt, "VISUAL SCENE")) throw new InvalidOperationException("Prompt validation failed: visual scene missing.");
        if (!ContainsAny(finalPrompt, "UI ARCHITECTURE")) throw new InvalidOperationException("Prompt validation failed: UI architecture missing.");
        if (!ContainsAny(finalPrompt, "LOCALIZED TEXT AND FACTS TO RENDER", "DATA TO RENDER")) throw new InvalidOperationException("Prompt validation failed: data to render missing.");
        if (!ContainsAny(finalPrompt, "QUALITY RULES")) throw new InvalidOperationException("Prompt validation failed: quality rules missing.");
        if (!ContainsAny(finalPrompt, "CTR INSTRUCTIONS")) throw new InvalidOperationException("Prompt validation failed: CTR instructions missing.");
        if (!ContainsAny(finalPrompt, "NEGATIVE RULES")) throw new InvalidOperationException("Prompt validation failed: negative rules missing.");
        if (!ContainsAny(finalPrompt, "LOCALIZED", "language", "Hindi", "English")) throw new InvalidOperationException("Prompt validation failed: localized language rules missing.");
    }
    private static void FailIfPresent(IReadOnlyList<PromptSection> sections, string category, string reason) { if (sections.Any(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Prompt validation failed: " + reason); }
    private static bool ContainsAny(string value, params string[] tokens) => tokens.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
}

public static class ThumbnailPromptContractValidator
{
    public static void Validate(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract); Require(contract.ContractVersion, nameof(contract.ContractVersion)); Require(contract.EventIdentity.EventId, "EventIdentity.EventId"); Require(contract.EventIdentity.EventName, "EventIdentity.EventName"); Require(contract.EventIdentity.EventFamily, "EventIdentity.EventFamily"); Require(contract.Display.DisplayTitle, "Display.DisplayTitle"); Require(contract.Display.LocalizedTitle, "Display.LocalizedTitle"); Require(contract.Display.DisplayShortTitle, "Display.DisplayShortTitle"); RequireAny(contract.Objects.PrimaryObjects, "Objects.PrimaryObjects"); Require(contract.Observation.ObservationWindow, "Observation.ObservationWindow"); Require(contract.Observation.BestViewingWindow, "Observation.BestViewingWindow"); Require(contract.Observation.Direction, "Observation.Direction"); Require(contract.Observation.Visibility, "Observation.Visibility"); Require(contract.Visual.VisualIdentity, "Visual.VisualIdentity"); Require(contract.Visual.EmotionalTone, "Visual.EmotionalTone"); Require(contract.Visual.EducationalIntent, "Visual.EducationalIntent"); Require(contract.Visual.CtrGoal, "Visual.CtrGoal"); Require(contract.Platform.Platform, "Platform.Platform"); Require(contract.Platform.AspectRatio, "Platform.AspectRatio"); Require(contract.Platform.CompositionProfile, "Platform.CompositionProfile"); Require(contract.Prompt.PositivePrompt, "Prompt.PositivePrompt"); Require(contract.Prompt.NegativePrompt, "Prompt.NegativePrompt"); RequireAny(contract.Prompt.RequiredObjects, "Prompt.RequiredObjects"); Require(contract.Brand.TypographyPolicy, "Brand.TypographyPolicy"); Require(contract.Brand.BrandStyle, "Brand.BrandStyle"); RequireAny(contract.Validation.ValidationRules, "Validation.ValidationRules"); RequireAny(contract.Validation.ScientificRules, "Validation.ScientificRules"); RequireAny(contract.Validation.PlatformRules, "Validation.PlatformRules");
    }
    private static void Require(string? value, string name) { if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"ThumbnailPromptContract validation failed: required field '{name}' is missing."); }
    private static void RequireAny(IReadOnlyCollection<string>? values, string name) { if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException($"ThumbnailPromptContract validation failed: required field '{name}' is missing."); }
}
