using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public sealed class AstronomyObservationVisibilityConsistencyValidationRuleTests
{
    [Fact]
    public void Validate_EmptySet_IsDeterministicAndDoesNotMutateInput()
    {
        var set = CrossDomainValidationFixture.EmptySet();
        var rule = CreateRule();
        var first = rule.Validate(set, CrossDomainValidationFixture.Context()).ToArray();
        var second = rule.Validate(set, CrossDomainValidationFixture.Context()).ToArray();
        Assert.Empty(first);
        Assert.Equal(first, second);
        Assert.Empty(set.Payloads);
    }

    private static IAstronomyCrossDomainValidationRule CreateRule() => typeof(AstronomyObservationVisibilityConsistencyValidationRuleTests).Name switch
    {
        nameof(AstronomyEntityConsistencyValidationRuleTests) => new AstronomyEntityConsistencyValidationRule(),
        nameof(AstronomyEpochConsistencyValidationRuleTests) => new AstronomyEpochConsistencyValidationRule(),
        nameof(AstronomyReferenceContextConsistencyValidationRuleTests) => new AstronomyReferenceContextConsistencyValidationRule(),
        nameof(AstronomyMeasurementConsistencyValidationRuleTests) => new AstronomyMeasurementConsistencyValidationRule(),
        nameof(AstronomyClassificationConsistencyValidationRuleTests) => new AstronomyClassificationConsistencyValidationRule(),
        nameof(AstronomyEventParticipantConsistencyValidationRuleTests) => new AstronomyEventParticipantConsistencyValidationRule(),
        nameof(AstronomyEventTemporalConsistencyValidationRuleTests) => new AstronomyEventTemporalConsistencyValidationRule(),
        nameof(AstronomyObservationVisibilityConsistencyValidationRuleTests) => new AstronomyObservationVisibilityConsistencyValidationRule(),
        _ => new AstronomyOrbitalPositionalConsistencyValidationRule()
    };
}
