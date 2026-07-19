namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
public sealed record AstronomyPhysicalPropertiesPayload : ITypedAstronomyKnowledgePayload, IEquatable<AstronomyPhysicalPropertiesPayload>
{
    public AstronomyPhysicalPropertiesPayload(AstronomyKnowledgeTypeId typeId, IEnumerable<AstronomyPhysicalProperty> properties)
    {
        if (!typeId.IsValid) throw new ArgumentException("Physical properties payload type ID is required.", nameof(typeId));
        TypeId = typeId;
        Properties = CopyProperties(properties);
    }
    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Physical;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.PhysicalProperty;
    public AstronomyKnowledgeTypeId TypeId { get; }
    public IReadOnlyList<AstronomyPhysicalProperty> Properties { get; }
    public bool Equals(AstronomyPhysicalPropertiesPayload? other) => other is not null && TypeId == other.TypeId && Properties.SequenceEqual(other.Properties);
    public override int GetHashCode() => Properties.Aggregate(TypeId.GetHashCode(), (hash, item) => HashCode.Combine(hash, item));
    private static IReadOnlyList<AstronomyPhysicalProperty> CopyProperties(IEnumerable<AstronomyPhysicalProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var ordered = properties.Select(p => p ?? throw new ArgumentException("Physical properties cannot contain null entries.", nameof(properties)))
            .OrderBy(p => p.Category).ThenBy(p => p.PropertyId.Value, StringComparer.Ordinal).ThenBy(p => p.Qualifier).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("At least one physical property is required.", nameof(properties));
        if (ordered.GroupBy(p => new { p.PropertyId, p.Qualifier }).Any(g => g.Count() > 1)) throw new ArgumentException("Physical properties must be unique by property ID and qualifier.", nameof(properties));
        return Array.AsReadOnly(ordered);
    }
}
