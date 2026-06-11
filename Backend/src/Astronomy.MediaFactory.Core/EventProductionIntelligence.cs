namespace Astronomy.MediaFactory.Core;

public sealed record ProductionEventIntelligence(
    string Domain,
    string EventType,
    string Title,
    string ShortTitle,
    DateTimeOffset? EventDate,
    DateTimeOffset? PeakUtc,
    string? LocalPeakTime,
    string? BestViewingWindowLocal,
    string? SkyDirectionHint,
    string? VisibilityRegion,
    IReadOnlyList<string> PrimaryObjects,
    IReadOnlyList<string> SecondaryObjects,
    string? ViewingQuality,
    string? MoonInterference,
    decimal? MoonIlluminationPercent,
    string? ScientificContext,
    IReadOnlyList<string> ViewerInstructions,
    IReadOnlyList<string> VisualMotifs,
    IReadOnlyList<string> SceneStrategy,
    IReadOnlyList<string> QualityWarnings,
    IReadOnlyList<string> ForbiddenTerms);

public sealed record MediaEventStrategyDefinition(
    string EventType,
    IReadOnlyList<string> QuestionTemplates,
    IReadOnlyList<string> SceneStoryArcShort,
    IReadOnlyList<string> SceneStoryArcLong,
    IReadOnlyList<string> VisualMotifs,
    IReadOnlyList<string> RequiredFactualFields,
    string NarrationTone,
    IReadOnlyList<string> ThumbnailHooks,
    IReadOnlyList<string> ForbiddenUnrelatedObjects,
    IReadOnlyList<string> ValidationRules);

public interface IMediaEventStrategy
{
    string EventType { get; }
    bool CanHandle(string eventType, string title);
    MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence);
}

public interface IMediaEventStrategyResolver
{
    IMediaEventStrategy Resolve(string eventType, string title);
}

public interface IEventProductionIntelligenceAdapter
{
    ProductionEventIntelligence Normalize(ProductionPipelineRequest request);
}

public sealed record ProductionValidationResult(
    bool IsValid,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public interface IProductionPipelineQualityValidator
{
    Task<ProductionValidationResult> ValidateBeforeVideoAssemblyAsync(
        ProductionEventIntelligence intelligence,
        string eventWorkingRoot,
        CancellationToken cancellationToken);

    Task<ProductionValidationResult> ValidateFinalOutputAsync(
        ProductionEventIntelligence intelligence,
        string outputRoot,
        CancellationToken cancellationToken);
}
