namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public abstract record AstronomyTemporalAnchor
{
    private protected AstronomyTemporalAnchor() { }
    public abstract AstronomyTemporalAnchorKind Kind { get; }
}
