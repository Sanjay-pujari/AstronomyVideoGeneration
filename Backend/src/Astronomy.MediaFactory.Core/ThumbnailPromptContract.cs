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
    ThumbnailPromptDiagnostics Diagnostics);

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

public sealed record ThumbnailPromptBuildResult(string Prompt, string NegativePrompt);

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
        if (normalizedProfile.Contains("landscape", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "16:9" or "16x9")
            return LandscapeProfile;
        if (normalizedProfile.Contains("portrait", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "9:16" or "9x16")
            return PortraitProfile;
        if (normalizedProfile.Contains("square", StringComparison.OrdinalIgnoreCase) || normalizedAspect is "1:1" or "1x1")
            return SquareProfile;
        throw new InvalidOperationException($"Thumbnail composition profile validation failed: unsupported profile '{profileName}' for aspect ratio '{aspectRatio}'.");
    }

    public static readonly CompositionProfile LandscapeProfile = new(
        "LandscapeProfile",
        "16:9",
        ["YouTube", "Website"],
        [
            "wide cinematic composition",
            "balanced horizon",
            "strong negative space",
            "object occupies roughly 35-45% of frame",
            "suitable title region",
            "premium documentary feel"
        ],
        [
            "COMPOSITION PROFILE: LandscapeProfile for 16:9 YouTube and website surfaces.",
            "Use wide cinematic framing with horizontal storytelling, balanced horizon, strong negative space, a suitable title region, and a premium documentary feel.",
            "Keep the dominant event object roughly 35-45% of the frame without squeezing, padding, or borrowing another aspect-ratio layout."
        ],
        [
            "Landscape prompt must be generated natively for 16:9.",
            "Landscape guidance must not be reused by portrait or square profiles."
        ]);

    public static readonly CompositionProfile PortraitProfile = new(
        "PortraitProfile",
        "9:16",
        ["Shorts", "Instagram Reels"],
        [
            "vertical composition",
            "dominant subject",
            "large foreground object",
            "layered depth",
            "natural vertical framing",
            "avoid squeezed landscape look",
            "preserve sky storytelling"
        ],
        [
            "COMPOSITION PROFILE: PortraitProfile for 9:16 Shorts and Instagram Reels surfaces.",
            "Use vertical cinematic framing, a dominant foreground subject, layered depth, natural vertical framing, and phone-first composition.",
            "Preserve sky storytelling while avoiding any squeezed or cropped landscape look."
        ],
        [
            "Portrait prompt must be generated natively for 9:16.",
            "Portrait guidance must not be reused by landscape or square profiles."
        ]);

    public static readonly CompositionProfile SquareProfile = new(
        "SquareProfile",
        "1:1",
        ["Facebook", "Mobile"],
        [
            "balanced composition",
            "centered hierarchy",
            "symmetrical framing",
            "no cropped dominant object",
            "optimized for feed browsing"
        ],
        [
            "COMPOSITION PROFILE: SquareProfile for 1:1 Facebook and mobile feed surfaces.",
            "Use centered balanced composition, equal visual weight, centered hierarchy, and symmetrical framing optimized for feed browsing.",
            "Keep the dominant object fully visible without cropped dominance or borrowed landscape/portrait layout."
        ],
        [
            "Square prompt must be generated natively for 1:1.",
            "Square guidance must not be reused by landscape or portrait profiles."
        ]);

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace('_', ':');
}

public sealed class ThumbnailPromptBuilder
{
    public ThumbnailPromptBuildResult Build(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ThumbnailPromptContractValidator.Validate(contract);
        var profile = ThumbnailCompositionProfiles.Resolve(contract);
        var prompt = string.Join(Environment.NewLine, contract.Prompt.PositivePrompt.Trim(), profile.PromptGuidance);
        return new ThumbnailPromptBuildResult(prompt, contract.Prompt.NegativePrompt);
    }
}

public static class ThumbnailPromptContractValidator
{
    public static void Validate(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        Require(contract.ContractVersion, nameof(contract.ContractVersion));
        Require(contract.EventIdentity.EventId, "EventIdentity.EventId");
        Require(contract.EventIdentity.EventName, "EventIdentity.EventName");
        Require(contract.EventIdentity.EventFamily, "EventIdentity.EventFamily");
        Require(contract.Display.DisplayTitle, "Display.DisplayTitle");
        Require(contract.Display.LocalizedTitle, "Display.LocalizedTitle");
        Require(contract.Display.DisplayShortTitle, "Display.DisplayShortTitle");
        RequireAny(contract.Objects.PrimaryObjects, "Objects.PrimaryObjects");
        Require(contract.Observation.ObservationWindow, "Observation.ObservationWindow");
        Require(contract.Observation.BestViewingWindow, "Observation.BestViewingWindow");
        Require(contract.Observation.Direction, "Observation.Direction");
        Require(contract.Observation.Visibility, "Observation.Visibility");
        Require(contract.Visual.VisualIdentity, "Visual.VisualIdentity");
        Require(contract.Visual.EmotionalTone, "Visual.EmotionalTone");
        Require(contract.Visual.EducationalIntent, "Visual.EducationalIntent");
        Require(contract.Visual.CtrGoal, "Visual.CtrGoal");
        Require(contract.Platform.Platform, "Platform.Platform");
        Require(contract.Platform.AspectRatio, "Platform.AspectRatio");
        Require(contract.Platform.CompositionProfile, "Platform.CompositionProfile");
        Require(contract.Prompt.PositivePrompt, "Prompt.PositivePrompt");
        Require(contract.Prompt.NegativePrompt, "Prompt.NegativePrompt");
        RequireAny(contract.Prompt.RequiredObjects, "Prompt.RequiredObjects");
        Require(contract.Brand.TypographyPolicy, "Brand.TypographyPolicy");
        Require(contract.Brand.BrandStyle, "Brand.BrandStyle");
        RequireAny(contract.Validation.ValidationRules, "Validation.ValidationRules");
        RequireAny(contract.Validation.ScientificRules, "Validation.ScientificRules");
        RequireAny(contract.Validation.PlatformRules, "Validation.PlatformRules");
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"ThumbnailPromptContract validation failed: required field '{name}' is missing.");
    }

    private static void RequireAny(IReadOnlyCollection<string>? values, string name)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException($"ThumbnailPromptContract validation failed: required field '{name}' is missing.");
    }
}
