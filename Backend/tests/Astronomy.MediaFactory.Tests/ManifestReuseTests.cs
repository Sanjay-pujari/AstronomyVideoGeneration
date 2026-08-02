using System.Reflection;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ManifestReuseTests
{
    [Fact]
    public void ManifestReuse_ValidPhase6ReusePreservesCommittedPhase6History()
    {
        var merged = Merge(CommittedHistory(), Reuse(6, "P6REUSE_VALID"));
        var phase6 = Assert.Single(merged.Where(x => x!["phaseNo"]!.GetValue<int>() == 6))!.AsObject();
        Assert.Equal("Succeeded", phase6["status"]!.GetValue<string>());
        Assert.Equal("P6AUTH_COMMITTED", phase6["reasonCode"]!.GetValue<string>());
    }

    [Fact]
    public void ManifestReuse_DoesNotReplaceCommittedHistoryWithP6REUSE_VALID()
        => Assert.DoesNotContain(Merge(CommittedHistory(), Reuse(6, "P6REUSE_VALID")),
            x => x!["reasonCode"]!.GetValue<string>() == "P6REUSE_VALID");

    [Fact]
    public void ManifestReuse_PreservesPhase1ToPhase5History()
    {
        var merged = Merge(CommittedHistory(), Reuse(6, "P6REUSE_VALID"));
        Assert.Equal(Enumerable.Range(1, 6), merged.Select(x => x!["phaseNo"]!.GetValue<int>()));
    }

    [Fact]
    public void ManifestReuse_StableHistoryContainsExactlyOnePhase6Entry()
        => Assert.Single(Merge(CommittedHistory(), Reuse(6, "P6REUSE_VALID"))
            .Where(x => x!["phaseNo"]!.GetValue<int>() == 6));

    [Fact]
    public void ManifestReuse_StableHistoryIsOrderedByPhaseNumber()
    {
        var reversed = new JsonArray(CommittedHistory().Reverse().Select(x => x!.DeepClone()).ToArray());
        Assert.Equal(Enumerable.Range(1, 6), Merge(reversed, Reuse(6, "P6REUSE_VALID"))
            .Select(x => x!["phaseNo"]!.GetValue<int>()));
    }

    [Fact]
    public void ManifestFailedRun_DoesNotReplaceCommittedPhase6History()
    {
        var failed = Result(6, ProductionPhaseStatus.Failed, "P6PUB_FAILED");
        Assert.Equal("P6AUTH_COMMITTED", Merge(CommittedHistory(), failed)[5]!["reasonCode"]!.GetValue<string>());
    }

    [Fact]
    public void ManifestForcedRun_DoesNotDuplicatePhase6History()
    {
        var committed = Result(6, ProductionPhaseStatus.Succeeded, "P6AUTH_COMMITTED");
        var phase6 = Assert.Single(Merge(CommittedHistory(), committed).Where(x => x!["phaseNo"]!.GetValue<int>() == 6))!;
        Assert.True(phase6["publicationCommitted"]!.GetValue<bool>());
        Assert.True(phase6["committedStateValidationPassed"]!.GetValue<bool>());
    }

    private static JsonArray CommittedHistory() => new(Enumerable.Range(1, 6).Select(phase =>
        (JsonNode)new JsonObject { ["phaseNo"] = phase, ["phaseName"] = phase == 6 ? "Story Frames Authority" : $"Phase {phase}",
            ["status"] = "Succeeded", ["reasonCode"] = phase == 6 ? "P6AUTH_COMMITTED" : $"P{phase}COMMITTED" }).ToArray());

    private static ProductionPhaseResult Reuse(int phase, string code) => Result(phase, ProductionPhaseStatus.Skipped, code);

    private static ProductionPhaseResult Result(int phase, ProductionPhaseStatus status, string code)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProductionPhaseResult(phase, phase == 6 ? "Story Frames Authority" : $"Phase {phase}", status,
            now, now, 0, [], [], $"validation/phase-{phase:00}-validation.json", [], [], false, code) { ReasonCode = code };
    }

    private static JsonArray Merge(JsonArray existing, params ProductionPhaseResult[] current)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("MergeCommittedPhaseHistory",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (JsonArray)method.Invoke(null, [existing, current])!;
    }
}
