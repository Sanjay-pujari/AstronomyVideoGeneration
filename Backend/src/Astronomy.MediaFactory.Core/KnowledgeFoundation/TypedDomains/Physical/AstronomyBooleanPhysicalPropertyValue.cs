namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;

public sealed record AstronomyBooleanPhysicalPropertyValue(bool Value) : AstronomyPhysicalPropertyValue
{
    public override AstronomyPhysicalPropertyValueKind Kind => AstronomyPhysicalPropertyValueKind.Boolean;
}
