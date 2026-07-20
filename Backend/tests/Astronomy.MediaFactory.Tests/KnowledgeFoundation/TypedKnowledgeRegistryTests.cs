using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedKnowledgeRegistryTests
{
    [Fact]
    public void BuiltInDescriptors_FreezeAllTypedPayloadDiscriminators()
    {
        var matrix = AstronomyBuiltInTypedPayloadDescriptors.BuiltIn.Select(d => (d.Discriminator, d.PayloadType.Name, d.Domain, d.Family)).ToArray();

        matrix.Should().Equal([
            ("typed.classification.entity.v1", nameof(AstronomyEntityClassificationPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification),
            ("typed.event.astronomical.v1", "AstronomyEventPayload", AstronomyKnowledgeDomain.Event, AstronomyKnowledgePayloadFamily.AstronomicalEvent),
            ("typed.observational.conditions.v1", "AstronomyObservationConditionsPayload", AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.ObservationCondition),
            ("typed.observational.visibility-windows.v1", "AstronomyVisibilityWindowsPayload", AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.VisibilityWindow),
            ("typed.orbital.keplerian-elements.v1", "AstronomyKeplerianElementsPayload", AstronomyKnowledgeDomain.Orbital, AstronomyKnowledgePayloadFamily.OrbitalParameter),
            ("typed.orbital.parameters.v1", "AstronomyOrbitalParametersPayload", AstronomyKnowledgeDomain.Orbital, AstronomyKnowledgePayloadFamily.OrbitalParameter),
            ("typed.physical.properties.v1", "AstronomyPhysicalPropertiesPayload", AstronomyKnowledgeDomain.Physical, AstronomyKnowledgePayloadFamily.PhysicalProperty),
            ("typed.positional.spatial-position.v1", "AstronomySpatialPositionPayload", AstronomyKnowledgeDomain.Positional, AstronomyKnowledgePayloadFamily.SpatialPosition),
            ("typed.temporal.pattern.v1", "AstronomyTemporalPatternPayload", AstronomyKnowledgeDomain.Temporal, AstronomyKnowledgePayloadFamily.TemporalCycle)
        ]);
    }

    [Theory]
    [MemberData(nameof(BuiltInDiscriminators))]
    public void Descriptor_accepts_built_in_canonical_discriminators(string discriminator)
    {
        var descriptor = new AstronomyTypedPayloadDescriptor(
            discriminator,
            typeof(AstronomyEntityClassificationPayload),
            AstronomyKnowledgeDomain.Classification,
            AstronomyKnowledgePayloadFamily.EntityClassification);

        descriptor.Discriminator.Should().Be(discriminator);
    }

    [Theory]
    [InlineData("Typed.Event.V1")]
    [InlineData(" typed.event.v1")]
    [InlineData("typed.event.v1 ")]
    [InlineData("typed event.v1")]
    [InlineData("typed.event.\u0001.v1")]
    [InlineData("typed.event")]
    [InlineData("typed.event.v")]
    [InlineData("typed.event.vx")]
    [InlineData("typed.event.v0")]
    [InlineData("typed.event.v-1")]
    [InlineData("typed.event.v1.extra")]
    [InlineData("typed..event.v1")]
    [InlineData(".typed.event.v1")]
    [InlineData("typed.event.v1.")]
    public void Descriptor_rejects_noncanonical_discriminators(string discriminator)
    {
        Assert.Throws<ArgumentException>(() => new AstronomyTypedPayloadDescriptor(
            discriminator,
            typeof(AstronomyEntityClassificationPayload),
            AstronomyKnowledgeDomain.Classification,
            AstronomyKnowledgePayloadFamily.EntityClassification));
    }

    [Fact]
    public void Descriptor_rejects_excessive_length_discriminator()
    {
        Assert.Throws<ArgumentException>(() => new AstronomyTypedPayloadDescriptor(
            $"typed.{new string('a', 128)}.v1",
            typeof(AstronomyEntityClassificationPayload),
            AstronomyKnowledgeDomain.Classification,
            AstronomyKnowledgePayloadFamily.EntityClassification));
    }

    public static IEnumerable<object[]> BuiltInDiscriminators() =>
        AstronomyBuiltInTypedPayloadDescriptors.BuiltIn.Select(descriptor => new object[] { descriptor.Discriminator });

    [Fact]
    public void Registry_IsImmutableDeterministicAndRejectsDuplicates()
    {
        var descriptors = AstronomyBuiltInTypedPayloadDescriptors.BuiltIn.Reverse().ToList();
        var registry = new AstronomyTypedPayloadRegistry(descriptors);
        descriptors.Clear();

        registry.Descriptors.Select(d => d.Discriminator).Should().BeInAscendingOrder(StringComparer.Ordinal);
        registry.Descriptors.Should().HaveCount(9);
        registry.TryGetByDiscriminator("typed.physical.properties.v1", out var physical).Should().BeTrue();
        registry.TryGetByPayloadType(physical.PayloadType, out var byType).Should().BeTrue();
        byType.Should().BeSameAs(physical);
        registry.TryGetByDiscriminator("typed.unknown.v1", out _).Should().BeFalse();

        Assert.Throws<ArgumentException>(() => new AstronomyTypedPayloadRegistry([AstronomyBuiltInTypedPayloadDescriptors.BuiltIn[0], AstronomyBuiltInTypedPayloadDescriptors.BuiltIn[0]]));
        Assert.Throws<ArgumentException>(() => new AstronomyTypedPayloadRegistry([null!]));
        Assert.Throws<ArgumentException>(() => new AstronomyTypedPayloadDescriptor("typed.invalid.v1", typeof(string), AstronomyKnowledgeDomain.Catalog, AstronomyKnowledgePayloadFamily.CatalogReference));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyTypedPayloadDescriptor("typed.invalid.v1", typeof(AstronomyEntityClassificationPayload), (AstronomyKnowledgeDomain)999, AstronomyKnowledgePayloadFamily.EntityClassification));
    }
}
