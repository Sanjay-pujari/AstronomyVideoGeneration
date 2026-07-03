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
    public static readonly VisualDirectingProfile PortraitDirector = new("PortraitDirector", "vertical mobile-first poster camera composed from the first frame for a 9:16 Shorts cover", "portrait telephoto/compressed depth language that enlarges planets without squeezing or elongating them", "premium poster stack: large cinematic title at top, huge circular celestial composition in the middle, small observation badge at bottom", "Netflix poster / National Geographic cover / Apple event poster polish for a phone lock-screen-like astronomy cover", "Jupiter dominates the vertical read, Venus supports with natural separation, and every planet remains physically circular", "deep twilight atmosphere, horizon glow, and vertical sky depth make the event feel native to mobile", "protect only three zones: top title, middle planets, bottom micro badge; no side-panel or landscape logic", ["VISUAL DIRECTING PROFILE: PortraitDirector.", "Compose as an intentionally vertical 9:16 premium Shorts cover that would be impossible to crop into landscape.", "Portrait hierarchy: 1 large title, 2 huge celestial composition, 3 small premium observation badge with Date, Best Time, Direction only."], UniversalAntiDistortionRules);
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

public sealed class ThumbnailPromptWriterV9
{
    public ThumbnailPromptBuildResult Write(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ThumbnailPromptContractValidator.Validate(contract);

        var strategy = PlatformStorytellingStrategies.Resolve(contract);
        var profile = ThumbnailCompositionProfiles.Resolve(contract);
        var directing = VisualDirectingProfiles.Resolve(contract);
        var familyDirector = FamilyDirectors.Resolve(contract);
        var language = NormalizeLanguage(contract.Brand.LocalizationRules.FirstOrDefault() ?? "en");
        var terms = LocalizedPromptTerms.For(language);
        var isPortrait = IsPortrait(contract);
        var isSquare = contract.Platform.CompositionProfile.Contains("square", StringComparison.OrdinalIgnoreCase) || contract.Platform.AspectRatio is "1:1" or "1x1";
        var sections = contract.PromptSections is { Count: > 0 } ? contract.PromptSections : BuildDefaultSections(contract);
        sections = sections.Select(section => section.Category.Contains("Observation", StringComparison.OrdinalIgnoreCase)
            ? section with { Content = BuildObservationPrompt(contract, terms, isPortrait, isSquare) }
            : section).ToArray();
        var removed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var included = FilterSections(contract, strategy, sections, removed);
        EnforceInformationBudget(strategy, included, removed);

        var prompt = string.Join(Environment.NewLine, included.Select(s => $"{s.Category.ToUpperInvariant()}: {s.Content.Trim()}"));
        prompt = string.Join(Environment.NewLine, prompt, strategy.PromptGuidance, profile.PromptGuidance, directing.PromptGuidance, familyDirector.PromptGuidance, ThumbnailArtworkPromptRules.PositiveArtworkOnlyInstruction).Trim();
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

    private static bool IsPortrait(ThumbnailPromptContract contract) =>
        contract.Platform.CompositionProfile.Contains("portrait", StringComparison.OrdinalIgnoreCase) ||
        contract.Platform.AspectRatio is "9:16" or "9x16";

    private static string BuildObservationPrompt(ThumbnailPromptContract contract, LocalizedPromptTerms terms, bool isPortrait, bool isSquare)
    {
        var fields = ThumbnailFieldFormatter.Format(contract.Observation, terms.Language);

        if (isPortrait)
            return $"portrait cover badge: {terms.Title}; {terms.Date}: {fields.Date}; {terms.BestTime}: {fields.BestTime}; {terms.Direction}: {fields.Direction}. Date, Best Time, and Direction only; no equipment, no separation, no observation window, no bottom strip, no guide table, no action prompt; keep planets circular.";

        if (isSquare)
        {
            var equipmentText = string.IsNullOrWhiteSpace(fields.Equipment) ? string.Empty : $"; {terms.Equipment}: {fields.Equipment}";
            var separationText = string.IsNullOrWhiteSpace(fields.Separation) ? string.Empty : $"; {terms.Separation}: {fields.Separation}";
            return $"medium-density square guide fields: {terms.Title}; {terms.Date}: {fields.Date}; {terms.BestTime}: {fields.BestTime}; {terms.Direction}: {fields.Direction}{equipmentText}{separationText}. No action prompt; no squeezed landscape composition.";
        }

        var landscapeSeparation = string.IsNullOrWhiteSpace(fields.Separation) ? string.Empty : $"; {terms.Separation}: {fields.Separation}";
        return $"premium landscape glass observation card: {terms.Date}: {fields.Date}; {terms.BestTime}: {fields.BestTime}; {terms.Direction}: {fields.Direction}; {terms.Equipment}: {fields.Equipment}{landscapeSeparation}; {terms.ObjectLabels}: every primary object. Do not render action-prompt text, long date ranges, technical wording, internal IDs, or footer tips.";
    }

    private static string NormalizeLanguage(string value) => value.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? "hi" : "en";

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
    public static readonly CompositionProfile PortraitProfile = new("PortraitProfile", "9:16", ["YouTube Shorts", "Instagram Reels"], ["premium Shorts cover", "large top title", "huge circular celestial composition", "small bottom observation badge", "Jupiter-dominant planetary scale", "impossible landscape crop", "avoid squeezed landscape look"], ["COMPOSITION PROFILE: PortraitProfile for native 9:16 YouTube Shorts and Instagram Reels covers.", "Use portrait cover art direction: large cinematic title at top, huge circular celestial bodies in the middle, and one small premium bottom badge for Date, Best Time, and Direction only.", "Make Jupiter dominant and Venus supportive with natural separation, natural lighting, natural proportion, and no stretched, squashed, or elongated planets.", "The composition must feel impossible to crop into landscape: no side panels, no guide table, no bottom strip, no action prompt, no equipment, no separation, no observation window."], ["Portrait prompt must be generated natively for 9:16.", "CompositionProfile controls portrait framing, object dominance, phone readability, and the cover-first hierarchy."]);
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
    public static readonly PlatformStorytellingStrategy PortraitStrategy = new("PortraitStrategy", "Cover-minimal", ["YouTube Shorts", "Instagram Reels"], ["Title", "Dominant Object", "One Observation Hint", "Visual Scene", "Quality Rules"], 18, 1, "Footer disabled", "Only one small bottom badge with date, best time, and direction; no large fields", "Action prompt disabled for portrait covers", ["STORYTELLING STRATEGY: PortraitStrategy decides WHAT information exists for phone-first Shorts and Reels covers.", "Allowed visible information: large title plus Date, Best Time, and Direction only in one small premium badge.", "Forbidden: equipment, separation, observation window, guide table, bottom strip, action prompt, large cards, marketing language."], 1);
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
