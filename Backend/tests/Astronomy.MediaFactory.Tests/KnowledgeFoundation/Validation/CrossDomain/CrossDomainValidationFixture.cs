using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public static class CrossDomainValidationFixture
{
    public static AstronomyCrossDomainValidationSet EmptySet() => new(Array.Empty<Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.ITypedAstronomyKnowledgePayload>());
    public static AstronomyCrossDomainValidationContext Context(AstronomyKnowledgeValidationSeverity minimumSeverity = AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode mode = AstronomyKnowledgeValidationMode.Standard) => new(new AstronomyKnowledgeValidationRunId("cross-domain-test"), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), mode, minimumSeverity);
}
