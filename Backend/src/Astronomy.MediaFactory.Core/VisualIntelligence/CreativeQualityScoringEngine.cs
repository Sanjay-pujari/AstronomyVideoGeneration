using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record CreativeQualityScoringRequest
{
    public VisualIntelligenceOrchestrationContext Context { get; init; } = new();
    public CDL? Cdl { get; init; }
    public CreativeDirectionContract? CreativeDirectionContract { get; init; }
    public PromptPackage? PromptPackage { get; init; }
    public IReadOnlyList<DiagnosticMessage> Diagnostics { get; init; } = [];
}

public interface ICreativeQualityScoringEngine
{
    Task<QualityReport> ScoreAsync(CreativeQualityScoringRequest request, CancellationToken cancellationToken = default);
}

public sealed class CreativeQualityScoringEngine(IOptions<VisualIntelligenceOptions> options, ILogger<CreativeQualityScoringEngine> logger) : ICreativeQualityScoringEngine
{
    private const double PassThreshold = 0.82d;

    public Task<QualityReport> ScoreAsync(CreativeQualityScoringRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var flags = request.Context.FeatureFlags ?? VisualIntelligenceFlagSnapshot.FromOptions(options.Value ?? new VisualIntelligenceOptions());
        if (!flags.UseQualityScoring)
        {
            return Task.FromResult(new QualityReport
            {
                QualityReportId = $"qr_{Guid.NewGuid():N}",
                Mode = "skipped",
                PublicationDecision = PublicationDecisionStatus.Skipped,
                RecommendedDecision = PublicationDecisionStatus.Skipped,
                Diagnostics = [Info("quality_scoring.skipped", "Creative quality scoring disabled by feature flag.")],
                Versions = Versions(request)
            });
        }

        logger.LogInformation("Creative quality scoring started. CorrelationId={CorrelationId}", request.Context.CorrelationId);
        var diagnostics = new List<DiagnosticMessage>(request.Diagnostics) { Info("quality_scoring.started", "Creative quality scoring started."), Info("quality_scoring.observation_mode_active", "Observation mode active; report is advisory and does not block publication.") };
        var warnings = new List<string>();
        var critical = new List<string>();
        var recommendations = new List<string>();
        var scores = BuildScores(request, diagnostics, warnings, critical, recommendations);
        foreach (var low in scores.Where(s => s.Score < PassThreshold))
            diagnostics.Add(Warn("quality_scoring.low_category_score", $"{low.Name} scored {low.Score:0.00}.", low.Name));

        var overall = Math.Round(scores.Count == 0 ? 0 : scores.Average(s => s.Score), 3);
        var confidence = Math.Round(0.55 + (Present(request.Cdl) + Present(request.CreativeDirectionContract) + Present(request.PromptPackage)) * 0.15, 3);
        var decision = Decision(overall, critical.Count, warnings.Count);
        diagnostics.Add(Info("quality_scoring.advisory_decision", $"Advisory publication decision: {decision}; blocking flag requested={flags.UseQualityScoringBlocking}."));

        return Task.FromResult(new QualityReport
        {
            QualityReportId = $"qr_{Guid.NewGuid():N}",
            ContractId = request.CreativeDirectionContract?.ContractId ?? string.Empty,
            PromptPackageId = request.PromptPackage?.PromptPackageId ?? string.Empty,
            ProviderName = request.PromptPackage?.ProviderName ?? request.CreativeDirectionContract?.ProviderHints.PreferredProvider ?? ImageProviderType.Unknown,
            ProviderProfileVersion = ProviderVersion(request),
            Mode = "observation",
            OverallScore = overall,
            Confidence = confidence,
            PublicationDecision = decision,
            RecommendedDecision = decision,
            CategoryScores = scores,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            CriticalIssues = critical.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Recommendations = recommendations.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ProviderInformation = ProviderInformation(request),
            Versions = Versions(request),
            Diagnostics = diagnostics,
            ExtensionFields = new Dictionary<string, object?>
            {
                ["observationOnly"] = true,
                ["blockingRequested"] = flags.UseQualityScoringBlocking,
                ["activePipelineBlockingApplied"] = false,
                ["imagePixelsInspected"] = false,
                ["imageGenerationCallMade"] = false,
                ["azureCallMade"] = false
            }
        });
    }

    private static List<CreativeQualityCategoryScore> BuildScores(CreativeQualityScoringRequest r, List<DiagnosticMessage> d, List<string> w, List<string> c, List<string> rec)
    {
        if (r.Cdl is null) { d.Add(Warn("quality_scoring.missing_cdl", "CDL is missing.")); c.Add("CDL is missing."); rec.Add("Generate CDL before visual publication readiness evaluation."); }
        if (r.CreativeDirectionContract is null) { d.Add(Warn("quality_scoring.missing_contract", "CreativeDirectionContract is missing.")); c.Add("CreativeDirectionContract is missing."); }
        if (r.Context.FeatureFlags.UsePromptComposerV2 && r.PromptPackage is null) { d.Add(Warn("quality_scoring.missing_prompt_package", "Prompt package is missing while PromptComposerV2 is enabled.")); w.Add("Prompt package is missing while PromptComposerV2 is enabled."); }

        bool cdl = r.Cdl?.Directives.Count > 0 == true;
        bool hero = !string.IsNullOrWhiteSpace(r.CreativeDirectionContract?.VisualIntent.PrimarySubject) || HasDirective(r.Cdl, "heroSubject");
        bool family = r.CreativeDirectionContract?.EventFamily != EventFamily.Unknown || r.Context.EventFamily != EventFamily.Unknown;
        bool aspect = r.CreativeDirectionContract?.AspectRatio != AspectRatio.Unknown || r.Context.AspectRatio != AspectRatio.Unknown;
        bool brand = r.CreativeDirectionContract is not null && !string.IsNullOrWhiteSpace(r.CreativeDirectionContract.BrandRules.BrandName) && (!string.IsNullOrWhiteSpace(r.CreativeDirectionContract.BrandRules.VisualTone) || r.CreativeDirectionContract.BrandRules.StylePrinciples.Count > 0);
        bool rendering = r.CreativeDirectionContract?.PlanetRenderingRules.Subjects.Count > 0 == true || HasDirective(r.Cdl, "astronomicalRendering");
        bool negative = Any(r.CreativeDirectionContract?.NegativeConstraints.Scientific) || Any(r.CreativeDirectionContract?.NegativeConstraints.Brand) || Any(r.CreativeDirectionContract?.NegativeConstraints.Typography) || HasDirective(r.Cdl, "negativeConstraints") || !string.IsNullOrWhiteSpace(r.PromptPackage?.NegativePrompt);
        bool prompt = r.PromptPackage is not null && !string.IsNullOrWhiteSpace(r.PromptPackage.PositivePrompt);
        bool targets = r.CreativeDirectionContract?.QualityTargets.Dimensions.Count > 0 == true || HasDirective(r.Cdl, "qualityTargets");
        bool provider = !string.IsNullOrWhiteSpace(r.PromptPackage?.ProviderProfileVersion) || !string.IsNullOrWhiteSpace(r.CreativeDirectionContract?.ProviderHints.ProviderProfileVersion);
        if (!brand) { d.Add(Warn("quality_scoring.missing_brand_rules", "Brand rules are missing or incomplete.")); w.Add("Brand rules are missing or incomplete."); }
        if (!rendering) { d.Add(Warn("quality_scoring.missing_rendering_rules", "Rendering rules are missing or incomplete.")); w.Add("Rendering rules are missing or incomplete."); }
        if (!hero) { w.Add("Hero subject is missing."); rec.Add("Add a clear hero subject to the CDL and contract visual intent."); }

        return
        [
            Score(CreativeQualityCategory.AstronomicalAccuracy, cdl, family, rendering, negative),
            Score(CreativeQualityCategory.PlanetRenderingAccuracy, rendering, negative, hero),
            Score(CreativeQualityCategory.BrandConsistency, brand, negative, cdl),
            Score(CreativeQualityCategory.Composition, aspect, hero, HasDirective(r.Cdl, "composition")),
            Score(CreativeQualityCategory.VisualHierarchy, hero, HasDirective(r.Cdl, "visualHierarchy"), !string.IsNullOrWhiteSpace(r.CreativeDirectionContract?.VisualIntent.Composition)),
            Score(CreativeQualityCategory.Typography, Any(r.CreativeDirectionContract?.TypographyRules.AllowedTextElements), Any(r.CreativeDirectionContract?.TypographyRules.ForbiddenText), negative),
            Score(CreativeQualityCategory.ObservationCard, r.CreativeDirectionContract?.ObservationCardRules.AllowedFields.Count > 0 == true, !string.IsNullOrWhiteSpace(r.CreativeDirectionContract?.ObservationCardRules.Placement), HasDirective(r.Cdl, "observationCard")),
            Score(CreativeQualityCategory.LabelQuality, r.CreativeDirectionContract?.TypographyRules.LabelRules.Count > 0 == true, HasDirective(r.Cdl, "labels"), negative),
            Score(CreativeQualityCategory.PlatformOptimization, aspect, r.CreativeDirectionContract?.TargetPlatform != Platform.Unknown || r.Context.Platform != Platform.Unknown, provider),
            Score(CreativeQualityCategory.Readability, Any(r.CreativeDirectionContract?.TypographyRules.AllowedTextElements), brand, prompt || !r.Context.FeatureFlags.UsePromptComposerV2),
            Score(CreativeQualityCategory.ScientificCredibility, family, rendering, negative, targets),
            Score(CreativeQualityCategory.DocumentaryAesthetic, brand, HasDirective(r.Cdl, "atmosphere"), HasDirective(r.Cdl, "lighting")),
            Score(CreativeQualityCategory.OverallProductionQuality, cdl, hero, family, aspect, brand, rendering, negative, prompt || !r.Context.FeatureFlags.UsePromptComposerV2, targets, provider)
        ];
    }

    private static CreativeQualityCategoryScore Score(CreativeQualityCategory name, params bool[] checks)
    {
        var passed = checks.Count(x => x);
        var score = checks.Length == 0 ? 0 : Math.Round(passed / (double)checks.Length, 3);
        return new CreativeQualityCategoryScore { Name = name, Score = score, Passed = score >= PassThreshold, Findings = [$"{passed}/{checks.Length} readiness checks passed."] };
    }
    private static PublicationDecisionStatus Decision(double score, int critical, int warnings) => critical > 1 || score < .50 ? PublicationDecisionStatus.Rejected : critical > 0 || score < .65 ? PublicationDecisionStatus.NeedsManualReview : score < .75 ? PublicationDecisionStatus.NeedsRegeneration : warnings > 0 || score < .90 ? PublicationDecisionStatus.ApprovedWithWarning : PublicationDecisionStatus.Approved;
    private static bool HasDirective(CDL? cdl, string name) => cdl?.Directives.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(d.Value)) == true;
    private static bool Any(IEnumerable<string>? values) => values?.Any(v => !string.IsNullOrWhiteSpace(v)) == true;
    private static int Present(object? value) => value is null ? 0 : 1;
    private static string ProviderVersion(CreativeQualityScoringRequest r) => r.PromptPackage?.ProviderProfileVersion ?? r.CreativeDirectionContract?.ProviderHints.ProviderProfileVersion ?? VisualIntelligenceContractVersions.ProviderProfileVersion;
    private static Dictionary<string, object?> ProviderInformation(CreativeQualityScoringRequest r) => new() { ["providerName"] = r.PromptPackage?.ProviderName.ToString() ?? r.CreativeDirectionContract?.ProviderHints.PreferredProvider.ToString() ?? ImageProviderType.Unknown.ToString(), ["providerProfileVersion"] = ProviderVersion(r), ["metadata"] = r.PromptPackage?.ProviderParameters ?? new Dictionary<string, object?>() };
    private static Dictionary<string, string> Versions(CreativeQualityScoringRequest r) => new() { ["qualityReportVersion"] = VisualIntelligenceContractVersions.QualityReportVersion, ["contractVersion"] = r.CreativeDirectionContract?.ContractVersion ?? VisualIntelligenceContractVersions.ContractVersion, ["cdlVersion"] = r.Cdl?.CdlVersion ?? r.CreativeDirectionContract?.Cdl.CdlVersion ?? VisualIntelligenceContractVersions.CdlVersion, ["promptComposerVersion"] = r.PromptPackage?.PromptComposerVersion ?? VisualIntelligenceContractVersions.PromptComposerVersion, ["providerProfileVersion"] = ProviderVersion(r) };
    private static DiagnosticMessage Info(string code, string message) => new() { Severity = DiagnosticSeverity.Info, Code = code, Message = message, Source = nameof(CreativeQualityScoringEngine) };
    private static DiagnosticMessage Warn(string code, string message, CreativeQualityCategory category = CreativeQualityCategory.Unknown) => new() { Severity = DiagnosticSeverity.Warning, Code = code, Message = message, Source = nameof(CreativeQualityScoringEngine), Metadata = category == CreativeQualityCategory.Unknown ? [] : new Dictionary<string, object?> { ["creativeQualityCategory"] = category.ToString() } };
}
