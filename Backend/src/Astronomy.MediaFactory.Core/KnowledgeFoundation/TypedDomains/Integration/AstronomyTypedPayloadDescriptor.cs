using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

public sealed record AstronomyTypedPayloadDescriptor
{
    private const int MaxDiscriminatorLength = 128;
    private static readonly Regex DiscriminatorPattern = new(
        @"^[a-z0-9]+(?:[.-][a-z0-9]+)*\.v[1-9][0-9]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        ArgumentNullException.ThrowIfNull(discriminator);

        if (discriminator.Length == 0 ||
            discriminator.Length > MaxDiscriminatorLength ||
            !DiscriminatorPattern.IsMatch(discriminator))
        {
            throw new ArgumentException(
                "Typed payload discriminator must be a lowercase, versioned token ending in '.v' followed by a positive integer.",
                nameof(discriminator));
        }

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
