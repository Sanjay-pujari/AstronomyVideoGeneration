using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

public abstract record AstronomyPositionValue
{
    private protected AstronomyPositionValue()
    {
    }

    public abstract AstronomyPositionRepresentationKind Kind { get; }
}
