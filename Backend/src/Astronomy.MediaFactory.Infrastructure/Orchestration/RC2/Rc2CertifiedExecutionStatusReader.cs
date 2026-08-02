using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public interface IRc2CertifiedExecutionStatusReader
{
    Task<Rc2CertifiedExecutionStatus?> ReadAsync(BatchGenerateFromPlansResponse response, CancellationToken cancellationToken = default);
}

public sealed class Rc2CertifiedExecutionStatusReader(IPhase4CommittedAuthorityEvaluator evaluator)
    : IRc2CertifiedExecutionStatusReader
{
    private const string IntegrationService = "DocumentaryBlueprintPhase4IntegrationService";

    public async Task<Rc2CertifiedExecutionStatus?> ReadAsync(BatchGenerateFromPlansResponse response, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(response.OutputRoot)) return null;
        var executionId = response.SelectedPlanId?.ToString("D") ?? response.PlanId?.ToString("D") ?? string.Empty;
        var eventId = response.ProductionPipelineRequest is ProductionPipelineRequest pipelineRequest &&
            pipelineRequest.AstronomyEventIntelligenceId != Guid.Empty
                ? pipelineRequest.AstronomyEventIntelligenceId.ToString("D")
                : string.Empty;
        var phase4 = response.Steps.OfType<ProductionPhaseResult>().LastOrDefault(x => x.PhaseNo == 4);
        var evaluation = await evaluator.EvaluateAsync(response.OutputRoot, executionId, executionId,
            eventId, response.RequestedLanguage ?? string.Empty, token);
        var authority = evaluation.PublishedAuthority;
        var phases = response.Steps.OfType<ProductionPhaseResult>().Where(x => x.PhaseNo is >= 1 and <= 6)
            .GroupBy(x => x.PhaseNo).Select(x => x.Last()).OrderBy(x => x.PhaseNo)
            .Select(x => new Rc2CertifiedPhaseStatus(x.PhaseNo, x.PhaseName, x.Status.ToString(), x.ReasonCode)).ToArray();
        return new(executionId, phases,
            new(IntegrationService, phase4?.Status.ToString() ?? "NotRun", authority is not null, evaluation.IsValid, false),
            authority?.AggregateId, authority?.DeterministicChecksum,
            authority?.LongVariant.ActualSceneCount ?? 0, authority?.ShortVariant.ActualSceneCount ?? 0,
            authority?.AggregateDurationSummary.LongDurationSeconds ?? 0, authority?.AggregateDurationSummary.ShortDurationSeconds ?? 0,
            evaluation.IsValid ? "Valid" : "Invalid", evaluation.IsValid, phase4?.ReasonCode == "P4PUB_ALREADY_PUBLISHED",
            evaluation.ArtifactPaths, evaluation.IsValid, LegacyAuthorityProduced: false,
            PipelineIntegrationService: IntegrationService,
            DownstreamAuthorityType: "PublishedDocumentaryBlueprintAggregate",
            LegacyCompatibilityArtifactExists: false, LegacyPhase4AuthorityUsed: false,
            CommittedStateReasonCode: evaluation.ReasonCode,
            Phase6Publication: ReadPhase6Publication(response.OutputRoot,
                response.Steps.OfType<ProductionPhaseResult>().LastOrDefault(x => x.PhaseNo == 6)));
    }

    private static Rc2Phase6PublicationStatus? ReadPhase6Publication(string outputRoot, ProductionPhaseResult? phase)
    {
        if (phase is null) return null;
        var validationPath = Path.Combine(outputRoot, "validation", "phase-06-validation.json");
        var paths = new[] { "06-story-frames/story-frames.json", "06-story-frames/story-frame-index.json", "06-story-frames/story-frame-diagnostics.json" };
        if (!File.Exists(validationPath) || paths.Any(path => !File.Exists(Path.Combine(outputRoot, path))))
            return new("StoryFrameIntegrationService", phase.Status.ToString(), false, false, false,
                null, null, null, [], 0, 0, 0, false, false, paths, "P6_COMMITTED_STATE_INVALID");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(validationPath));
            var root = document.RootElement;
            string? String(string name) => root.TryGetProperty(name, out var value) ? value.GetString() : null;
            int Int(string name) => root.TryGetProperty(name, out var value) ? value.GetInt32() : 0;
            bool Bool(string name) => root.TryGetProperty(name, out var value) && value.GetBoolean();
            var variants = root.TryGetProperty("requestedVariants", out var requested) && requested.ValueKind == JsonValueKind.Array
                ? requested.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
            return new("StoryFrameIntegrationService", phase.Status.ToString(), true,
                Bool("committedStateValidationPassed"), false, String("authorityId"), String("authorityChecksum"),
                String("indexChecksum"), variants, Int("longStoryFrameCount"), Int("shortStoryFrameCount"),
                Int("totalFrameCount"), Bool("publicationCommitted"), phase.ReasonCode == "P6REUSE_VALID", paths, "P6REUSE_VALID");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return new("StoryFrameIntegrationService", phase.Status.ToString(), true, false, false,
                null, null, null, [], 0, 0, 0, false, false, paths, "P6_COMMITTED_STATE_INVALID");
        }
    }
}
