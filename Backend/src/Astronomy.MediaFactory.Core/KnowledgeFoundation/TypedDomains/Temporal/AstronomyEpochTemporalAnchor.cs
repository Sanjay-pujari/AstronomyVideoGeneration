using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyEpochTemporalAnchor : AstronomyTemporalAnchor
{
    public AstronomyEpochTemporalAnchor(AstronomyEpochReference epoch) { Epoch = epoch ?? throw new ArgumentNullException(nameof(epoch)); }
    public override AstronomyTemporalAnchorKind Kind => AstronomyTemporalAnchorKind.Epoch;
    public AstronomyEpochReference Epoch { get; }
}
