namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

public interface IAstronomyTypedPayloadRegistry
{
    IReadOnlyCollection<AstronomyTypedPayloadDescriptor> Descriptors { get; }
    bool TryGetByDiscriminator(string discriminator, out AstronomyTypedPayloadDescriptor descriptor);
    bool TryGetByPayloadType(Type payloadType, out AstronomyTypedPayloadDescriptor descriptor);
    AstronomyTypedPayloadDescriptor GetRequiredByDiscriminator(string discriminator);
}
