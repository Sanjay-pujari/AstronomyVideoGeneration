using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionConvergencePolicyTests
{
    [Theory] [InlineData(0, 1)] [InlineData(-1, 1)] [InlineData(1, 0)] [InlineData(1, -1)]
    public void Rejects_invalid_limits(int cycles, int progress) => Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentaryNarrativeRevisionConvergencePolicy(cycles, true, progress, true, true, "1.0"));
    [Fact] public void Rejects_unsupported_success_and_schema() { Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionConvergencePolicy(1, true, 1, false, true, "1.0")); Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionConvergencePolicy(1, true, 1, true, false, "1.0")); Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionConvergencePolicy(1, true, 1, true, true, "2.0")); }
    [Fact] public void Preserves_and_round_trips_exactly() { var p = OrionDocumentaryNarrativeRevisionConvergenceFixture.DefaultPolicy(); Assert.Equal((3,true,2,true,true,"1.0"),(p.MaximumCycleCount,p.StopOnRegression,p.MaximumConsecutiveNoProgressCycles,p.RequireCleanValidationForSuccess,p.RequireNoUnresolvedRevisionItemsForSuccess,p.PolicySchemaVersion)); RoundTrip(p); }
    internal static void RoundTrip<T>(T value) { var o=OrionDocumentaryNarrativeRevisionConvergenceFixture.JsonOptions(); var j=JsonSerializer.Serialize(value,o); Assert.Equal(j,JsonSerializer.Serialize(JsonSerializer.Deserialize<T>(j,o),o)); }
}
