namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
public sealed record AstronomyRangePhysicalPropertyValue : AstronomyPhysicalPropertyValue
{
    public AstronomyRangePhysicalPropertyValue(AstronomyMeasurementRange range) { Range = range ?? throw new ArgumentNullException(nameof(range)); }
    public override AstronomyPhysicalPropertyValueKind Kind => AstronomyPhysicalPropertyValueKind.MeasurementRange;
    public AstronomyMeasurementRange Range { get; }
}
