using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

internal static class Rc2CertifiedExecutionStatusReader
{
    private const string IntegrationService = "DocumentaryBlueprintPhase4IntegrationService";

    public static Rc2CertifiedExecutionStatus? Read(BatchGenerateFromPlansResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.OutputRoot)) return null;
        var root = response.OutputRoot;
        var authorityPath = Path.Combine(root, "04-blueprint", "documentary-blueprint.json");
        var validationPath = Path.Combine(root, "validation", "phase-04-validation.json");
        if (!File.Exists(validationPath)) validationPath = Path.Combine(root, "phase-04-validation.json");

        DocumentaryBlueprintAggregate? authority = null;
        if (File.Exists(authorityPath))
        {
            try { authority = JsonSerializer.Deserialize<DocumentaryBlueprintAggregate>(File.ReadAllBytes(authorityPath), new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException) { }
        }

        var phase4 = response.Steps.OfType<ProductionPhaseResult>().FirstOrDefault(x => x.PhaseNo == 4);
        var committed = authority is not null && File.Exists(validationPath) && File.Exists(Path.Combine(root, "phase-manifest.json"));
        var checksumValid = authority is not null && DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(authority)
            && DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(authority.LongVariant)
            && DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(authority.ShortVariant);
        var phases = response.Steps.OfType<ProductionPhaseResult>().Where(x => x.PhaseNo is >= 1 and <= 4)
            .GroupBy(x => x.PhaseNo).Select(x => x.Last()).OrderBy(x => x.PhaseNo)
            .Select(x => new Rc2CertifiedPhaseStatus(x.PhaseNo, x.PhaseName, x.Status.ToString(), x.ReasonCode)).ToArray();
        var artifacts = new[]
        {
            "04-blueprint/documentary-blueprint.json", "04-blueprint/documentary-blueprint.long.json",
            "04-blueprint/documentary-blueprint.short.json", "04-blueprint/knowledge-selection.json",
            "04-blueprint/long-scene-index.json", "04-blueprint/short-scene-index.json",
            "04-blueprint/blueprint-build-report.json", "phase-manifest.json", "phase-04-validation.json"
        }.Where(x => File.Exists(Path.Combine(root, x.Replace('/', Path.DirectorySeparatorChar)))).ToArray();
        var legacy = File.Exists(Path.Combine(root, "editorial", "story-graph.json"));

        return new(
            response.SelectedPlanId?.ToString("D") ?? response.PlanId?.ToString("D") ?? authority?.ExecutionId ?? string.Empty,
            phases,
            new(IntegrationService, phase4?.Status.ToString() ?? "NotRun", authority is not null, committed && checksumValid, legacy),
            authority?.AggregateId, authority?.DeterministicChecksum,
            authority?.LongVariant.ActualSceneCount ?? 0, authority?.ShortVariant.ActualSceneCount ?? 0,
            authority?.AggregateDurationSummary.LongDurationSeconds ?? 0, authority?.AggregateDurationSummary.ShortDurationSeconds ?? 0,
            committed && checksumValid ? "Valid" : "Invalid", committed, phase4?.ReasonCode == "P4PUB_ALREADY_PUBLISHED", artifacts);
    }
}
