using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

public sealed record AstronomyTypedPayloadDescriptor
{
    private const int MaxDiscriminatorLength = 128;

    public AstronomyTypedPayloadDescriptor(string discriminator, Type payloadType, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family)
    {
        Discriminator = ValidateDiscriminator(discriminator);
        PayloadType = ValidatePayloadType(payloadType);
        Domain = TypedKnowledgeEnumGuard.RequireDefined(domain, nameof(domain));
        Family = TypedKnowledgeEnumGuard.RequireDefined(family, nameof(family));
    }

    public string Discriminator { get; }
    public Type PayloadType { get; }
    public AstronomyKnowledgeDomain Domain { get; }
    public AstronomyKnowledgePayloadFamily Family { get; }

    private static string ValidateDiscriminator(string discriminator)
    {
        if (string.IsNullOrWhiteSpace(discriminator) || discriminator.Length > MaxDiscriminatorLength || discriminator.Any(char.IsWhiteSpace) || discriminator.Any(char.IsControl) || !discriminator.Contains(".v", StringComparison.Ordinal))
            throw new ArgumentException("Typed payload discriminator must be a stable versioned token with no whitespace or control characters.", nameof(discriminator));
        return discriminator;
    }

    private static Type ValidatePayloadType(Type payloadType)
    {
        ArgumentNullException.ThrowIfNull(payloadType);
        if (!typeof(ITypedAstronomyKnowledgePayload).IsAssignableFrom(payloadType) || payloadType.IsAbstract || payloadType.IsInterface)
            throw new ArgumentException("Typed payload descriptor payload type must be a concrete ITypedAstronomyKnowledgePayload type.", nameof(payloadType));
        return payloadType;
    }
}
