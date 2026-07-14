using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;

namespace Astronomy.MediaFactory.Tests;

public sealed class LegacyFactCompatibilityTests
{
    [Fact]
    public void Missing_V1_Fact_Does_Not_Create_Legacy_Filler()
    {
        var fact = new ResolvedSemanticFactV1(new SemanticCapabilityId("EventWindow"), SemanticResolutionStatusV1.MissingRequiredValue, true, null, null, null, null, null, null, null, default, 0, [], [], [], [], "None", [], [], "Missing", "Missing");

        Assert.Null(LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "ObservationTiming", "beat-1", "Required", "en"));
    }

    [Fact]
    public void Missing_V1_Fact_With_Canonical_Value_Still_Does_Not_Create_Legacy_Filler()
    {
        var fact = new ResolvedSemanticFactV1(new SemanticCapabilityId("EventWindow"), SemanticResolutionStatusV1.MissingRequiredValue, true, new SemanticSourceValueV1("fallback", "String"), "fallback", "fallback", null, null, null, null, default, 0, [], [], [], [], "None", [], [], "Missing", "Missing");

        Assert.Null(LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "ObservationTiming", "beat-1", "Required", "en"));
    }
}
