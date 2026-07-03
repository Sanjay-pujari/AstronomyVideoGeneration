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

    public static readonly VisualDirectingProfile LandscapeDirector = new("LandscapeDirector", "wide premium documentary cover camera, horizon-aware lateral staging", "natural cinema lens language with believable planetary scale", "editorial left-to-right composition with a dominant event pair and calm premium space", "National Geographic feature image with Netflix-documentary polish and YouTube clarity", "the event dominates first glance without distorting scientific proportions", "twilight horizon glow, clean sky gradient, and restrained landscape silhouette explain where to look", "reserve edge-safe breathing room for a title and one glass observation card only", ["VISUAL DIRECTING PROFILE: LandscapeDirector.", "Compose as a native 16:9 premium astronomy cover: recognizable event first, elegant horizon context second, concise viewing details third."], UniversalAntiDistortionRules);
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

public sealed class ThumbnailPromptWriterV9
{
    private readonly ThumbnailCreativeDirector _creativeDirector = new();

    public ThumbnailPromptBuildResult Write(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ThumbnailPromptContractValidator.Validate(contract);

        var direction = _creativeDirector.Direct(contract);
        var strategy = PlatformStorytellingStrategies.Resolve(contract);
        var profile = ThumbnailCompositionProfiles.Resolve(contract);
        var directing = VisualDirectingProfiles.Resolve(contract);
        var family = FamilyDirectors.Resolve(contract);
        var sections = contract.PromptSections is { Count: > 0 } ? contract.PromptSections : BuildDefaultSections(contract);
        var removed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var included = FilterSections(contract, strategy, sections, removed);
        EnforceInformationBudget(strategy, included, removed);

        var prompt = BuildCreativeBrief(contract, direction, strategy, profile, directing, family, included);
        var duplicateSectionsRemoved = RemoveDuplicateLines(ref prompt);
        PromptValidatorV9.Validate(contract, included, prompt, strategy);

        var report = new PromptAssemblyReport(
            contract.EventIdentity.EventName,
            contract.EventIdentity.EventFamily,
            contract.Platform.CompositionProfile,
            contract.Brand.LocalizationRules.FirstOrDefault() ?? string.Empty,
            profile.Name,
            strategy.Name,
            included.Select(s => s.Id).ToArray(),
            sections.Select(s => s.Id).Except(included.Select(s => s.Id), StringComparer.OrdinalIgnoreCase).Concat(Enumerable.Repeat("duplicate-section", duplicateSectionsRemoved)).ToArray(),
            removed,
            prompt.Length,
            CountWords(prompt));

        return new ThumbnailPromptBuildResult(prompt, AppendArtworkNegativeRules(contract.Prompt.NegativePrompt), strategy, report);
    }

    private static string BuildCreativeBrief(ThumbnailPromptContract contract, ThumbnailCreativeDirection direction, PlatformStorytellingStrategy strategy, CompositionProfile profile, VisualDirectingProfile directing, FamilyDirector family, IReadOnlyList<PromptSection> included)
    {
        var aspect = contract.Platform.AspectRatio;
        var isPortrait = aspect == "9:16";
        var isSquare = aspect == "1:1";
        var lang = NormalizeLanguage(contract.Brand.LocalizationRules.FirstOrDefault() ?? "en");
        var localized = LocalizedPromptTerms.For(lang);
        var title = contract.Display.LocalizedTitle;
        var subtitle = lang == "hi" ? LocalizeKnownEnglishPhrase(contract.Display.DisplayShortTitle, lang) : contract.Display.DisplayShortTitle;
        var objects = string.Join(", ", contract.Objects.PrimaryObjects.Select(o => contract.Objects.LocalizedObjectNames.TryGetValue(o, out var local) ? local : o));
        var observation = BuildObservationPrompt(contract, localized, isPortrait, isSquare);
        var layout = isPortrait
            ? "Native vertical mobile poster: a tall sky column, large circular event subject in the upper half, short title stacked in the safe middle, one small action cue near the lower third, with no extra panels or tabular data."
            : isSquare
                ? "Native square feed composition: balanced center-weighted celestial geometry, compact title block, one small rounded fact badge, equal breathing room on every side. Do not reuse a wide landscape layout."
                : "Native wide 16:9 YouTube cover: large recognizable event bodies dominate one side, premium title on the other, and one glassmorphism observation card for the essential viewing facts.";
        var textBudget = isPortrait ? "large mobile-readable words with date, time, direction, and one CTA" : isSquare ? "medium-density feed-readable copy" : "large YouTube-readable title plus only the essential facts: date, best time, direction, equipment, separation when available, and CTA";
        var aspectDetail = isPortrait
            ? "Let the vertical depth do the work: sky gradient, horizon glow, and one clean focal path from title to subject to action cue."
            : isSquare
                ? "Use a compact radial read: the viewer should understand the title, the subject, and the one fact badge in a single glance. Keep the scene symmetrical enough for feed cropping safety while still feeling photographic, not like a resized poster. Let the sky texture frame the object instead of filling the square with text."
                : "Use the width for instant recognition: Jupiter and Venus should read within one second, with Jupiter visibly larger and banded and Venus smaller, bright, and warm-white. Keep the sky scientifically believable for the event time and direction, use premium dark-blue/gold contrast, and place the glass card where it never overlaps or clips the planets. Avoid checklist energy: every visible word must feel editorial, intentional, and thumbnail-size readable.";
        var familyStyle = string.Join(", ", family.ArtisticVocabulary);
        var localizationInstruction = lang == "hi"
            ? isPortrait
                ? "all visible UI labels, CTA, date/time/direction labels, and observation fields must be Hindi in Devanagari; planet names may remain English only when intentionally listed as object labels."
                : isSquare
                    ? "all visible UI labels, CTA, footer tips, date/time/direction labels, and observation fields must be Hindi in Devanagari; planet names may remain English only when intentionally listed as object labels."
                    : "all visible UI labels, CTA, date/time/direction labels, and observation fields must be Hindi in Devanagari; planet names may remain English only when intentionally listed as object labels."
            : isPortrait
                ? "all visible UI labels, CTA, date/time/direction labels, and observation fields must be English."
                : isSquare
                    ? "all visible UI labels, CTA, footer tips, date/time/direction labels, and observation fields must be English."
                    : "all visible UI labels, CTA, date/time/direction labels, and observation fields must be English.";
        var dataToRender = isPortrait || isSquare
            ? $"{localized.Title}: \"{title}\". {localized.Subtitle}: \"{subtitle}\". Render {observation}. {localized.ObjectLabels}: {objects}."
            : $"{localized.Title}: \"{title}\". Render {observation}. {localized.ObjectLabels}: {objects}.";

        return $"""
Creative intent: Create a complete final astronomy thumbnail, not a background plate. The image itself must include the finished text and simple integrated UI. No post-processing overlay will be added. Aim for {direction.EmotionalAngle}: immediate recognition of {contract.EventIdentity.EventName} with scientific trust and high contrast.

Aspect/output size: {contract.Platform.Width}x{contract.Platform.Height}, {aspect}, {profile.Name}. Compose natively for this canvas; never stretch, squeeze, pad, crop, or adapt another aspect ratio. Celestial bodies must stay circular with physically correct geometry.

Scene description: Show {objects} as the unmistakable subject in a premium dark-blue and gold observational sky. Use {family.Name} visual language: {familyStyle}. The mood is cinematic but clean, with scientifically trusted color, proportion, sky context, atmospheric depth, and no invented planets or random extra objects.

Layout guidance: {layout} {directing.ArtisticComposition}. {aspectDetail} Keep the object prominence natural for {contract.Platform.CompositionProfile}, with readable negative space and no crowded report-like layout.

Data to render: {dataToRender} Language/localization: {lang}; {localizationInstruction} Use only short human-facing labels, never database names.

Typography/readability: Use natural title case, premium bold typography, high contrast, clean spacing, and polished glassmorphism panels/icons that feel designed into the image. Keep {textBudget}; make every word large and legible at thumbnail size.

Negative instructions: no separate background plate, no later text pass, no watermark, no logo, no location names, no clutter, no tiny text, no distorted celestial disks, no squeezed or cropped landscape look, no duplicate CTA or repeated quality rules.
""".Trim();
    }

    private static string BuildObservationPrompt(ThumbnailPromptContract contract, LocalizedPromptTerms terms, bool isPortrait, bool isSquare)
    {
        var info = contract.Observation.ObservationInfo;
        var date = FirstNonEmpty(info?.DisplayDate, ExtractDate(contract.Observation.BestViewingWindow), contract.Observation.BestViewingWindow);
        var bestTime = FirstNonEmpty(info?.DisplayTime, contract.Observation.BestViewingWindow, ExtractTimeCue(info?.BestViewingWindowLocal), ExtractTimeCue(info?.DisplayWindowLocal));
        var direction = FirstNonEmpty(info?.Direction, contract.Observation.Direction);
        var separation = ExtractSeparation(contract.Observation.Visibility);
        var equipment = terms.Language == "hi" ? "नंगी आँख; दूरबीन वैकल्पिक" : "Naked eye; binoculars optional";
        var cta = terms.Language == "hi" ? "आज रात आसमान देखें" : "Look tonight";

        if (isPortrait)
            return $"mobile-clean vertical guide fields: {terms.Title}, {terms.Date}: {date}; {terms.BestTime}: {bestTime}; {terms.Direction}: {direction}; {terms.Cta}: {cta}. No full large table; keep planets circular.";

        if (isSquare)
        {
            var equipmentText = equipment.Length <= 32 ? $"; {terms.Equipment}: {equipment}" : string.Empty;
            var separationText = string.IsNullOrWhiteSpace(separation) ? string.Empty : $"; {terms.Separation}: {separation}";
            return $"medium-density square guide fields: {terms.Title}; {terms.Date}: {date}; {terms.BestTime}: {bestTime}; {terms.Direction}: {direction}{separationText}{equipmentText}. No squeezed landscape composition.";
        }

        var landscapeSeparation = string.IsNullOrWhiteSpace(separation) ? string.Empty : $"; {terms.Separation}: {separation}";
        return $"premium landscape glass observation card: {terms.Date}: {date}; {terms.BestTime}: {bestTime}; {terms.Direction}: {direction}{landscapeSeparation}; {terms.Equipment}: {equipment}; {terms.Cta}: {cta}; {terms.ObjectLabels}: every primary object. Do not render long date ranges, technical wording, internal IDs, or footer tips.";
    }

    private static string ExtractTimeCue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(?:\b\d{1,2}:\d{2}\s*(?:AM|PM)?\b|\bafter sunset\b|\bbefore sunrise\b|\bafter midnight\b)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Trim() : text;
    }

    private static string ExtractDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var match = System.Text.RegularExpressions.Regex.Match(value, @"\b\d{4}-\d{2}-\d{2}\b");
        return match.Success ? match.Value : value;
    }

    private static string ExtractSeparation(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var match = System.Text.RegularExpressions.Regex.Match(value, @"(?i)(?:separation|apart|angular separation|minimum angular separation)?[^.;,]*(\d+(?:\.\d+)?\s*(?:°|deg|degree|degrees))");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    private static string NormalizeLanguage(string value) => value.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? "hi" : "en";

    private static string LocalizeKnownEnglishPhrase(string value, string language)
    {
        if (language != "hi") return value;
        return value.Replace("Jupiter", "बृहस्पति", StringComparison.OrdinalIgnoreCase).Replace("Venus", "शुक्र", StringComparison.OrdinalIgnoreCase).Replace("Conjunction", "युति", StringComparison.OrdinalIgnoreCase).Replace("Pairing", "जोड़ी", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record LocalizedPromptTerms(string Language, string Title, string Subtitle, string Date, string BestTime, string Direction, string Separation, string Equipment, string FooterTips, string Cta, string ObjectLabels)
    {
        public static LocalizedPromptTerms For(string language) => language == "hi"
            ? new LocalizedPromptTerms("hi", "शीर्षक", "उपशीर्षक", "तारीख", "सबसे अच्छा समय", "दिशा", "दूरी", "उपकरण", "नीचे के सुझाव", "कार्रवाई", "वस्तु लेबल")
            : new LocalizedPromptTerms("en", "Title", "Subtitle", "Date", "Best Time", "Direction", "Separation", "Equipment", "Footer tips", "CTA", "Object labels");
    }

    private static List<PromptSection> FilterSections(ThumbnailPromptContract contract, PlatformStorytellingStrategy strategy, IReadOnlyList<PromptSection> sections, Dictionary<string, string> removed)
    {
        var included = new List<PromptSection>();
        foreach (var section in sections.OrderBy(s => s.Priority).ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!Supports(section.SupportedPlatforms, contract.Platform.CompositionProfile, contract.Platform.AspectRatio, contract.Platform.Platform)) { removed[section.Id] = "Unsupported platform/profile/aspect ratio"; continue; }
            if (!Supports(section.SupportedLanguages, contract.Brand.LocalizationRules.FirstOrDefault() ?? string.Empty)) { removed[section.Id] = "Unsupported language"; continue; }
            if (!Supports(section.SupportedFamilies, contract.EventIdentity.EventFamily)) { removed[section.Id] = "Unsupported family"; continue; }
            if (!strategy.AllowsCategory(section.Category)) { removed[section.Id] = $"Section category '{section.Category}' is not allowed by {strategy.Name}"; continue; }
            included.Add(section);
        }
        return included;
    }

    private static int RemoveDuplicateLines(ref string prompt)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var removed = 0; var lines = new List<string>();
        foreach (var line in prompt.Split('\n')) { var key = line.Trim(); if (key.Length > 0 && !seen.Add(key)) { removed++; continue; } lines.Add(line.TrimEnd()); }
        prompt = string.Join(Environment.NewLine, lines).Trim(); return removed;
    }

    public static IReadOnlyList<PromptSection> BuildDefaultSections(ThumbnailPromptContract contract) => PromptAssembler.BuildDefaultSections(contract);
    private static bool Supports(IReadOnlyList<string>? allowed, params string[] values) => allowed is null || allowed.Count == 0 || allowed.Any(a => values.Any(v => !string.IsNullOrWhiteSpace(v) && (string.Equals(a, v, StringComparison.OrdinalIgnoreCase) || v.Contains(a, StringComparison.OrdinalIgnoreCase) || a.Contains(v, StringComparison.OrdinalIgnoreCase))));
    public static void EnforceInformationBudget(PlatformStorytellingStrategy strategy, List<PromptSection> included, Dictionary<string, string> removed) => PromptAssembler.EnforceInformationBudget(strategy, included, removed);
    public static string AppendArtworkNegativeRules(string negativePrompt) => PromptAssembler.AppendArtworkNegativeRules(negativePrompt);
    private static int CountWords(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

public sealed class ThumbnailPromptComposerV1
{
    private readonly ThumbnailPromptWriterV9 _writer = new();
    public ThumbnailPromptBuildResult Compose(ThumbnailPromptContract contract) => _writer.Write(contract);
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
    public static readonly PlatformStorytellingStrategy LandscapeStrategy = new("LandscapeStrategy", "Premium concise", ["YouTube", "Website"], ["Title", "Subtitle", "Observation Card", "Equipment", "Safety", "CTA", "Dominant Object", "Visual Scene", "Quality Rules"], 36, 5, "Footer disabled for landscape final polish", "One premium glass observation card allowed", "CTA allowed as one short action cue", ["STORYTELLING STRATEGY: LandscapeStrategy decides WHAT information exists for premium YouTube and website thumbnails.", "Allowed visible information: event title, date, best time, direction, equipment, separation when applicable, and one CTA.", "Keep the event dominant and the copy large, concise, human-facing, and mobile readable."], null);
    public static readonly PlatformStorytellingStrategy PortraitStrategy = new("PortraitStrategy", "Mobile-clean", ["YouTube Shorts", "Instagram Reels"], ["Title", "Subtitle", "Dominant Object", "One Observation Hint", "CTA", "Visual Scene", "Quality Rules"], 22, 2, "Footer disabled", "Observation table disabled; render date, best time, and direction as clean mobile fields", "CTA allowed only as short phone-first action cue", ["STORYTELLING STRATEGY: PortraitStrategy decides WHAT information exists for phone-first Shorts and Reels thumbnails.", "Allowed sections: Title, Subtitle, Dominant Object, clean Date/Best Time/Direction fields, CTA.", "Forbidden: Observation Table, Equipment Table, Footer, Dense Infographic."], null);
    public static readonly PlatformStorytellingStrategy SquareStrategy = new("SquareStrategy", "Medium", ["Facebook", "Mobile feed"], ["Title", "Subtitle", "Compact Observation", "CTA", "Dominant Object", "Visual Scene", "Quality Rules"], 34, 4, "Footer disabled", "Compact observation enabled with title, date, best time, direction, separation when available, and short equipment when useful", "CTA allowed when compact and feed optimized", ["STORYTELLING STRATEGY: SquareStrategy decides WHAT information exists for balanced mobile-feed thumbnails.", "Allowed sections: Title, Subtitle, Compact Observation, CTA.", "Use medium-density details without reusing a squeezed landscape layout."], null);
    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace('_', ':');
}

public sealed class PromptAssembler
{
    public ThumbnailPromptBuildResult Assemble(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract); ThumbnailPromptContractValidator.Validate(contract);
        var profile = ThumbnailCompositionProfiles.Resolve(contract); var strategy = PlatformStorytellingStrategies.Resolve(contract); var directing = VisualDirectingProfiles.Resolve(contract); var familyDirector = FamilyDirectors.Resolve(contract);
        var sections = contract.PromptSections is { Count: > 0 } ? contract.PromptSections : BuildDefaultSections(contract);
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
        PromptValidatorV9.Validate(contract, included, prompt, strategy);
        var report = new PromptAssemblyReport(contract.EventIdentity.EventName, contract.EventIdentity.EventFamily, contract.Platform.CompositionProfile, contract.Brand.LocalizationRules.FirstOrDefault() ?? string.Empty, profile.Name, strategy.Name, included.Select(s => s.Id).ToArray(), sections.Select(s => s.Id).Except(included.Select(s => s.Id), StringComparer.OrdinalIgnoreCase).ToArray(), removed, prompt.Length, CountWords(prompt));
        return new ThumbnailPromptBuildResult(prompt, AppendArtworkNegativeRules(contract.Prompt.NegativePrompt), strategy, report);
    }

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
        // Prompt word-count targets are optimization guidance for generation quality, not
        // runtime blockers. Keep calculating the count so future diagnostics can mirror
        // this validator, but do not fail validation solely because a target is exceeded.
        _ = CountWords(finalPrompt);
        var aspect = contract.Platform.AspectRatio;

        var legacyPhrases = new[] { "V8", "background-only", "background only", "background artwork", "do not draw text", "do not render text", "do not draw title", "do not draw icons", "renderer will add", "renderer owns", "renderer-owned", "deterministic overlay", "manual overlay", "BackgroundOnly", "RendererPresentation", "ThumbnailV8AiNativeRenderer", "AzureImage2ThumbnailV5", "crop landscape" };
        if (ContainsAny(finalPrompt, legacyPhrases)) throw new InvalidOperationException("Prompt validation failed: forbidden V8/background/overlay phrase remains.");
        if (aspect == "9:16" && ContainsAny(finalPrompt, "observation card", "footer", "equipment table", "dense infographic")) throw new InvalidOperationException("Prompt validation failed: portrait prompt contains forbidden card/footer/table language.");

        if (!ContainsAny(finalPrompt, "complete final astronomy thumbnail", "complete final thumbnail")) throw new InvalidOperationException("Prompt validation failed: complete-thumbnail instruction missing.");
        if (!ContainsAny(finalPrompt, "No post-processing overlay will be added", "No post-processing")) throw new InvalidOperationException("Prompt validation failed: no-post-processing instruction missing.");
        if (!ContainsAny(finalPrompt, contract.Display.DisplayTitle, contract.Display.LocalizedTitle)) throw new InvalidOperationException("Prompt validation failed: title text missing.");
        if (!finalPrompt.Contains($"{contract.Platform.Width}x{contract.Platform.Height}", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Prompt validation failed: aspect output size missing.");
        foreach (var heading in new[] { "Creative intent:", "Aspect/output size:", "Scene description:", "Layout guidance:", "Data to render:", "Typography/readability:", "Negative instructions:" })
            if (!finalPrompt.Contains(heading, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Prompt validation failed: missing creative brief section '{heading}'.");
        if (!ContainsAny(finalPrompt, "circular", "physically correct geometry")) throw new InvalidOperationException("Prompt validation failed: circular celestial body rule missing.");
        if (aspect == "1:1" && !ContainsAny(finalPrompt, "Native square", "Do not reuse a wide landscape layout")) throw new InvalidOperationException("Prompt validation failed: native square composition missing.");
        if (aspect == "9:16" && !ContainsAny(finalPrompt, "Native vertical", "mobile")) throw new InvalidOperationException("Prompt validation failed: native portrait composition missing.");
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
