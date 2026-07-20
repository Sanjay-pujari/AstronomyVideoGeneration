using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation;

public sealed class AstronomyKnowledgeValidationRuleRegistryTests
{
    [Fact]
    public void Registry_OrdersCopiesLooksUpAndFiltersApplicableDescriptors()
    {
        var descriptors = new List<AstronomyKnowledgeValidationRuleDescriptor> { new("test.always-error", typeof(AlwaysErrorRule), typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification, 20), new("test.always-warning", typeof(AlwaysWarningRule), typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification, 10) };
        var registry = new AstronomyKnowledgeValidationRuleRegistry(descriptors); descriptors.Clear();
        Assert.Equal(new[] { "test.always-warning", "test.always-error" }, registry.GetApplicable(typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification).Select(d => d.RuleId));
        Assert.True(registry.TryGetByRuleId("test.always-warning", out _)); Assert.Equal(2, registry.Descriptors.Count);
    }
    [Fact]
    public void Registry_RejectsInvalidDescriptors()
    {
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeValidationRuleRegistry(null!));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationRuleRegistry(new AstronomyKnowledgeValidationRuleDescriptor?[] { null! }!));
        var descriptor = new AstronomyKnowledgeValidationRuleDescriptor("test.duplicate", typeof(AlwaysWarningRule), typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification);
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationRuleRegistry(new[] { descriptor, descriptor }));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationRuleDescriptor("test.bad", typeof(string), typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationRuleDescriptor("test.bad", typeof(AlwaysWarningRule), typeof(string), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification));
    }
}
