namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

public sealed class AstronomyTypedPayloadRegistry : IAstronomyTypedPayloadRegistry
{
    private readonly IReadOnlyDictionary<string, AstronomyTypedPayloadDescriptor> byDiscriminator;
    private readonly IReadOnlyDictionary<Type, AstronomyTypedPayloadDescriptor> byType;

    public AstronomyTypedPayloadRegistry(IEnumerable<AstronomyTypedPayloadDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var ordered = descriptors.Select(d => d ?? throw new ArgumentException("Typed payload descriptors cannot contain null entries.", nameof(descriptors)))
            .OrderBy(d => d.Discriminator, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0) throw new ArgumentException("At least one typed payload descriptor is required.", nameof(descriptors));
        var duplicateDiscriminator = ordered.GroupBy(d => d.Discriminator, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicateDiscriminator is not null) throw new ArgumentException($"Duplicate typed payload discriminator '{duplicateDiscriminator.Key}'.", nameof(descriptors));
        var duplicateType = ordered.GroupBy(d => d.PayloadType).FirstOrDefault(g => g.Count() > 1);
        if (duplicateType is not null) throw new ArgumentException($"Duplicate typed payload type '{duplicateType.Key.Name}'.", nameof(descriptors));
        Descriptors = Array.AsReadOnly(ordered);
        byDiscriminator = ordered.ToDictionary(d => d.Discriminator, d => d, StringComparer.Ordinal);
        byType = ordered.ToDictionary(d => d.PayloadType, d => d);
    }

    public IReadOnlyCollection<AstronomyTypedPayloadDescriptor> Descriptors { get; }
    public bool TryGetByDiscriminator(string discriminator, out AstronomyTypedPayloadDescriptor descriptor) => byDiscriminator.TryGetValue(discriminator ?? string.Empty, out descriptor!);
    public bool TryGetByPayloadType(Type payloadType, out AstronomyTypedPayloadDescriptor descriptor) => payloadType is not null && byType.TryGetValue(payloadType, out descriptor!);
    public AstronomyTypedPayloadDescriptor GetRequiredByDiscriminator(string discriminator) => TryGetByDiscriminator(discriminator, out var descriptor) ? descriptor : throw new KeyNotFoundException($"Unknown typed astronomy knowledge payload discriminator '{discriminator}'.");
}
