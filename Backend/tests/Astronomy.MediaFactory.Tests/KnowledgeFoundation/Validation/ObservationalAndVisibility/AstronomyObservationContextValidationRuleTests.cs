using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Visibility;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ObservationalAndVisibility;


public sealed class AstronomyObservationContextValidationRuleTests {
[Fact] public void Valid_horizontal_topocentric_context_passes()=>Assert.Empty(new AstronomyObservationContextValidationRule().Validate(ObservationalVisibilityValidationFixture.ValidObservationPayload(), ObservationalVisibilityValidationFixture.Context()));
[Fact] public void Horizontal_context_with_non_topocentric_origin_is_rejected(){var p=new AstronomyObservationConditionsPayload(new("typed.observational.conditions.v1"),ObservationalVisibilityValidationFixture.ObservationContext(origin:AstronomyReferenceOrigin.Geocentric),ObservationalVisibilityValidationFixture.Conditions());var i=Assert.Single(new AstronomyObservationContextValidationRule().Validate(p, ObservationalVisibilityValidationFixture.Context()));ObservationalVisibilityValidationFixture.AssertIssue(i,AstronomyObservationalValidationCodes.ContextCoordinateSystemMismatch,AstronomyKnowledgeValidationSeverity.Error,"$.observationContext.coordinateSystem",AstronomyObservationContextValidationRule.Id,AstronomyKnowledgeDomain.Observational,AstronomyKnowledgePayloadFamily.ObservationCondition);} 
[Fact] public void Valid_non_horizontal_context_passes_when_supported(){var p=new AstronomyObservationConditionsPayload(new("typed.observational.conditions.v1"),ObservationalVisibilityValidationFixture.ObservationContext(AstronomyCoordinateSystem.Equatorial,AstronomyReferenceOrigin.Geocentric),ObservationalVisibilityValidationFixture.Conditions());Assert.Empty(new AstronomyObservationContextValidationRule().Validate(p, ObservationalVisibilityValidationFixture.Context()));}
[Fact] public void Observation_time_must_be_utc_or_is_constructor_protected()=>Assert.Throws<ArgumentException>(()=>ObservationalVisibilityValidationFixture.ObservationContext(observationTimeUtc:new DateTimeOffset(2026,8,1,0,0,0,TimeSpan.FromHours(1))));
[Fact] public void Context_issue_contains_stable_code_path_rule_domain_and_family()=>Horizontal_context_with_non_topocentric_origin_is_rejected();
[Fact] public void Rule_execution_is_deterministic(){var p=new AstronomyObservationConditionsPayload(new("typed.observational.conditions.v1"),ObservationalVisibilityValidationFixture.ObservationContext(origin:AstronomyReferenceOrigin.Geocentric),ObservationalVisibilityValidationFixture.Conditions());Assert.Equal(new AstronomyObservationContextValidationRule().Validate(p, ObservationalVisibilityValidationFixture.Context()),new AstronomyObservationContextValidationRule().Validate(p, ObservationalVisibilityValidationFixture.Context()));}}
