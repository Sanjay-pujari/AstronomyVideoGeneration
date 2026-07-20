using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Extensions;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedKnowledgeIntegrationTests
{
    [Fact]
    public void AddAstronomyTypedKnowledge_RegistersRegistryAndJsonOptionsIdempotently()
    {
        var services = new ServiceCollection();
        services.AddAstronomyTypedKnowledge();
        services.AddAstronomyTypedKnowledge();

        services.Count(d => d.ServiceType == typeof(IAstronomyTypedPayloadRegistry)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(JsonSerializerOptions)).Should().Be(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAstronomyTypedPayloadRegistry>().Descriptors.Should().HaveCount(9);
        var options = provider.GetRequiredService<JsonSerializerOptions>();
        options.Converters.OfType<AstronomyTypedKnowledgePayloadJsonConverter>().Should().ContainSingle();
        options.Converters.OfType<JsonStringEnumConverter>().Should().ContainSingle();
    }

    [Fact]
    public void FoundationRegistration_IncludesTypedKnowledgeIntegration()
    {
        using var provider = new ServiceCollection().AddCgA2AstronomyKnowledgeFoundation().BuildServiceProvider();
        provider.GetRequiredService<IAstronomyTypedPayloadRegistry>().Descriptors.Should().HaveCount(9);
        provider.GetRequiredService<JsonSerializerOptions>().Converters.OfType<AstronomyTypedKnowledgePayloadJsonConverter>().Should().ContainSingle();
    }

    [Fact]
    public void AddAstronomyTypedKnowledge_ResolvedOptionsRoundTripTypedPayloads()
    {
        using var provider = new ServiceCollection().AddAstronomyTypedKnowledge().BuildServiceProvider();
        var options = provider.GetRequiredService<JsonSerializerOptions>();

        RoundTrip(new AstronomyEventPayload(
            new("typed.event.astronomical.v1"),
            new AstronomyEvent(
                new("event.2026.jupiter-venus-conjunction"),
                AstronomyEventKind.Conjunction,
                new AstronomyInstantEventTemporalExtent(DateTimeOffset.Parse("2026-08-01T00:00:00Z")),
                new AstronomyEventReferenceContext(AstronomyEventScope.Global),
                [new AstronomyEventParticipant(new("body.jupiter"), AstronomyEventParticipantRole.Primary)])), options);

        RoundTrip(new AstronomyTemporalPatternPayload(
            new("typed.temporal.pattern.v1"),
            new AstronomyTemporalPattern(
                new("temporal.lunar-cycle"),
                AstronomyTemporalPatternKind.Periodic,
                new AstronomyTemporalPatternReferenceContext(AstronomyTemporalReferenceBasis.Utc),
                new AstronomyRecurrenceDescription(AstronomyRecurrenceKind.None))), options);
    }

    private static void RoundTrip<TPayload>(TPayload original, JsonSerializerOptions options)
        where TPayload : ITypedAstronomyKnowledgePayload
    {
        var json = JsonSerializer.Serialize<ITypedAstronomyKnowledgePayload>(original, options);
        var result = JsonSerializer.Deserialize<ITypedAstronomyKnowledgePayload>(json, options);
        result.Should().BeOfType<TPayload>();
        result.Should().Be(original);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"type\":\"\",\"value\":{}}")]
    [InlineData("{\"type\":\"typed.unknown.v1\",\"value\":{}}")]
    [InlineData("{\"type\":\"typed.event.astronomical.v1\"}")]
    [InlineData("{\"type\":\"typed.event.astronomical.v1\",\"type\":\"typed.event.astronomical.v1\",\"value\":{}}")]
    [InlineData("{\"type\":\"typed.event.astronomical.v1\",\"value\":{},\"value\":{}}")]
    public void TypedPayloadConverter_RejectsMalformedEnvelopeJson(string json)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddAstronomyTypedKnowledgeJson(new AstronomyTypedPayloadRegistry(AstronomyBuiltInTypedPayloadDescriptors.BuiltIn));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ITypedAstronomyKnowledgePayload>(json, options));
    }
}
