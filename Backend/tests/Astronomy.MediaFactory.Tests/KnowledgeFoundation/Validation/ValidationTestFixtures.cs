using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation;

internal sealed record TestPayload(AstronomyKnowledgeTypeId TypeId) : ITypedAstronomyKnowledgePayload
{
    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Classification;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.EntityClassification;
}

internal sealed record DerivedTestPayload(AstronomyKnowledgeTypeId TypeId) : ITypedAstronomyKnowledgePayload
{
    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Classification;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.EntityClassification;
}

internal sealed class AlwaysWarningRule : AstronomyKnowledgeValidationRule<TestPayload>
{
    public override string RuleId => "test.always-warning";
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Classification;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.EntityClassification;
    public override int Order => 10;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(TestPayload payload, AstronomyKnowledgeValidationContext context) => new[] { Fixtures.Issue(RuleId, AstronomyKnowledgeValidationSeverity.Warning, "warning") };
}

internal sealed class AlwaysErrorRule : AstronomyKnowledgeValidationRule<TestPayload>
{
    public override string RuleId => "test.always-error";
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Classification;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.EntityClassification;
    public override int Order => 20;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(TestPayload payload, AstronomyKnowledgeValidationContext context) => new[] { Fixtures.Issue(RuleId, AstronomyKnowledgeValidationSeverity.Error, "error") };
}

internal static class Fixtures
{
    public static AstronomyKnowledgeValidationContext Context(AstronomyKnowledgeValidationSeverity minimum = AstronomyKnowledgeValidationSeverity.Information) => new(new AstronomyKnowledgeValidationRunId("run-1"), new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero), minimumSeverity: minimum);
    public static TestPayload Payload(string typeId = "typed.test.payload.v1") => new(new AstronomyKnowledgeTypeId(typeId));
    public static AstronomyTypedPayloadRegistry PayloadRegistry() => new(new[] { new AstronomyTypedPayloadDescriptor("typed.test.payload.v1", typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification) });
    public static AstronomyKnowledgeValidationIssue Issue(string ruleId, AstronomyKnowledgeValidationSeverity severity, string message = "message") => new("test.issue", severity, message, "$", ruleId, AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification);
}
