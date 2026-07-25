using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryNarrativeRevisionConvergenceMetadataTests
{
    [Fact] public void Rejects_invalid_values() { Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionConvergenceMetadata(default,"x","1.0","c")); Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionConvergenceMetadata(DateTimeOffset.Parse("2026-01-01Z")," ","1.0","c")); Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionConvergenceMetadata(DateTimeOffset.Parse("2026-01-01Z"),"x","2.0","c")); Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionConvergenceMetadata(DateTimeOffset.Parse("2026-01-01Z"),"x","1.0"," ")); }
    [Fact] public void Preserves_offset_precision_whitespace_and_round_trips() { var m=OrionDocumentaryNarrativeRevisionConvergenceFixture.Metadata(); Assert.Equal(" convergence coordinator ",m.CreatedBy); Assert.Equal(TimeSpan.FromHours(5.5),m.CreatedUtc.Offset); Assert.Equal(1234567,m.CreatedUtc.Ticks%TimeSpan.TicksPerSecond); DocumentaryNarrativeRevisionConvergencePolicyTests.RoundTrip(m); }
}
