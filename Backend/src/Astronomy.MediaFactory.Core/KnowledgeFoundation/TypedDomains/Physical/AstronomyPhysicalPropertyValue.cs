namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;

public abstract record AstronomyPhysicalPropertyValue
{
    private protected AstronomyPhysicalPropertyValue()
    {
    }

    public abstract AstronomyPhysicalPropertyValueKind Kind { get; }
}
