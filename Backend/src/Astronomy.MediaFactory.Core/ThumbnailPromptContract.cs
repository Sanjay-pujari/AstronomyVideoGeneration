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

public sealed class ThumbnailPromptBuilder
{
    public ThumbnailPromptBuildResult Build(ThumbnailPromptContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ThumbnailPromptContractValidator.Validate(contract);
        return new ThumbnailPromptBuildResult(contract.Prompt.PositivePrompt, contract.Prompt.NegativePrompt);
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
