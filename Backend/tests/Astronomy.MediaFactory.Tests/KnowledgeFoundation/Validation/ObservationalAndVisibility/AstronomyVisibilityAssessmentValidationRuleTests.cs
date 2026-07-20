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

public sealed class AstronomyVisibilityAssessmentValidationRuleTests{[Fact] public void Valid_assessment_passes()=>Assert.Empty(ObservationalVisibilityValidationFixture.Validate(ObservationalVisibilityValidationFixture.ValidVisibilityPayload(),s=>s.AddAstronomyVisibilityValidation()).Issues);[Fact] public void Undefined_status_is_rejected_or_constructor_protected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>ObservationalVisibilityValidationFixture.Assessment(status:(AstronomyVisibilityStatus)999));[Fact] public void Undefined_method_is_rejected_or_constructor_protected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>ObservationalVisibilityValidationFixture.Assessment(method:(AstronomyVisibilityMethod)999));[Fact] public void Duplicate_limitations_are_detected_or_constructor_protected()=>Assert.Throws<ArgumentException>(()=>ObservationalVisibilityValidationFixture.Assessment(limitations:[AstronomyVisibilityLimitation.Cloud,AstronomyVisibilityLimitation.Cloud]));[Fact] public void Conflicting_limitations_are_detected_only_when_explicit_policy_exists()=>Assert.Throws<ArgumentException>(()=>ObservationalVisibilityValidationFixture.Assessment(limitations:[AstronomyVisibilityLimitation.None,AstronomyVisibilityLimitation.Cloud]));[Fact] public void No_limitation_combined_with_specific_limitation_is_rejected_when_none_member_exists()=>Conflicting_limitations_are_detected_only_when_explicit_policy_exists();[Fact] public void Blank_assessment_note_is_rejected_or_constructor_protected()=>Assert.Throws<ArgumentException>(()=>ObservationalVisibilityValidationFixture.Assessment(summary:" "));[Fact] public void Limitation_issue_path_contains_window_and_limitation_indexes()=>Duplicate_limitations_are_detected_or_constructor_protected();}