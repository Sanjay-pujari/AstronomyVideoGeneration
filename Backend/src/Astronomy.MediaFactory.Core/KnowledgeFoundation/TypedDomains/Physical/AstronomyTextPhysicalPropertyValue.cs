using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;

public sealed record AstronomyTextPhysicalPropertyValue : AstronomyPhysicalPropertyValue
{
    private const int MaxLength = 512;

    public AstronomyTextPhysicalPropertyValue(string value)
    {
        Value = TypedKnowledgeTextGuards.RequireText(
            value,
            MaxLength,
            nameof(value),
            "Physical property text value");
    }

    public override AstronomyPhysicalPropertyValueKind Kind => AstronomyPhysicalPropertyValueKind.Text;

    public string Value { get; }
}
