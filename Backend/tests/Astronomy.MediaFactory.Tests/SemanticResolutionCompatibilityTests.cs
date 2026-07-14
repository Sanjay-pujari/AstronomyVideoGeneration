using System.Collections.Immutable;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticResolutionCompatibilityTests
{
    [Fact]
    public void V1_To_Legacy_Compatibility_Mapping_Is_Deterministic()
    {
        var fact = new ResolvedSemanticFactV1(new SemanticCapabilityId("EventIdentity"), SemanticResolutionStatusV1.Resolved, true, new SemanticSourceValueV1("Solar eclipse", "String"), "Solar eclipse", "Solar eclipse", "candidate-1", "adapter-1", "source-1", SemanticEvidenceCategoryV1.VerifiedEventData, SemanticEvidenceStrengthV1.Strong, .95m, [new("source-1", "model", "path", true)], [], [], [], "FirstApprovedByPriority", [], [], "Resolved", "Resolved");

        var first = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "EventIdentity", "beat-1", "Required", "en");
        var second = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "EventIdentity", "beat-1", "Required", "en");

        Assert.Equal(first, second);
        Assert.Equal("source-1", first!.SourceArtifact);
        Assert.Equal("Solar eclipse", first.CanonicalValue);
    }
}
