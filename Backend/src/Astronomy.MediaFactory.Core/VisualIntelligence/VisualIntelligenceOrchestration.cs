using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public enum VisualIntelligenceOrchestrationStatus
{
    Disabled = 0,
    Skipped,
    Success,
    FallbackApplied,
    Failed
}

public sealed record VisualIntelligenceFlagSnapshot
{
    public bool UseVisualCreativeDirector { get; init; }
    public bool UseCDL { get; init; }
    public bool UseCreativeDirectionContract { get; init; }
    public bool UsePromptComposerV2 { get; init; }
    public bool UseProviderProfiles { get; init; }
    public bool UseQualityScoring { get; init; }
    public bool UseQualityScoringBlocking { get; init; }
    public bool UseExperimentalRenderingRules { get; init; }

    public static VisualIntelligenceFlagSnapshot FromOptions(VisualIntelligenceOptions options) => new()
    {
        UseVisualCreativeDirector = options.UseVisualCreativeDirector,
        UseCDL = options.UseCDL,
        UseCreativeDirectionContract = options.UseCreativeDirectionContract,
        UsePromptComposerV2 = options.UsePromptComposerV2,
        UseProviderProfiles = options.UseProviderProfiles,
        UseQualityScoring = options.UseQualityScoring,
        UseQualityScoringBlocking = options.UseQualityScoringBlocking,
        UseExperimentalRenderingRules = options.UseExperimentalRenderingRules
    };
}

public sealed record VisualIntelligenceVersionSnapshot
{
    public string ContractVersion { get; init; } = VisualIntelligenceContractVersions.ContractVersion;
    public string CdlVersion { get; init; } = VisualIntelligenceContractVersions.CdlVersion;
    public string BrandVersion { get; init; } = VisualIntelligenceContractVersions.BrandVersion;
    public string RenderingRulesVersion { get; init; } = VisualIntelligenceContractVersions.RenderingRulesVersion;
    public string PromptComposerVersion { get; init; } = VisualIntelligenceContractVersions.PromptComposerVersion;
    public string ProviderProfileVersion { get; init; } = VisualIntelligenceContractVersions.ProviderProfileVersion;
    public string QualityReportVersion { get; init; } = VisualIntelligenceContractVersions.QualityReportVersion;
}

public sealed record VisualIntelligenceOrchestrationRequest
{
    public string? CorrelationId { get; init; }
    public EventFamily EventFamily { get; init; } = EventFamily.Unknown;
    public string EventType { get; init; } = string.Empty;
    public string Language { get; init; } = "en";
    public Platform Platform { get; init; } = Platform.Unknown;
    public AspectRatio AspectRatio { get; init; } = AspectRatio.Unknown;
    public string RequestedAssetType { get; init; } = string.Empty;
}

public sealed record VisualIntelligenceOrchestrationContext
{
    public string CorrelationId { get; init; } = string.Empty;
    public EventFamily EventFamily { get; init; } = EventFamily.Unknown;
    public string EventType { get; init; } = string.Empty;
    public string Language { get; init; } = "en";
    public Platform Platform { get; init; } = Platform.Unknown;
    public AspectRatio AspectRatio { get; init; } = AspectRatio.Unknown;
    public string RequestedAssetType { get; init; } = string.Empty;
    public VisualIntelligenceFlagSnapshot FeatureFlags { get; init; } = new();
    public VisualIntelligenceVersionSnapshot Versions { get; init; } = new();
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
}

public sealed record VisualCreativeDirectorResult
{
    public CDL? Cdl { get; init; }
    public CreativeDirectionContract? CreativeDirectionContract { get; init; }
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
}

public sealed record VisualIntelligenceOrchestrationResult
{
    public VisualIntelligenceOrchestrationStatus Status { get; init; }
    public VisualIntelligenceOrchestrationContext Context { get; init; } = new();
    public CDL? Cdl { get; init; }
    public CreativeDirectionContract? CreativeDirectionContract { get; init; }
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
    public bool FallbackApplied { get; init; }
    public string? FallbackReason { get; init; }
}

public interface IVisualCreativeDirector
{
    Task<VisualCreativeDirectorResult> CreateDirectionAsync(VisualIntelligenceOrchestrationContext context, CancellationToken cancellationToken = default);
}

public interface IVisualIntelligenceOrchestrator
{
    Task<VisualIntelligenceOrchestrationResult> OrchestrateAsync(VisualIntelligenceOrchestrationRequest request, CancellationToken cancellationToken = default);
}

public sealed class StubVisualCreativeDirector : IVisualCreativeDirector
{
    public Task<VisualCreativeDirectorResult> CreateDirectionAsync(VisualIntelligenceOrchestrationContext context, CancellationToken cancellationToken = default)
    {
        var cdl = context.FeatureFlags.UseCDL ? new CDL
        {
            DocumentId = $"cdl_{context.CorrelationId}",
            Directives = [new CdlDirective("placeholder", "visual-intelligence-v3.3b", 0)]
        } : null;

        var contract = context.FeatureFlags.UseCreativeDirectionContract ? new CreativeDirectionContract
        {
            ContractId = $"cdc_{context.CorrelationId}",
            EventFamily = context.EventFamily,
            TargetPlatform = context.Platform,
            Language = context.Language,
            AspectRatio = context.AspectRatio,
            Cdl = cdl ?? new CDL { DocumentId = $"cdl_{context.CorrelationId}" }
        } : null;

        return Task.FromResult(new VisualCreativeDirectorResult
        {
            Cdl = cdl,
            CreativeDirectionContract = contract,
            Diagnostics = [Info("visual_intelligence.stub", "Placeholder VisualCreativeDirector result returned.")]
        });
    }

    private static DiagnosticMessage Info(string code, string message) => new() { Severity = DiagnosticSeverity.Info, Code = code, Message = message, Source = nameof(StubVisualCreativeDirector) };
}

public sealed class VisualIntelligenceOrchestrator : IVisualIntelligenceOrchestrator
{
    private readonly IOptions<VisualIntelligenceOptions> options;
    private readonly IVisualCreativeDirector director;
    private readonly ILogger<VisualIntelligenceOrchestrator> logger;

    public VisualIntelligenceOrchestrator(IOptions<VisualIntelligenceOptions> options, IVisualCreativeDirector director, ILogger<VisualIntelligenceOrchestrator> logger)
    {
        this.options = options;
        this.director = director;
        this.logger = logger;
    }

    public async Task<VisualIntelligenceOrchestrationResult> OrchestrateAsync(VisualIntelligenceOrchestrationRequest request, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<DiagnosticMessage>();
        var context = CreateContext(request, diagnostics);
        logger.LogInformation("Visual intelligence orchestration started. CorrelationId={CorrelationId} EventFamily={EventFamily} Platform={Platform}", context.CorrelationId, context.EventFamily, context.Platform);
        logger.LogInformation("Visual intelligence flags snapshot. CorrelationId={CorrelationId} {@FeatureFlags}", context.CorrelationId, context.FeatureFlags);

        if (!context.FeatureFlags.UseVisualCreativeDirector)
        {
            diagnostics.Add(Info("visual_intelligence.disabled", "VisualCreativeDirector feature flag is disabled; orchestration skipped."));
            logger.LogInformation("Visual intelligence orchestration disabled/skipped. CorrelationId={CorrelationId}", context.CorrelationId);
            return Complete(new VisualIntelligenceOrchestrationResult { Status = VisualIntelligenceOrchestrationStatus.Disabled, Context = context with { Diagnostics = diagnostics }, Diagnostics = diagnostics });
        }

        if (!context.FeatureFlags.UseCDL && !context.FeatureFlags.UseCreativeDirectionContract)
        {
            diagnostics.Add(Info("visual_intelligence.skipped", "No Visual Intelligence output contract flags are enabled; orchestration skipped."));
            return Complete(new VisualIntelligenceOrchestrationResult { Status = VisualIntelligenceOrchestrationStatus.Skipped, Context = context with { Diagnostics = diagnostics }, Diagnostics = diagnostics });
        }

        try
        {
            var direction = await director.CreateDirectionAsync(context with { Diagnostics = diagnostics }, cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(direction.Diagnostics);
            return Complete(new VisualIntelligenceOrchestrationResult { Status = VisualIntelligenceOrchestrationStatus.Success, Context = context with { Diagnostics = diagnostics }, Cdl = direction.Cdl, CreativeDirectionContract = direction.CreativeDirectionContract, Diagnostics = diagnostics });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new DiagnosticMessage { Severity = DiagnosticSeverity.Error, Code = "visual_intelligence.fallback", Message = "Visual Intelligence orchestration failed; safe fallback applied.", Source = nameof(VisualIntelligenceOrchestrator), Metadata = new Dictionary<string, object?> { ["exceptionType"] = ex.GetType().Name } });
            logger.LogWarning(ex, "Visual intelligence fallback applied. CorrelationId={CorrelationId}", context.CorrelationId);
            return Complete(new VisualIntelligenceOrchestrationResult { Status = VisualIntelligenceOrchestrationStatus.FallbackApplied, Context = context with { Diagnostics = diagnostics }, Diagnostics = diagnostics, FallbackApplied = true, FallbackReason = ex.Message });
        }
    }

    private VisualIntelligenceOrchestrationContext CreateContext(VisualIntelligenceOrchestrationRequest request, List<DiagnosticMessage> diagnostics)
    {
        var flags = VisualIntelligenceFlagSnapshot.FromOptions(options.Value ?? new VisualIntelligenceOptions());
        diagnostics.Add(Info("visual_intelligence.context_created", "Visual Intelligence orchestration context created."));
        if (request.EventFamily == EventFamily.Unknown && !string.IsNullOrWhiteSpace(request.EventType))
            diagnostics.Add(Info("visual_intelligence.event_family_placeholder", "Event family profile resolution placeholder used."));
        return new VisualIntelligenceOrchestrationContext
        {
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId,
            EventFamily = request.EventFamily,
            EventType = request.EventType,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language,
            Platform = request.Platform,
            AspectRatio = request.AspectRatio,
            RequestedAssetType = request.RequestedAssetType,
            FeatureFlags = flags,
            Versions = new VisualIntelligenceVersionSnapshot(),
            Diagnostics = diagnostics
        };
    }

    private VisualIntelligenceOrchestrationResult Complete(VisualIntelligenceOrchestrationResult result)
    {
        logger.LogInformation("Visual intelligence orchestration completed. CorrelationId={CorrelationId} Status={Status} FallbackApplied={FallbackApplied}", result.Context.CorrelationId, result.Status, result.FallbackApplied);
        return result;
    }

    private static DiagnosticMessage Info(string code, string message) => new() { Severity = DiagnosticSeverity.Info, Code = code, Message = message, Source = nameof(VisualIntelligenceOrchestrator) };
}

public static class VisualIntelligenceServiceCollectionExtensions
{
    public static IServiceCollection AddVisualIntelligenceOrchestration(this IServiceCollection services)
    {
        services.AddOptions<VisualIntelligenceOptions>();
        services.AddScoped<IVisualCreativeDirector, StubVisualCreativeDirector>();
        services.AddScoped<IVisualIntelligenceOrchestrator, VisualIntelligenceOrchestrator>();
        return services;
    }
}
