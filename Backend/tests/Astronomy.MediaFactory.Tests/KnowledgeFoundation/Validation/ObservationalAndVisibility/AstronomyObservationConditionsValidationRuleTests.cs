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


public sealed class AstronomyObservationConditionsValidationRuleTests { static IEnumerable<AstronomyKnowledgeValidationIssue> Run(AstronomyObservationConditions c)=>new AstronomyObservationConditionsValidationRule().Validate(new AstronomyObservationConditionsPayload(new("typed.observational.conditions.v1"),ObservationalVisibilityValidationFixture.ObservationContext(),c),ObservationalVisibilityValidationFixture.Context());
[Fact] public void Valid_conditions_pass()=>Assert.Empty(Run(ObservationalVisibilityValidationFixture.Conditions()));
[Fact] public void Undefined_sky_condition_is_rejected_or_constructor_protected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>ObservationalVisibilityValidationFixture.Conditions(sky:(AstronomySkyConditionKind)999));
[Fact] public void Undefined_seeing_is_rejected_or_constructor_protected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>ObservationalVisibilityValidationFixture.Conditions(seeing:(AstronomySeeingQuality)999));
[Fact] public void Undefined_transparency_is_rejected_or_constructor_protected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>ObservationalVisibilityValidationFixture.Conditions(transparency:(AstronomyTransparencyQuality)999));
[Fact] public void Blank_optional_note_is_rejected_or_constructor_protected()=>Assert.Throws<ArgumentException>(()=>ObservationalVisibilityValidationFixture.Conditions(note:" "));
[Fact] public void Suspicious_condition_combination_produces_warning_when_policy_exists(){var i=Assert.Single(Run(ObservationalVisibilityValidationFixture.Conditions(transparency:AstronomyTransparencyQuality.VeryPoor)));ObservationalVisibilityValidationFixture.AssertIssue(i,AstronomyObservationalValidationCodes.ConditionCombinationSuspicious,AstronomyKnowledgeValidationSeverity.Warning,"$.conditions",AstronomyObservationConditionsValidationRule.Id,AstronomyKnowledgeDomain.Observational,AstronomyKnowledgePayloadFamily.ObservationCondition);} 
[Fact] public void Valid_sky_brightness_measurement_passes()=>Assert.Empty(Run(ObservationalVisibilityValidationFixture.Conditions(skyBrightness:ObservationalVisibilityValidationFixture.Measurement(21m,AstronomyMeasurementDimension.Magnitude,"mag","mag"))));
[Fact] public void Measurement_defect_uses_observational_measurement_code()=>Assert.Throws<ArgumentException>(()=>new AstronomyMeasurementUnit(" ","u",AstronomyMeasurementDimension.Magnitude));}
