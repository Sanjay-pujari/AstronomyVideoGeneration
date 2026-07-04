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
public sealed record ThumbnailObservation(ProductionObservationInfo? ObservationInfo, string ObservationWindow, string BestViewingWindow, string Direction, string Visibility, PlanetaryThumbnailGuideCardDto? GuideCard = null);
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

    public static readonly VisualDirectingProfile LandscapeDirector = new("LandscapeDirector", "wide premium documentary cover camera, horizon-aware lateral staging", "natural cinema lens language with believable planetary scale", "editorial left-to-right composition with a dominant event pair and calm premium space", "National Geographic feature image with Netflix-documentary polish and YouTube clarity", "the event dominates first glance without distorting scientific proportions", "twilight horizon glow, clean sky gradient, and restrained landscape silhouette explain where to look", "reserve edge-safe breathing room for a title and one glass observation card only", ["VISUAL DIRECTING PROFILE: LandscapeDirector.", "Compose as a native 16:9 premium astronomy cover: recognizable event first, elegant horizon context second, concise viewing details third."], UniversalAntiDistortionRules);
    public static readonly VisualDirectingProfile PortraitDirector = new("PortraitDirector", "vertical mobile-first poster camera composed from the first frame for a 9:16 Shorts cover", "portrait premium-poster camera language that makes circular planets feel important through framing, perspective, and camera distance only", "premium mobile cover hierarchy: title first, beautiful circular Jupiter and Venus second, compact observation card third, large clean negative space throughout", "Apple keynote poster / Netflix documentary cover / National Geographic astronomy magazine polish", "the viewer recognizes the Jupiter-Venus event before reading, with both planets physically circular and never oval, elongated, stretched, or squeezed", "deep twilight atmosphere, horizon glow, and vertical sky depth make the event feel native to mobile", "protect only three zones: top title, middle planets, bottom micro badge; no side-panel or landscape logic", ["VISUAL DIRECTING PROFILE: PortraitDirector.", "Compose as an intentionally vertical 9:16 premium Shorts cover that would be impossible to crop into landscape.", "Portrait hierarchy: 1 premium title, 2 beautiful circular Jupiter and Venus, 3 compact observation card with Date, Best Time, Direction, Equipment, and Separation, 4 large clean negative space."], UniversalAntiDistortionRules);
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
            "9:16" => "Portrait object prominence: make the planets visually dominant through composition, framing, perspective, and camera distance while keeping them physically circular.",
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

public sealed class ThumbnailPromptTemplateRenderer
{
    private const string TemplateRoot = "docs/product/thumbnail-prompts";

    public string Render(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var aspectName = ResolveAspectName(contract);
        var template = File.ReadAllText(ResolveTemplatePath(aspectName));
        var language = NormalizeLanguage(contract.Brand.LocalizationRules.FirstOrDefault() ?? "en");
        var fields = ThumbnailFieldFormatter.Format(contract.Observation, language);
        var objectLabels = string.Join(", ", contract.Objects.PrimaryObjects.Select(o => contract.Objects.LocalizedObjectNames.TryGetValue(o, out var local) ? local : o));
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TITLE"] = contract.Display.LocalizedTitle,
            ["SUBTITLE"] = ResolveSubtitle(contract),
            ["DATE"] = fields.Date,
            ["BEST_TIME"] = fields.BestTime,
            ["DIRECTION"] = fields.Direction,
            ["EQUIPMENT"] = fields.Equipment,
            ["SEPARATION"] = fields.Separation ?? string.Empty,
            ["OBJECT_LABELS"] = objectLabels,
            ["LANGUAGE"] = language,
            ["OUTPUT_SIZE"] = $"{contract.Platform.Width}x{contract.Platform.Height}",
            ["ASPECT_RATIO"] = contract.Platform.AspectRatio,
            ["ASPECT_NAME"] = aspectName
        };

        foreach (var (key, value) in values)
            template = template.Replace("{{" + key + "}}", value, StringComparison.OrdinalIgnoreCase);

        return template.Trim();
    }

    public async Task<string> SaveAsync(ThumbnailPromptContract contract, string thumbnailRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailRoot);

        Directory.CreateDirectory(thumbnailRoot);
        var aspectName = ResolveAspectName(contract);
        var promptPath = Path.Combine(thumbnailRoot, $"thumbnail-{aspectName}-prompt.txt");
        await File.WriteAllTextAsync(promptPath, Render(contract), cancellationToken);
        return promptPath;
    }

    private static string ResolveAspectName(ThumbnailPromptContract contract)
    {
        var profile = contract.Platform.CompositionProfile;
        var aspect = contract.Platform.AspectRatio;
        if (profile.Contains("portrait", StringComparison.OrdinalIgnoreCase) || aspect is "9:16" or "9x16") return "portrait";
        if (profile.Contains("square", StringComparison.OrdinalIgnoreCase) || aspect is "1:1" or "1x1") return "square";
        if (profile.Contains("landscape", StringComparison.OrdinalIgnoreCase) || aspect is "16:9" or "16x9") return "landscape";
        throw new InvalidOperationException($"Thumbnail prompt template validation failed: unsupported aspect '{profile}' / '{aspect}'.");
    }

    private static string ResolveTemplatePath(string aspectName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, TemplateRoot, $"{aspectName}.master.prompt.md");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        var workingDirectoryCandidate = Path.Combine(Directory.GetCurrentDirectory(), TemplateRoot, $"{aspectName}.master.prompt.md");
        if (File.Exists(workingDirectoryCandidate)) return workingDirectoryCandidate;

        throw new FileNotFoundException($"Thumbnail master prompt template not found for aspect '{aspectName}'.", $"{aspectName}.master.prompt.md");
    }

    private static string ResolveSubtitle(ThumbnailPromptContract contract)
    {
        if (!string.IsNullOrWhiteSpace(contract.EventIdentity.EventSubtype))
            return contract.EventIdentity.EventSubtype.Contains("Conjunction", StringComparison.OrdinalIgnoreCase) ? "Planet Conjunction" : contract.EventIdentity.EventSubtype;
        return contract.EventIdentity.EventAction;
    }

    private static string NormalizeLanguage(string value) => value.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? "hi" : "en";
}


public interface IThumbnailCreativeProfile
{
    string Name { get; }
    string Subject { get; }
    string Composition { get; }
    string ObjectRenderingInstructions { get; }
    string FamilyNegativeRules { get; }
}

public sealed record ThumbnailCreativeProfile(
    string Name,
    string Subject,
    string Composition,
    string ObjectRenderingInstructions,
    string FamilyNegativeRules) : IThumbnailCreativeProfile;

public static class ThumbnailCreativeProfileFactory
{
    private static readonly string[] PlanetNames = ["Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune"];

    public static IThumbnailCreativeProfile Create(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var family = Normalize(contract.EventIdentity.EventFamily + " " + contract.EventIdentity.EventSubtype + " " + contract.EventIdentity.EventName + " " + contract.EventIdentity.EventAction);
        if (family.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return MeteorCreativeProfile(contract);
        if (family.Contains("solar eclipse", StringComparison.OrdinalIgnoreCase)) return SolarEclipseCreativeProfile(contract);
        if (family.Contains("lunar eclipse", StringComparison.OrdinalIgnoreCase)) return LunarEclipseCreativeProfile(contract);
        if (family.Contains("comet", StringComparison.OrdinalIgnoreCase)) return CometCreativeProfile(contract);
        if (family.Contains("opposition", StringComparison.OrdinalIgnoreCase)) return OppositionCreativeProfile(contract);
        if (family.Contains("visibility", StringComparison.OrdinalIgnoreCase) || family.Contains("elongation", StringComparison.OrdinalIgnoreCase)) return PlanetVisibilityCreativeProfile(contract);
        if (family.Contains("occult", StringComparison.OrdinalIgnoreCase)) return OccultationCreativeProfile(contract);
        if (family.Contains("constellation", StringComparison.OrdinalIgnoreCase) || family.Contains("sky guide", StringComparison.OrdinalIgnoreCase)) return ConstellationGuideCreativeProfile(contract);
        return PlanetaryCreativeProfile(contract);
    }

    public static IThumbnailCreativeProfile PlanetaryCreativeProfile(ThumbnailPromptContract contract)
    {
        var objects = EventObjects(contract);
        var rendering = new List<string> { "Render only the named event objects: " + ObjectPhrase(objects) + "." };
        foreach (var obj in objects)
        {
            if (obj.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)) rendering.Add("Render Jupiter as a premium telescope-quality circular planetary disk with visible cloud bands, natural atmospheric depth, subtle Great Red Spot style detail when visible, and realistic illumination; Jupiter dominant when it is the primary event object.");
            else if (obj.Equals("Venus", StringComparison.OrdinalIgnoreCase)) rendering.Add("Render Venus as a bright, naturally illuminated circular planetary disk with realistic atmospheric glow.");
            else if (PlanetNames.Any(p => p.Equals(obj, StringComparison.OrdinalIgnoreCase))) rendering.Add($"Render {obj} as a scientifically plausible circular planetary disk or bright naked-eye planet appropriate to the event scale.");
            else rendering.Add($"Render {obj} only if it is an event object, with realistic astronomy scale and lighting.");
        }
        return new ThumbnailCreativeProfile("PlanetaryCreativeProfile", $"named planet/object event only: {ObjectPhrase(objects)}", "Event-object composition with clean separation, horizon context when useful, and no unrelated sky phenomena.", string.Join(" ", rendering), "No meteor, radiant, eclipse, comet-tail, or unrelated object wording; no random planets; no conjunction wording unless the event subtype is a conjunction.");
    }

    public static IThumbnailCreativeProfile MeteorCreativeProfile(ThumbnailPromptContract contract)
    {
        var shower = ObjectPhrase(EventObjects(contract));
        return new ThumbnailCreativeProfile("MeteorCreativeProfile", $"meteor shower sky for {shower}", "Dark sky with radiant area, natural meteor streaks, wide horizon context, and open negative space for integrated guide UI.", "Render natural meteor streaks radiating from the radiant; include a subtle radiant marker only if useful for the guide; use dark sky, radiant, meteor streaks, and horizon as the artwork vocabulary.", "No close-pair wording, no planet disk wording, no unrelated bright planetary bodies, no Moon unless explicitly required by the guide-card object labels.");
    }

    public static IThumbnailCreativeProfile SolarEclipseCreativeProfile(ThumbnailPromptContract contract) =>
        new("SolarEclipseCreativeProfile", "Sun and Moon eclipse geometry", "Centered eclipse phase with dramatic sky contrast and safety-aware guide UI.", "Render the Sun-Moon eclipse geometry with solar corona or partial eclipse phase as appropriate; include strong safety visual language such as eclipse glasses or safe-viewing cue when safety information is present.", "No planets, no meteor streaks, no comet tails, no unrelated night-sky objects.");

    public static IThumbnailCreativeProfile LunarEclipseCreativeProfile(ThumbnailPromptContract contract) =>
        new("LunarEclipseCreativeProfile", "Moon in Earth shadow", "Dominant circular Moon with shadow progression or umbra context and clean guide UI.", "Render the Moon in Earth shadow; use red or copper Moon treatment for total eclipse wording, otherwise show the appropriate eclipse phase with lunar surface detail.", "No planets or meteor streaks unless explicitly required by the event objects.");

    public static IThumbnailCreativeProfile CometCreativeProfile(ThumbnailPromptContract contract) =>
        new("CometCreativeProfile", $"comet with realistic tail: {ObjectPhrase(EventObjects(contract))}", "Comet-forward dark sky composition with tail direction, horizon depth, and clean readable UI.", "Render the comet nucleus, coma, and realistic dust/ion tail with astronomical plausibility; keep tail graceful rather than symbolic.", "No planets unless explicitly required by the event objects; no meteor-shower radiant field; no eclipse geometry.");

    public static IThumbnailCreativeProfile OppositionCreativeProfile(ThumbnailPromptContract contract) =>
        new("OppositionCreativeProfile", $"opposition planet as hero object: {ObjectPhrase(EventObjects(contract))}", "Hero planet composition with scale, texture, and observation-card safe space.", "Render the opposition target as the only hero planet with realistic circular geometry, illumination, and recognizable surface or atmospheric detail.", "No meteor streaks, no eclipse geometry, no unrelated planets.");

    public static IThumbnailCreativeProfile PlanetVisibilityCreativeProfile(ThumbnailPromptContract contract) =>
        new("PlanetVisibilityCreativeProfile", $"visible planet sky guide: {ObjectPhrase(EventObjects(contract))}", "Sky-guide composition showing only the target planet or planets with horizon/direction context.", "Render only the target visible planet(s), using realistic brightness, color, and scale for naked-eye viewing.", "No unrelated planets, no meteor shower, no eclipse geometry, no comet tail.");

    public static IThumbnailCreativeProfile OccultationCreativeProfile(ThumbnailPromptContract contract) =>
        new("OccultationCreativeProfile", $"occulting body and hidden object: {ObjectPhrase(EventObjects(contract))}", "Close sky-guide composition emphasizing disappearance or reappearance near the occulting limb.", "Render the occulting body and the hidden object only; show disappearance/reappearance concept when available without inventing extra bodies.", "No unrelated planets, no meteor streaks, no eclipse corona unless the event is explicitly an eclipse.");

    public static IThumbnailCreativeProfile ConstellationGuideCreativeProfile(ThumbnailPromptContract contract) =>
        new("ConstellationGuideCreativeProfile", $"constellation pattern and guide stars: {ObjectPhrase(EventObjects(contract))}", "Wide dark-sky guide with constellation line pattern, guide stars, and readable integrated UI.", "Render the constellation pattern and guide stars with restrained linework or markers suitable for a sky guide.", "No unrelated planets, no meteor shower, no eclipse geometry unless explicitly required by the event.");

    private static IReadOnlyList<string> EventObjects(ThumbnailPromptContract contract)
    {
        var values = contract.Objects.PrimaryObjects.Concat(contract.Objects.SecondaryObjects).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return values.Count == 0 ? [contract.EventIdentity.EventName] : values;
    }

    private static string ObjectPhrase(IReadOnlyList<string> objects) => objects.Count == 0 ? "the event object" : string.Join(" and ", objects);
    private static string Normalize(string value) => value.Replace("-", " ", StringComparison.OrdinalIgnoreCase).Replace("_", " ", StringComparison.OrdinalIgnoreCase);
}

public sealed class ThumbnailPromptWriterV9
{
    public ThumbnailPromptBuildResult Write(ThumbnailPromptContract contract) => CreativeBriefPromptBuilder.Build(contract);
}

internal static class CreativeBriefPromptBuilder
{
    public static ThumbnailPromptBuildResult Build(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ThumbnailPromptContractValidator.Validate(contract);

        var strategy = PlatformStorytellingStrategies.Resolve(contract);
        var profile = ThumbnailCompositionProfiles.Resolve(contract);
        var language = NormalizeLanguage(contract.Brand.LocalizationRules.FirstOrDefault() ?? "en");
        var terms = LocalizedPromptTerms.For(language);
        var fields = ThumbnailFieldFormatter.Format(contract.Observation, language);
        var aspect = ResolveAspect(contract);
        var prompt = string.Join(Environment.NewLine, BuildSections(contract, aspect, terms, fields)).Trim();
        PromptValidatorV9.Validate(contract, [], prompt, strategy);

        var report = new PromptAssemblyReport(
            contract.EventIdentity.EventName,
            contract.EventIdentity.EventFamily,
            contract.Platform.CompositionProfile,
            contract.Brand.LocalizationRules.FirstOrDefault() ?? string.Empty,
            profile.Name,
            strategy.Name,
            ["creative-brief"],
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            prompt.Length,
            CountWords(prompt));

        return new ThumbnailPromptBuildResult(prompt, PromptAssembler.AppendArtworkNegativeRules(contract.Prompt.NegativePrompt), strategy, report);
    }

    private static IEnumerable<string> BuildSections(ThumbnailPromptContract contract, string aspect, LocalizedPromptTerms terms, ThumbnailFormattedGuideFields fields)
    {
        var size = $"{contract.Platform.Width}x{contract.Platform.Height}";
        var subtitle = contract.EventIdentity.EventSubtype.Contains("Conjunction", StringComparison.OrdinalIgnoreCase) ? "Planet Conjunction" : contract.EventIdentity.EventAction;
        var creativeProfile = ThumbnailCreativeProfileFactory.Create(contract);
        var optional = BuildOptionalGuideRows(contract.Observation.GuideCard);
        var card = $"{terms.Date}: {fields.Date}; {terms.BestTime}: {fields.BestTime}; {terms.Direction}: {fields.Direction}; {terms.Equipment}: {fields.Equipment}" + (string.IsNullOrWhiteSpace(fields.Separation) ? string.Empty : $"; {terms.Separation}: {fields.Separation}") + optional;
        var title = contract.Display.LocalizedTitle;
        var intent = aspect switch
        {
            "portrait" => "Create a premium YouTube Shorts cover that feels designed natively for mobile discovery, not an empty poster or cropped landscape.",
            "square" => "Create a balanced premium astronomy feed thumbnail with the same clean documentary design language as the wide and vertical covers.",
            _ => "Create a stable premium YouTube astronomy thumbnail with cinematic polish, clear science context, and strong first-glance recognition."
        };
        var canvas = aspect switch
        {
            "portrait" => $"2160x3840 native 9:16 composition; keep the requested output proportional to this vertical poster design even when rendered at {size}.",
            "square" => $"{size} native 1:1 composition; centered, balanced, and never a crop from another aspect ratio.",
            _ => $"{size} native 16:9 composition; wide horizon-aware layout with safe title and card zones."
        };
        var composition = aspect switch
        {
            "portrait" => "Premium mobile-cover hierarchy: cinematic title at the top, event artwork in the center, compact observation card at the bottom, and generous clean negative space; increase subject dominance only with framing, perspective, and camera distance.",
            "square" => "Center-weighted event artwork, readable title area, compact observation card, symmetrical breathing room, and no squeezed landscape or tall-poster leftovers.",
            _ => "Wide sky with an elegant horizon cue, dominant event artwork, readable title area, and one premium glass observation card."
        };
        var typography = aspect switch
        {
            "portrait" => $"Large top title: {title}. Compact bottom card only: {card}. Use bold high-contrast typography with mobile readability.",
            "square" => $"Title: {title}. Subtitle: {subtitle}. Compact card: {card}. Keep text large enough for mobile feed readability.",
            _ => $"Title: {title}. Subtitle: {subtitle}. Premium glass card: {card}. Keep all UI integrated into the final image."
        };

        yield return $"Creative Intent: {intent}";
        yield return $"Canvas & Aspect: {canvas}";
        yield return $"Creative Profile: {creativeProfile.Name}";
        yield return $"Subject: {creativeProfile.Subject}.";
        yield return $"Composition: {creativeProfile.Composition} {composition}";
        yield return $"Object Rendering: {creativeProfile.ObjectRenderingInstructions}";
        yield return $"Information to Render: {card}. Use absolute date and time only.";
        yield return $"Typography & UI: {typography}";
        yield return "Scientific Accuracy: Keep celestial bodies physically circular where applicable, naturally separated, and astronomically plausible; keep planets circular; never stretch, squeeze, elongate, or turn circular bodies into vertical ovals.";
        yield return "Visual Style: Premium dark blue and gold astronomy documentary look, atmospheric depth, subtle horizon glow, polished integrated thumbnail UI, high contrast, clean negative space, no watermark, native aspect ratio.";
        yield return $"Negative Rules: {creativeProfile.FamilyNegativeRules} No relative-day or urgency phrases; no post-processing overlay instructions; no watermark, clutter, tiny text, random objects, empty poster look, external branding, vertical oval bodies, elongated bodies, stretched bodies, or squeezed bodies.";
        yield return "Final Objective: Deliver one complete final thumbnail that is clean, accurate, premium, and ready to publish.";
    }

    private static string ResolveAspect(ThumbnailPromptContract contract)
    {
        if (contract.Platform.CompositionProfile.Contains("portrait", StringComparison.OrdinalIgnoreCase) || contract.Platform.AspectRatio is "9:16" or "9x16") return "portrait";
        if (contract.Platform.CompositionProfile.Contains("square", StringComparison.OrdinalIgnoreCase) || contract.Platform.AspectRatio is "1:1" or "1x1") return "square";
        return "landscape";
    }

    private static string NormalizeLanguage(string value) => value.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? "hi" : "en";
    private static int CountWords(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string BuildOptionalGuideRows(PlanetaryThumbnailGuideCardDto? guideCard)
    {
        if (guideCard is null) return string.Empty;
        var rows = new List<string>();
        Add("Moon", guideCard.Moon);
        Add("Radiant", guideCard.Radiant);
        Add("Peak", guideCard.Peak);
        Add("Safety", guideCard.Safety);
        Add("Magnitude", guideCard.Magnitude);
        if (guideCard.ObjectLabels is { Count: > 0 }) Add("Object Labels", string.Join(", ", guideCard.ObjectLabels.Where(v => !string.IsNullOrWhiteSpace(v))));
        if (guideCard.Callouts is { Count: > 0 }) Add("Callouts", string.Join(", ", guideCard.Callouts.Where(v => !string.IsNullOrWhiteSpace(v))));
        Add("Sky Guide Cue", guideCard.SkyGuideCue);
        return rows.Count == 0 ? string.Empty : "; " + string.Join("; ", rows);

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) rows.Add($"{label}: {value.Trim()}");
        }
    }

    private sealed record LocalizedPromptTerms(string Language, string Date, string BestTime, string Direction, string Separation, string Equipment)
    {
        public static LocalizedPromptTerms For(string language) => language == "hi"
            ? new LocalizedPromptTerms("hi", "तारीख", "सबसे अच्छा समय", "दिशा", "दूरी", "उपकरण")
            : new LocalizedPromptTerms("en", "Date", "Best Time", "Direction", "Separation", "Equipment");
    }
}

public sealed record ThumbnailFormattedGuideFields(string DateDisplay, string BestTimeDisplay, string DirectionDisplay, string EquipmentDisplay, string? SeparationDisplay)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Date => DateDisplay;
    [System.Text.Json.Serialization.JsonIgnore]
    public string BestTime => BestTimeDisplay;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Direction => DirectionDisplay;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Equipment => EquipmentDisplay;
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Separation => SeparationDisplay;
}

public static class ThumbnailFieldFormatter
{
    private static readonly string[] RelativeTimePhrases = ["today", "tonight", "tomorrow", "this evening", "this week", "look tonight", "watch tonight", "don't miss", "don’t miss", "coming soon", "right now"];

    public static ThumbnailFormattedGuideFields Format(ThumbnailObservation observation, string language)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var guideCard = observation.GuideCard;
        var info = observation.ObservationInfo;
        var date = CleanDate(FirstNonEmpty(guideCard?.Date, info?.DisplayDate));
        var bestTime = CleanBestTime(FirstNonEmpty(guideCard?.BestTime, info?.DisplayTime, ExtractTimeCue(info?.BestViewingWindowLocal), ExtractTimeCue(info?.DisplayWindowLocal)), info?.Timezone);
        var direction = CleanDirection(FirstNonEmpty(guideCard?.Direction, info?.Direction, observation.Direction));
        var equipment = CleanEquipment(FirstNonEmpty(guideCard?.Equipment, language == "hi" ? "नंगी आँख; दूरबीन वैकल्पिक" : "Naked eye; binoculars optional"));
        var separation = CleanSeparation(FirstNonEmpty(guideCard?.Separation, ExtractSeparation(observation.Visibility)));

        Validate(date, bestTime, direction, equipment, separation);
        return new ThumbnailFormattedGuideFields(date, bestTime, direction, equipment, separation);
    }

    private static string CleanDate(string value)
    {
        var text = value.Trim();
        if (System.DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return parsed.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
        return text;
    }

    private static string CleanBestTime(string value, string? timezone)
    {
        var text = ExtractTimeCue(value);
        var zone = FirstNonEmpty(ExtractTimezone(text), timezone);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+\b[A-Z]{2,5}\b$", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(zone) ? text : $"{text} {zone}";
    }

    private static string CleanDirection(string value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value, @"\b(North|South|East|West|Northeast|Northwest|Southeast|Southwest|NE|NW|SE|SW|N|S|E|W)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(match.Value.ToLowerInvariant()) : value.Trim();
    }

    private static string CleanEquipment(string value)
    {
        var text = value.Trim();
        if (text.Contains("naked eye", StringComparison.OrdinalIgnoreCase))
            return "Naked Eye";
        return string.Join("; ", text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part.ToLowerInvariant())));
    }

    private static string? CleanSeparation(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(value, @"(\d+(?:\.\d+)?)\s*(?:°|deg|degree|degrees)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? $"{match.Groups[1].Value}° Apart" : null;
    }

    private static void Validate(string date, string bestTime, string direction, string equipment, string? separation)
    {
        if (string.IsNullOrWhiteSpace(date)) throw new InvalidOperationException("Thumbnail guide-card validation failed: Date is missing.");
        if (System.Text.RegularExpressions.Regex.IsMatch(date, @"^\s*\d{1,2}:\d{2}\s*(?:AM|PM)?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new InvalidOperationException("Thumbnail guide-card validation failed: DateDisplay resembles a time-only value.");
        if (System.Text.RegularExpressions.Regex.IsMatch(date, @"\b(?:AM|PM)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new InvalidOperationException("Thumbnail guide-card validation failed: DateDisplay contains AM/PM.");
        if (string.IsNullOrWhiteSpace(bestTime)) throw new InvalidOperationException("Thumbnail guide-card validation failed: BestTime is missing.");
        if (string.IsNullOrWhiteSpace(direction)) throw new InvalidOperationException("Thumbnail guide-card validation failed: Direction is missing.");
        var text = string.Join(" ", date, bestTime, direction, equipment, separation);
        foreach (var phrase in RelativeTimePhrases)
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Thumbnail guide-card validation failed: relative time phrase '{phrase}' is forbidden.");
    }

    private static string ExtractTimeCue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b\d{1,2}:\d{2}\s*(?:AM|PM)?(?:\s+[A-Z]{2,5})?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Trim() : text;
    }

    private static string ExtractTimezone(string value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value, @"\b([A-Z]{2,5})\b\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string ExtractSeparation(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var match = System.Text.RegularExpressions.Regex.Match(value, @"(?i)(?:separation|apart|angular separation|minimum angular separation)?[^.;,]*(\d+(?:\.\d+)?\s*(?:°|deg|degree|degrees))");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

public sealed class ThumbnailPromptComposerV1
{
    public ThumbnailPromptBuildResult Compose(ThumbnailPromptContract contract) => new PromptAssembler().Assemble(contract);
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

    public static readonly CompositionProfile LandscapeProfile = new("LandscapeProfile", "16:9", ["YouTube", "Website"], ["premium wide documentary cover", "balanced twilight horizon", "strong negative space", "event occupies roughly 35-45% of frame", "safe text area", "finished-thumbnail layout guidance", "LandscapeProfile = Stable"], ["COMPOSITION PROFILE: LandscapeProfile for 16:9 YouTube and website surfaces.", "Use premium wide documentary framing, balanced twilight horizon, strong negative space, safe text area, and elegant final-thumbnail layout guidance.", "Keep the astronomical event roughly 35-45% of the frame with recognizable objects, no clipping, no overlap, and no borrowed aspect-ratio layout."], ["Landscape prompt must be generated natively for 16:9.", "CompositionProfile controls framing, negative space, object dominance, safe text area, and finished-thumbnail layout guidance."]);
    public static readonly CompositionProfile PortraitProfile = new("PortraitProfile", "9:16", ["YouTube Shorts", "Instagram Reels"], ["premium Shorts cover", "premium top title", "beautiful circular planetary composition", "compact bottom observation card", "Jupiter-Venus planetary recognition", "impossible landscape crop", "avoid squeezed landscape look"], ["COMPOSITION PROFILE: PortraitProfile for native 9:16 YouTube Shorts and Instagram Reels covers.", "Use portrait cover art direction: premium title at top, beautiful circular Jupiter and Venus in the middle, compact bottom observation card, and large clean negative space.", "Make Jupiter and Venus recognizable as circular planetary disks; increase dominance only through composition, framing, perspective, and camera distance, never by stretching or distortion.", "The composition must feel impossible to crop into landscape: no side panels, no guide table, no bottom strip, no action prompt, no observation window; keep Date, Best Time, Direction, Equipment, and Separation compact."], ["Portrait prompt must be generated natively for 9:16.", "CompositionProfile controls portrait framing, object dominance, phone readability, and the cover-first hierarchy."]);
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
    public static readonly PlatformStorytellingStrategy LandscapeStrategy = new("LandscapeStrategy", "Premium concise", ["YouTube", "Website"], ["Title", "Subtitle", "Observation Card", "Equipment", "Safety", "CTA", "Dominant Object", "Visual Scene", "Quality Rules"], 36, 5, "Footer disabled for landscape final polish", "One premium glass observation card allowed", "CTA allowed as one short action cue", ["STORYTELLING STRATEGY: LandscapeStrategy decides WHAT information exists for premium YouTube and website thumbnails.", "Allowed visible information: event title, date, best time, direction, equipment, separation when applicable, and one CTA.", "Keep the event dominant and the copy large, concise, human-facing, and mobile readable."], null);
    public static readonly PlatformStorytellingStrategy PortraitStrategy = new("PortraitStrategy", "Cover-minimal", ["YouTube Shorts", "Instagram Reels"], ["Title", "Dominant Object", "One Observation Hint", "Visual Scene", "Quality Rules"], 18, 1, "Footer disabled", "Compact bottom observation card with date, best time, direction, equipment, and separation; no large fields", "Action prompt disabled for portrait covers", ["STORYTELLING STRATEGY: PortraitStrategy decides WHAT information exists for phone-first Shorts and Reels covers.", "Allowed visible information: large title plus Date, Best Time, Direction, Equipment, and Separation in one compact premium badge.", "Forbidden: observation window, guide table, bottom strip, action prompt, large cards, marketing language."], 1);
    public static readonly PlatformStorytellingStrategy SquareStrategy = new("SquareStrategy", "Medium", ["Facebook", "Mobile feed"], ["Title", "Subtitle", "Compact Observation", "CTA", "Dominant Object", "Visual Scene", "Quality Rules"], 34, 4, "Footer disabled", "Compact observation enabled with title, date, best time, direction, separation when available, and short equipment when useful", "CTA allowed when compact and feed optimized", ["STORYTELLING STRATEGY: SquareStrategy decides WHAT information exists for balanced mobile-feed thumbnails.", "Allowed sections: Title, Subtitle, Compact Observation, CTA.", "Use medium-density details without reusing a squeezed landscape layout."], null);
    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace('_', ':');
}

public sealed class PromptAssembler
{
    public ThumbnailPromptBuildResult Assemble(ThumbnailPromptContract contract) => CreativeBriefPromptBuilder.Build(contract);

    public static string AppendArtworkNegativeRules(string negativePrompt)
    {
        const string antiDistortion = "native aspect composition, no stretched landscape, no squeezed portrait, no cropped square, circular celestial bodies, physically correct astronomical geometry";
        return string.IsNullOrWhiteSpace(negativePrompt) ? ThumbnailArtworkPromptRules.NegativePrompt + ", " + antiDistortion : negativePrompt + ", " + ThumbnailArtworkPromptRules.NegativePrompt + ", " + antiDistortion;
    }

    public static IReadOnlyList<PromptSection> BuildDefaultSections(ThumbnailPromptContract contract) =>
    [
        new PromptSection("legacy-title", "Title", 10, contract.Display.DisplayTitle, true),
        new PromptSection("legacy-dominant-object", "Dominant Object", 20, string.Join(" + ", contract.Objects.PrimaryObjects), true),
        new PromptSection("legacy-observation-card", "Observation Card", 30, $"{contract.Observation.BestViewingWindow}; {contract.Observation.Direction}; {contract.Observation.Visibility}", true),
        new PromptSection("legacy-one-observation-hint", "One Observation Hint", 30, $"{contract.Observation.Direction} / {contract.Observation.BestViewingWindow}", false),
        new PromptSection("legacy-compact-observation", "Compact Observation", 30, $"{contract.Observation.Direction} / {contract.Observation.BestViewingWindow}", false),
        new PromptSection("legacy-positive-prompt", "Quality Rules", 100, contract.Prompt.PositivePrompt, true)
    ];

    private static bool Supports(IReadOnlyList<string>? allowed, params string[] values) => allowed is null || allowed.Count == 0 || allowed.Any(a => values.Any(v => !string.IsNullOrWhiteSpace(v) && (string.Equals(a, v, StringComparison.OrdinalIgnoreCase) || v.Contains(a, StringComparison.OrdinalIgnoreCase) || a.Contains(v, StringComparison.OrdinalIgnoreCase))));
    public static void EnforceInformationBudget(PlatformStorytellingStrategy strategy, List<PromptSection> included, Dictionary<string, string> removed)
    {
        if (strategy.MaximumInformationItems is not int max) return;
        var info = included.Where(s => IsInformation(s.Category)).OrderBy(s => s.Priority).ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var section in info.Skip(max)) { included.Remove(section); removed[section.Id] = $"Information density exceeds {strategy.Name} maximum of {max} item(s)"; }
    }
    private static bool IsInformation(string category) => category.Contains("Observation", StringComparison.OrdinalIgnoreCase) || category.Contains("Equipment", StringComparison.OrdinalIgnoreCase) || category.Contains("Safety", StringComparison.OrdinalIgnoreCase) || category.Contains("Footer", StringComparison.OrdinalIgnoreCase);
    private static int CountWords(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

public sealed class ThumbnailPromptBuilder { public ThumbnailPromptBuildResult Build(ThumbnailPromptContract contract) => new ThumbnailPromptComposerV1().Compose(contract); }

public static class PromptValidatorV9
{
    public static void Validate(ThumbnailPromptContract contract, IReadOnlyList<PromptSection> sections, string finalPrompt, PlatformStorytellingStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (string.IsNullOrWhiteSpace(finalPrompt)) throw new InvalidOperationException("Prompt validation failed: final composed prompt is empty.");
        _ = CountWords(finalPrompt);

        var legacyPhrases = new[] { "V8", "background-only", "background only", "background artwork", "do not draw text", "do not render text", "do not draw title", "do not draw icons", "renderer will add", "renderer owns", "renderer-owned", "deterministic overlay", "manual overlay", "BackgroundOnly", "RendererPresentation", "ThumbnailV8AiNativeRenderer", "AzureImage2ThumbnailV5", "crop landscape" };
        if (ContainsAny(finalPrompt, legacyPhrases)) throw new InvalidOperationException("Prompt validation failed: forbidden V8/background/overlay phrase remains.");

        // Validate normalized guide-card values before/independent of prompt rendering.
        // Do not parse rendered prose as structured date data; Best Time legitimately contains AM/PM in English.
        _ = ThumbnailFieldFormatter.Format(contract.Observation, contract.Brand.LocalizationRules.FirstOrDefault() ?? "en");
        if (ContainsAny(finalPrompt, "Today", "Tonight", "Tomorrow", "This evening", "This week", "Look tonight", "Watch tonight", "Don’t miss", "Don't miss", "Coming soon", "Right now")) throw new InvalidOperationException("Prompt validation failed: relative time words are forbidden in evergreen thumbnail prompts.");
    }

    private static bool ContainsAny(string value, params string[] tokens) => tokens.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
    private static int CountWords(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
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
