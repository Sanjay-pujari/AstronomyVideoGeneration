using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

[JsonConverter(typeof(JsonStringEnumConverter<VisualIntelligenceOrchestrationStatus>))]
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
    public bool UseHeroPromptV4 { get; init; }
    public bool UseHeroImageV4Comparison { get; init; }
    public bool Enabled { get; init; }
    public bool WriteDiagnostics { get; init; }
    public bool ObservationMode { get; init; } = true;
    public ImageProviderType DefaultProvider { get; init; } = ImageProviderType.Unknown;

    public static VisualIntelligenceFlagSnapshot FromOptions(VisualIntelligenceOptions options) => new()
    {
        UseVisualCreativeDirector = options.UseVisualCreativeDirector,
        UseCDL = options.UseCDL,
        UseCreativeDirectionContract = options.UseCreativeDirectionContract,
        UsePromptComposerV2 = options.UsePromptComposerV2,
        UseProviderProfiles = options.UseProviderProfiles,
        UseQualityScoring = options.UseQualityScoring,
        UseQualityScoringBlocking = options.UseQualityScoringBlocking,
        UseExperimentalRenderingRules = options.UseExperimentalRenderingRules,
        UseHeroPromptV4 = options.UseHeroPromptV4,
        UseHeroImageV4Comparison = options.UseHeroImageV4Comparison,
        Enabled = options.Enabled,
        WriteDiagnostics = options.WriteDiagnostics,
        ObservationMode = options.ObservationMode,
        DefaultProvider = options.DefaultProvider
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
    public Guid? ContentGenerationPlanId { get; init; }
    public Guid? AstronomyEventIntelligenceId { get; init; }
    public ContractEventFamily EventFamily { get; init; } = ContractEventFamily.Unknown;
    public string EventType { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public string Language { get; init; } = "en";
    public Platform Platform { get; init; } = Platform.Unknown;
    public AspectRatio AspectRatio { get; init; } = AspectRatio.Unknown;
    public string RequestedAssetType { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public List<string> PrimaryObjects { get; init; } = [];
    public List<string> SupportingObjects { get; init; } = [];
    public DateTimeOffset? ObservationDateTime { get; init; }
    public string VisibilityGuidance { get; init; } = string.Empty;
    public string? RunOutputFolder { get; init; }
    public ImageProviderType RequestedProvider { get; init; } = ImageProviderType.Unknown;
}

public sealed record VisualIntelligenceOrchestrationContext
{
    public string CorrelationId { get; init; } = string.Empty;
    public Guid? ContentGenerationPlanId { get; init; }
    public Guid? AstronomyEventIntelligenceId { get; init; }
    public ContractEventFamily EventFamily { get; init; } = ContractEventFamily.Unknown;
    public string EventType { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public string Language { get; init; } = "en";
    public Platform Platform { get; init; } = Platform.Unknown;
    public AspectRatio AspectRatio { get; init; } = AspectRatio.Unknown;
    public string RequestedAssetType { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public List<string> PrimaryObjects { get; init; } = [];
    public List<string> SupportingObjects { get; init; } = [];
    public DateTimeOffset? ObservationDateTime { get; init; }
    public string VisibilityGuidance { get; init; } = string.Empty;
    public string? RunOutputFolder { get; init; }
    public ImageProviderType RequestedProvider { get; init; } = ImageProviderType.Unknown;
    public VisualIntelligenceFlagSnapshot FeatureFlags { get; init; } = new();
    public VisualIntelligenceVersionSnapshot Versions { get; init; } = new();
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
}

public sealed record VisualCreativeDirectorResult
{
    public CDL? Cdl { get; init; }
    public CreativeDirectionContract? CreativeDirectionContract { get; init; }
    public PromptPackage? PromptPackage { get; init; }
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
}

public sealed record VisualIntelligenceOrchestrationResult
{
    public VisualIntelligenceOrchestrationStatus Status { get; init; }
    public VisualIntelligenceOrchestrationContext Context { get; init; } = new();
    public CDL? Cdl { get; init; }
    public CreativeDirectionContract? CreativeDirectionContract { get; init; }
    public PromptPackage? PromptPackage { get; init; }
    public QualityReport? QualityReport { get; init; }
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
    public bool FallbackApplied { get; init; }
    public string? FallbackReason { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    public long DurationMs { get; init; }
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
    private readonly IPromptComposerV2 promptComposer;
    private readonly ICreativeQualityScoringEngine? qualityScoringEngine;
    private readonly ILogger<VisualIntelligenceOrchestrator> logger;
    private readonly IAICinematicImageGenerator? imageGenerator;

    public VisualIntelligenceOrchestrator(IOptions<VisualIntelligenceOptions> options, IVisualCreativeDirector director, IPromptComposerV2 promptComposer, ILogger<VisualIntelligenceOrchestrator> logger)
        : this(options, director, promptComposer, null, logger, null) { }

    public VisualIntelligenceOrchestrator(IOptions<VisualIntelligenceOptions> options, IVisualCreativeDirector director, IPromptComposerV2 promptComposer, ICreativeQualityScoringEngine? qualityScoringEngine, ILogger<VisualIntelligenceOrchestrator> logger, IAICinematicImageGenerator? imageGenerator = null)
    {
        this.options = options;
        this.director = director;
        this.promptComposer = promptComposer;
        this.qualityScoringEngine = qualityScoringEngine;
        this.logger = logger;
        this.imageGenerator = imageGenerator;
    }

    public async Task<VisualIntelligenceOrchestrationResult> OrchestrateAsync(VisualIntelligenceOrchestrationRequest request, CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var diagnostics = new List<DiagnosticMessage>();
        var context = CreateContext(request, diagnostics);
        if (!options.Value.Enabled)
        {
            diagnostics.Add(Info("visual_intelligence.disabled", "Visual Intelligence is disabled; orchestration skipped."));
            logger.LogInformation("Visual Intelligence disabled. CorrelationId={CorrelationId}", context.CorrelationId);
            return await CompleteAsync(new VisualIntelligenceOrchestrationResult { Status = VisualIntelligenceOrchestrationStatus.Disabled, Context = context with { Diagnostics = diagnostics }, Diagnostics = diagnostics, StartedAtUtc = startedAtUtc }, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Visual Intelligence observation started. CorrelationId={CorrelationId} EventFamily={EventFamily} Platform={Platform} ObservationMode={ObservationMode}", context.CorrelationId, context.EventFamily, context.Platform, options.Value.ObservationMode);
        logger.LogInformation("Visual intelligence flags snapshot. CorrelationId={CorrelationId} {@FeatureFlags}", context.CorrelationId, context.FeatureFlags);

        if (!context.FeatureFlags.UseVisualCreativeDirector)
        {
            diagnostics.Add(Info("visual_intelligence.disabled", "VisualCreativeDirector feature flag is disabled; orchestration skipped."));
            logger.LogInformation("Visual Intelligence disabled. CorrelationId={CorrelationId}", context.CorrelationId);
            return await CompleteAsync(new VisualIntelligenceOrchestrationResult { Status = VisualIntelligenceOrchestrationStatus.Disabled, Context = context with { Diagnostics = diagnostics }, Diagnostics = diagnostics, StartedAtUtc = startedAtUtc }, cancellationToken).ConfigureAwait(false);
        }

        if (!context.FeatureFlags.UseCDL && !context.FeatureFlags.UseCreativeDirectionContract)
        {
            diagnostics.Add(Info("visual_intelligence.skipped", "No Visual Intelligence output contract flags are enabled; orchestration skipped."));
            return await CompleteAsync(new VisualIntelligenceOrchestrationResult { Status = VisualIntelligenceOrchestrationStatus.Skipped, Context = context with { Diagnostics = diagnostics }, Diagnostics = diagnostics, StartedAtUtc = startedAtUtc }, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            logger.LogInformation("VisualCreativeDirector started. CorrelationId={CorrelationId}", context.CorrelationId);
            var direction = await director.CreateDirectionAsync(context with { Diagnostics = diagnostics }, cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(direction.Diagnostics);
            var resolvedContext = context with { EventFamily = direction.CreativeDirectionContract?.EventFamily ?? ResolveFamilyFromCdl(direction.Cdl) ?? context.EventFamily, Diagnostics = diagnostics };
            PromptPackage? promptPackage = null;
            if (context.FeatureFlags.UsePromptComposerV2)
            {
                var promptResult = await promptComposer.ComposeAsync(direction.Cdl, direction.CreativeDirectionContract, ResolveRequestedProvider(direction.CreativeDirectionContract), cancellationToken).ConfigureAwait(false);
                diagnostics.AddRange(promptResult.Diagnostics);
                promptPackage = promptResult.PromptPackage;
            }
            else
            {
                diagnostics.Add(Info("prompt_composer_v2.skipped", "PromptComposerV2 feature flag is disabled; prompt package composition skipped."));
            }
            QualityReport? qualityReport = null;
            if (context.FeatureFlags.UseQualityScoring)
            {
                var scorer = qualityScoringEngine ?? new CreativeQualityScoringEngine(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<CreativeQualityScoringEngine>.Instance);
                qualityReport = await scorer.ScoreAsync(new CreativeQualityScoringRequest { Context = resolvedContext, Cdl = direction.Cdl, CreativeDirectionContract = direction.CreativeDirectionContract, PromptPackage = promptPackage, Diagnostics = diagnostics }, cancellationToken).ConfigureAwait(false);
                diagnostics.AddRange(qualityReport.Diagnostics.Where(d => !diagnostics.Any(existing => existing.Code == d.Code && existing.Message == d.Message)));
            }
            logger.LogInformation("VisualCreativeDirector completed. CorrelationId={CorrelationId} CdlGenerated={CdlGenerated} ContractGenerated={ContractGenerated}", context.CorrelationId, direction.Cdl is not null, direction.CreativeDirectionContract is not null);
            logger.LogInformation("Visual Intelligence generated artifacts summary. CorrelationId={CorrelationId} Cdl={CdlGenerated} Contract={ContractGenerated} PromptPackage={PromptPackageGenerated} QualityReport={QualityReportGenerated}", context.CorrelationId, direction.Cdl is not null, direction.CreativeDirectionContract is not null, promptPackage is not null, qualityReport is not null);
            diagnostics.Add(Info("visual_intelligence.observation_advisory_only", "Observation mode artifacts are advisory only; active prompts, Azure calls, and publication decisions are unchanged."));
            return await CompleteAsync(new VisualIntelligenceOrchestrationResult { Status = VisualIntelligenceOrchestrationStatus.Success, Context = resolvedContext, Cdl = direction.Cdl, CreativeDirectionContract = direction.CreativeDirectionContract, PromptPackage = promptPackage, QualityReport = qualityReport, Diagnostics = diagnostics, StartedAtUtc = startedAtUtc }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new DiagnosticMessage { Severity = DiagnosticSeverity.Error, Code = "visual_intelligence.fallback", Message = "Visual Intelligence orchestration failed; safe fallback applied.", Source = nameof(VisualIntelligenceOrchestrator), Metadata = new Dictionary<string, object?> { ["exceptionType"] = ex.GetType().Name } });
            logger.LogWarning(ex, "Visual intelligence fallback applied. CorrelationId={CorrelationId}", context.CorrelationId);
            return await CompleteAsync(new VisualIntelligenceOrchestrationResult { Status = VisualIntelligenceOrchestrationStatus.FallbackApplied, Context = context with { Diagnostics = diagnostics }, Diagnostics = diagnostics, FallbackApplied = true, FallbackReason = ex.Message, StartedAtUtc = startedAtUtc }, cancellationToken).ConfigureAwait(false);
        }
    }

    private VisualIntelligenceOrchestrationContext CreateContext(VisualIntelligenceOrchestrationRequest request, List<DiagnosticMessage> diagnostics)
    {
        var flags = VisualIntelligenceFlagSnapshot.FromOptions(options.Value ?? new VisualIntelligenceOptions());
        diagnostics.Add(Info("visual_intelligence.context_created", "Visual Intelligence orchestration context created."));
        if (request.EventFamily == ContractEventFamily.Unknown && !string.IsNullOrWhiteSpace(request.EventType))
            diagnostics.Add(Info("visual_intelligence.event_family_placeholder", "Event family profile resolution placeholder used."));
        return new VisualIntelligenceOrchestrationContext
        {
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId,
            ContentGenerationPlanId = request.ContentGenerationPlanId,
            AstronomyEventIntelligenceId = request.AstronomyEventIntelligenceId,
            EventFamily = request.EventFamily,
            EventType = request.EventType,
            EventName = request.EventName,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language,
            Platform = request.Platform,
            AspectRatio = request.AspectRatio,
            RequestedAssetType = request.RequestedAssetType,
            Region = request.Region,
            Location = request.Location,
            PrimaryObjects = request.PrimaryObjects,
            SupportingObjects = request.SupportingObjects,
            ObservationDateTime = request.ObservationDateTime,
            VisibilityGuidance = request.VisibilityGuidance,
            RunOutputFolder = request.RunOutputFolder,
            RequestedProvider = request.RequestedProvider,
            FeatureFlags = flags,
            Versions = new VisualIntelligenceVersionSnapshot(),
            Diagnostics = diagnostics
        };
    }

    private static ContractEventFamily? ResolveFamilyFromCdl(CDL? cdl)
    {
        if (cdl?.ExtensionFields.TryGetValue("eventFamily", out var value) != true || value is null)
            return null;
        return Enum.TryParse<ContractEventFamily>(value.ToString(), ignoreCase: true, out var family) ? family : null;
    }

    private ImageProviderType ResolveRequestedProvider(CreativeDirectionContract? contract)
    {
        if (!options.Value.UseProviderProfiles)
            return ImageProviderType.Unknown;

        var requested = contract?.ProviderHints.PreferredProvider ?? ImageProviderType.Unknown;
        return requested == ImageProviderType.Unknown ? options.Value.DefaultProvider : requested;
    }

    private async Task<VisualIntelligenceOrchestrationResult> CompleteAsync(VisualIntelligenceOrchestrationResult result, CancellationToken cancellationToken)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        result = result with
        {
            CompletedAtUtc = completedAtUtc,
            DurationMs = result.StartedAtUtc == default ? 0 : Math.Max(0, (long)(completedAtUtc - result.StartedAtUtc).TotalMilliseconds)
        };

        if (options.Value.WriteDiagnostics)
        {
            try
            {
                var path = await WriteDiagnosticsAsync(result, cancellationToken).ConfigureAwait(false);
                result.Diagnostics.Add(Info("visual_intelligence.diagnostics_written", $"Visual Intelligence diagnostics written to {path}."));
                logger.LogInformation("Visual Intelligence diagnostics written. CorrelationId={CorrelationId} DiagnosticsPath={DiagnosticsPath}", result.Context.CorrelationId, path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Diagnostics.Add(new DiagnosticMessage { Severity = DiagnosticSeverity.Warning, Code = "visual_intelligence.diagnostics_write_failed", Message = "Visual Intelligence diagnostics writing failed; pipeline execution continues.", Source = nameof(VisualIntelligenceOrchestrator), Metadata = new Dictionary<string, object?> { ["exceptionType"] = ex.GetType().Name } });
                logger.LogWarning(ex, "Visual Intelligence diagnostics writing failed. CorrelationId={CorrelationId}", result.Context.CorrelationId);
            }
        }
        else
        {
            result.Diagnostics.Add(Info("visual_intelligence.diagnostics_disabled", "Visual Intelligence diagnostic file writing is disabled."));
        }

        await TryGenerateHeroImageV4ComparisonAsync(result, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Visual Intelligence observation completed. CorrelationId={CorrelationId} Status={Status} FallbackApplied={FallbackApplied}", result.Context.CorrelationId, result.Status, result.FallbackApplied);
        return result;
    }

    private async Task TryGenerateHeroImageV4ComparisonAsync(VisualIntelligenceOrchestrationResult result, CancellationToken cancellationToken)
    {
        if (!result.Context.FeatureFlags.UseHeroImageV4Comparison) return;
        try
        {
            logger.LogInformation("V4 comparison started");
            var heroDirectory = string.IsNullOrWhiteSpace(result.Context.RunOutputFolder) ? string.Empty : Path.Combine(result.Context.RunOutputFolder, "hero");
            var productionHeroPath = Path.Combine(heroDirectory, "hero.png");
            if (string.IsNullOrWhiteSpace(heroDirectory) || !File.Exists(productionHeroPath))
                throw new FileNotFoundException("Production Hero image was not found for V4 comparison.", productionHeroPath);

            var comparisonDirectory = Path.Combine(heroDirectory, "comparison");
            Directory.CreateDirectory(comparisonDirectory);
            var v3Prompt = ResolveLegacyHeroPrompt(result);
            var v4Prompt = result.PromptPackage?.PositivePrompt ?? string.Empty;
            await File.WriteAllTextAsync(Path.Combine(comparisonDirectory, "hero-v3-prompt.txt"), v3Prompt, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(comparisonDirectory, "hero-v4-prompt.txt"), v4Prompt, cancellationToken).ConfigureAwait(false);

            var v3ImagePath = Path.Combine(comparisonDirectory, "hero-v3.png");
            File.Copy(productionHeroPath, v3ImagePath, overwrite: true);
            logger.LogInformation("V3 hero copied");

            var v4ImagePath = Path.Combine(comparisonDirectory, "hero-v4.png");
            var v4Generated = false;
            var provider = imageGenerator?.IsConfigured == true ? $"AzureOpenAIForImage:{imageGenerator.DeploymentName}" : "AzureOpenAIForImage:NotConfigured";
            if (imageGenerator?.IsConfigured == true && !string.IsNullOrWhiteSpace(v4Prompt))
            {
                var generation = await imageGenerator.GenerateAsync(new AICinematicAssetRequest("hero-v4-experimental", "hero-v4-comparison", "HeroEvent", "V4Comparison", "hero-v4-experimental", "ExperimentalComparisonOnly", "Manual review", "Still", "V4 experimental hero comparison only", v4Prompt, "No text, labels, watermarks, logos, UI, or production replacement.", 1792, 1024, v4ImagePath), cancellationToken).ConfigureAwait(false);
                v4Generated = string.Equals(generation.GenerationStatus, "Generated", StringComparison.OrdinalIgnoreCase) && File.Exists(v4ImagePath);
            }
            if (!v4Generated)
                CreatePlaceholderV4Image(v3ImagePath, v4ImagePath, "V4 Experimental\nAzure Image provider not configured");
            logger.LogInformation("V4 hero generated");

            var sideBySidePath = Path.Combine(comparisonDirectory, "hero-side-by-side.png");
            CreateSideBySide(v3ImagePath, v4ImagePath, sideBySidePath);
            logger.LogInformation("side-by-side created");

            await File.WriteAllTextAsync(Path.Combine(comparisonDirectory, "hero-comparison.json"), JsonSerializer.Serialize(new
            {
                planId = result.Context.ContentGenerationPlanId?.ToString() ?? result.Context.CorrelationId,
                eventType = result.Context.EventType,
                eventFamily = result.Context.EventFamily.ToString(),
                provider,
                v3PromptLength = v3Prompt.Length,
                v4PromptLength = v4Prompt.Length,
                v4Generated,
                productionUnchanged = File.Exists(productionHeroPath) && !string.Equals(Path.GetFullPath(productionHeroPath), Path.GetFullPath(v4ImagePath), StringComparison.OrdinalIgnoreCase),
                sideBySidePath,
                recommendation = "ManualReviewRequired"
            }, VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true)), cancellationToken).ConfigureAwait(false);
            logger.LogInformation("production unchanged");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "V4 Hero image comparison failed; continuing pipeline.");
            result.Diagnostics.Add(new DiagnosticMessage { Severity = DiagnosticSeverity.Warning, Code = "hero_image_v4_comparison.failed", Message = "V4 Hero image comparison failed; pipeline continues.", Source = nameof(VisualIntelligenceOrchestrator), Metadata = new Dictionary<string, object?> { ["exceptionType"] = ex.GetType().Name } });
        }
    }

    private static string ResolveLegacyHeroPrompt(VisualIntelligenceOrchestrationResult result)
        => result.CreativeDirectionContract is null
            ? "Legacy production Hero prompt retained by active Hero renderer."
            : $"Legacy production Hero prompt retained by active Hero renderer. Event: {result.CreativeDirectionContract.EventFamily}. Subject: {result.CreativeDirectionContract.VisualIntent.PrimarySubject}. Production Azure Hero request remains unchanged.";

    private static void CreatePlaceholderV4Image(string sourcePath, string outputPath, string label)
    {
        using var image = Image.Load<Rgba32>(sourcePath);
        image.Mutate(ctx => { ctx.GaussianBlur(8f); ctx.Fill(Color.Black.WithAlpha(0.42f)); DrawLabel(ctx, label, 32, 48); });
        image.Save(outputPath);
    }

    private static void CreateSideBySide(string leftPath, string rightPath, string outputPath)
    {
        using var left = Image.Load<Rgba32>(leftPath);
        using var right = Image.Load<Rgba32>(rightPath);
        var width = Math.Max(left.Width, right.Width);
        var height = Math.Max(left.Height, right.Height);
        left.Mutate(x => x.Resize(width, height));
        right.Mutate(x => x.Resize(width, height));
        using var canvas = new Image<Rgba32>(width * 2, height, Color.Black);
        canvas.Mutate(ctx => { ctx.DrawImage(left, new Point(0, 0), 1f); ctx.DrawImage(right, new Point(width, 0), 1f); DrawLabel(ctx, "V3 Production", 24, 24); DrawLabel(ctx, "V4 Experimental", width + 24, 24); });
        canvas.Save(outputPath);
    }

    private static void DrawLabel(IImageProcessingContext ctx, string text, float x, float y)
    {
        var family = SystemFonts.Collection.Families.FirstOrDefault();
        var font = family.CreateFont(30, FontStyle.Bold);
        var options = new RichTextOptions(font) { Origin = new PointF(x, y) };
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(x + 2, y + 2) }, text, Color.Black);
        ctx.DrawText(options, text, Color.White);
    }

    private async Task<string> WriteDiagnosticsAsync(VisualIntelligenceOrchestrationResult result, CancellationToken cancellationToken)
    {
        var folder = ResolveDiagnosticsFolder(result.Context, out var fallbackApplied, out var fallbackReason);
        Directory.CreateDirectory(folder);
        var json = VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true);
        var generatedArtifacts = new List<string>();
        if (result.Cdl is not null) { await WriteJsonAsync(folder, "CDL.json", result.Cdl, json, cancellationToken).ConfigureAwait(false); generatedArtifacts.Add("CDL.json"); }
        if (result.CreativeDirectionContract is not null) { await WriteJsonAsync(folder, "CreativeDirectionContract.json", result.CreativeDirectionContract, json, cancellationToken).ConfigureAwait(false); generatedArtifacts.Add("CreativeDirectionContract.json"); }
        if (result.PromptPackage is not null) { await WriteJsonAsync(folder, "PromptPackage.json", result.PromptPackage, json, cancellationToken).ConfigureAwait(false); generatedArtifacts.Add("PromptPackage.json"); }
        if (result.QualityReport is not null) { await WriteJsonAsync(folder, "QualityReport.json", result.QualityReport, json, cancellationToken).ConfigureAwait(false); generatedArtifacts.Add("QualityReport.json"); }
        generatedArtifacts.Add("OrchestrationSummary.json");
        await WriteJsonAsync(folder, "OrchestrationSummary.json", CreateSummary(result, folder, generatedArtifacts, fallbackApplied, fallbackReason), json, cancellationToken).ConfigureAwait(false);
        return folder;
    }

    private static Task WriteJsonAsync<T>(string folder, string fileName, T value, JsonSerializerOptions json, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(folder, fileName), JsonSerializer.Serialize(value, json), cancellationToken);

    private string ResolveDiagnosticsFolder(VisualIntelligenceOrchestrationContext context, out bool fallbackApplied, out string? fallbackReason)
    {
        fallbackApplied = false;
        fallbackReason = null;
        if (!string.IsNullOrWhiteSpace(options.Value.DiagnosticsOutputPath))
            return Path.Combine(options.Value.DiagnosticsOutputPath, Sanitize(context.CorrelationId));
        if (!string.IsNullOrWhiteSpace(context.RunOutputFolder))
            return Path.Combine(context.RunOutputFolder, "diagnostics", "visual-intelligence");
        fallbackApplied = true;
        fallbackReason = "runOutputFolder unavailable";
        return Path.Combine(AppContext.BaseDirectory, "diagnostics", "visual-intelligence", Sanitize(context.CorrelationId));
    }

    private static object CreateSummary(VisualIntelligenceOrchestrationResult result, string diagnosticsOutputPath, IReadOnlyList<string> generatedArtifacts, bool diagnosticsPathFallbackApplied, string? diagnosticsPathFallbackReason) => new
    {
        result.Context.CorrelationId,
        result.Context.ContentGenerationPlanId,
        result.Context.AstronomyEventIntelligenceId,
        EventFamily = result.CreativeDirectionContract?.EventFamily ?? ResolveFamilyFromCdl(result.Cdl) ?? result.Context.EventFamily,
        result.Context.EventType,
        result.Context.Language,
        result.Context.Platform,
        result.Context.AspectRatio,
        Provider = result.Context.RequestedProvider == ImageProviderType.Unknown ? result.Context.FeatureFlags.DefaultProvider : result.Context.RequestedProvider,
        ObservationMode = result.Context.FeatureFlags.ObservationMode,
        DiagnosticsOutputPath = diagnosticsOutputPath,
        FeatureFlags = result.Context.FeatureFlags,
        StageExecutionSummary = new
        {
            VisualCreativeDirector = result.Cdl is not null || result.CreativeDirectionContract is not null ? "Success" : result.Context.FeatureFlags.UseVisualCreativeDirector ? "Skipped" : "Disabled",
            CDL = result.Cdl is not null ? "Success" : result.Context.FeatureFlags.UseCDL ? "Skipped" : "Disabled",
            CreativeDirectionContract = result.CreativeDirectionContract is not null ? "Success" : result.Context.FeatureFlags.UseCreativeDirectionContract ? "Skipped" : "Disabled",
            PromptComposerV2 = result.PromptPackage is not null ? "Success" : result.Context.FeatureFlags.UsePromptComposerV2 ? "Skipped" : "Disabled",
            QualityScoring = result.QualityReport is not null ? "Success" : result.Context.FeatureFlags.UseQualityScoring ? "Skipped" : "Disabled"
        },
        GeneratedArtifacts = generatedArtifacts,
        result.FallbackApplied,
        result.FallbackReason,
        DiagnosticsPathFallbackApplied = diagnosticsPathFallbackApplied,
        DiagnosticsPathFallbackReason = diagnosticsPathFallbackReason,
        DisabledFeatureFlags = GetDisabledFeatureFlags(result.Context.FeatureFlags),
        Diagnostics = result.Diagnostics,
        Warnings = result.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error or DiagnosticSeverity.Blocking).ToList(),
        result.StartedAtUtc,
        result.CompletedAtUtc,
        result.DurationMs
    };

    private static IReadOnlyList<string> GetDisabledFeatureFlags(VisualIntelligenceFlagSnapshot flags)
    {
        var disabled = new List<string>();
        if (!flags.UseVisualCreativeDirector) disabled.Add(nameof(flags.UseVisualCreativeDirector));
        if (!flags.UseCDL) disabled.Add(nameof(flags.UseCDL));
        if (!flags.UseCreativeDirectionContract) disabled.Add(nameof(flags.UseCreativeDirectionContract));
        if (!flags.UsePromptComposerV2) disabled.Add(nameof(flags.UsePromptComposerV2));
        if (!flags.UseProviderProfiles) disabled.Add(nameof(flags.UseProviderProfiles));
        if (!flags.UseQualityScoring) disabled.Add(nameof(flags.UseQualityScoring));
        if (!flags.UseQualityScoringBlocking) disabled.Add(nameof(flags.UseQualityScoringBlocking));
        if (!flags.UseExperimentalRenderingRules) disabled.Add(nameof(flags.UseExperimentalRenderingRules));
        if (!flags.UseHeroPromptV4) disabled.Add(nameof(flags.UseHeroPromptV4));
        if (!flags.UseHeroImageV4Comparison) disabled.Add(nameof(flags.UseHeroImageV4Comparison));
        return disabled;
    }

    private static string Sanitize(string value) => string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    private static DiagnosticMessage Info(string code, string message) => new() { Severity = DiagnosticSeverity.Info, Code = code, Message = message, Source = nameof(VisualIntelligenceOrchestrator) };
}

public static class VisualIntelligenceServiceCollectionExtensions
{
    public static IServiceCollection AddVisualIntelligenceOrchestration(this IServiceCollection services)
    {
        services.AddOptions<VisualIntelligenceOptions>();
        services.AddScoped<IFamilyCreativeProfile, PlanetGroupingCreativeProfile>();
        services.AddScoped<IFamilyCreativeProfile, PlanetPairingCreativeProfile>();
        services.AddScoped<IFamilyCreativeProfile, MeteorShowerCreativeProfile>();
        services.AddScoped<IFamilyCreativeProfile, NamedFullMoonCreativeProfile>();
        services.AddScoped<IFamilyCreativeProfile, SolarEclipseCreativeProfile>();
        services.AddScoped<IFamilyCreativeProfile, LunarEclipseCreativeProfile>();
        services.AddScoped<IFamilyCreativeProfile, GenericAstronomyCreativeProfile>();
        services.AddSingleton<IImageProviderProfile, GenericImageProviderProfile>();
        services.AddSingleton<IImageProviderProfile, AzureImageProviderProfile>();
        services.AddSingleton<IImageProviderProfileRegistry, ImageProviderProfileRegistry>();
        services.AddScoped<IPromptSectionBuilder, PromptSectionBuilder>();
        services.AddScoped<IPromptOptimizer, PromptOptimizer>();
        services.AddScoped<GenericProviderAdapter>();
        services.AddScoped<AzurePromptProviderAdapter>();
        services.AddScoped<IProviderAdapter>(sp => new ProviderAdapterResolver([sp.GetRequiredService<AzurePromptProviderAdapter>(), sp.GetRequiredService<GenericProviderAdapter>()]));
        services.AddScoped<IPromptPackageBuilder, PromptPackageBuilder>();
        services.AddScoped<IPromptComposerV2, PromptComposerV2>();
        services.AddScoped<IHeroPromptMigrationService, HeroPromptMigrationService>();
        services.AddScoped<ICreativeQualityScoringEngine, CreativeQualityScoringEngine>();
        services.AddScoped<IFamilyCreativeProfileResolver, FamilyCreativeProfileResolver>();
        services.AddScoped<IVisualCreativeDirector, VisualCreativeDirector>();
        services.AddScoped<IVisualIntelligenceOrchestrator, VisualIntelligenceOrchestrator>();
        return services;
    }
}
