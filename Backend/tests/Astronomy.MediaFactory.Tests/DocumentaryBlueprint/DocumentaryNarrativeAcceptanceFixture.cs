using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeAcceptanceFixture
{
    internal const string Correlation = "orion-documentary-correlation-001";
    internal static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);
    internal static DocumentaryNarrativeAcceptancePolicy StrictPolicy() => new(true, true, true, true, false, false, false, false, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy ManualHoldPolicy() => new(true, true, true, false, true, true, true, true, "1.0");
    internal static DocumentaryNarrativeAcceptanceMetadata AcceptanceMetadata() => new(DateTimeOffset.Parse("2026-02-03T04:05:06.1234567+05:30"), " acceptance editor ", "1.0", OrionDocumentaryNarrativeRevisionCycleFixture.Correlation);
    internal static DocumentaryNarrativeReleaseCandidateMetadata ReleaseMetadata() => new(DateTimeOffset.Parse("2026-02-04T05:06:07.7654321-04:00"), " release editor ", "1.0", OrionDocumentaryNarrativeRevisionCycleFixture.Correlation);
    internal static DocumentaryNarrativeRevisionConvergenceState InitiallyCleanConvergenceState() => OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyCleanState();
    internal static DocumentaryNarrativeRevisionConvergenceState SuccessfulOneCycleConvergenceState() => OrionDocumentaryNarrativeRevisionConvergenceFixture.OneCycleSuccessfulState();
    internal static DocumentaryNarrativeAcceptanceRequest AcceptedInitiallyCleanRequest() => new(InitiallyCleanConvergenceState(), StrictPolicy(), AcceptanceMetadata());
    internal static DocumentaryNarrativeAcceptanceRequest AcceptedOneCycleRequest() => new(SuccessfulOneCycleConvergenceState(), StrictPolicy(), AcceptanceMetadata());
    internal static DocumentaryNarrativeAcceptanceDecision AcceptedDecision(DocumentaryNarrativeRevisionConvergenceState? state = null)
    {
        state ??= InitiallyCleanConvergenceState();
        return new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(new(state, StrictPolicy(), AcceptanceMetadata()));
    }
    internal static DocumentaryNarrativeReleaseCandidate InitiallyCleanReleaseCandidate()
    {
        var state = InitiallyCleanConvergenceState();
        return new DocumentaryNarrativeReleaseCandidateBuilder().Build(state, AcceptedDecision(state), ReleaseMetadata());
    }
    internal static DocumentaryNarrativeReleaseCandidate OneCycleReleaseCandidate()
    {
        var state = SuccessfulOneCycleConvergenceState();
        return new DocumentaryNarrativeReleaseCandidateBuilder().Build(state, AcceptedDecision(state), ReleaseMetadata());
    }
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions());
}
