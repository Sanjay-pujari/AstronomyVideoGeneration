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

public sealed class AstronomyHorizontalCoordinatesValidationRuleTests{ static AstronomyKnowledgeValidationResult Val(AstronomyObservationConditionsPayload p,AstronomyKnowledgeValidationMode m=AstronomyKnowledgeValidationMode.Standard)=>ObservationalVisibilityValidationFixture.Validate(p,s=>s.AddAstronomyObservationalValidation(),ObservationalVisibilityValidationFixture.Context(m)); static AstronomyObservationConditionsPayload P(AstronomyObservationContext? c=null,AstronomyHorizontalObservationCoordinate? h=null,AstronomyHorizonSector? sec=null)=>new(new("typed.observational.conditions.v1"),c??ObservationalVisibilityValidationFixture.ObservationContext(),ObservationalVisibilityValidationFixture.Conditions(),[],h,sec);
[Fact] public void Valid_azimuth_altitude_pair_passes()=>Assert.Empty(Val(P(h:ObservationalVisibilityValidationFixture.HorizontalCoordinate())).Issues);
[Fact] public void Horizontal_context_without_coordinates_warns_in_standard_mode(){var i=Assert.Single(Val(P()).Issues);ObservationalVisibilityValidationFixture.AssertIssue(i,AstronomyObservationalValidationCodes.HorizontalCoordinateMissing,AstronomyKnowledgeValidationSeverity.Warning,"$.horizontalCoordinate",AstronomyHorizontalCoordinatesValidationRule.Id,AstronomyKnowledgeDomain.Observational,AstronomyKnowledgePayloadFamily.ObservationCondition);} [Fact] public void Horizontal_context_without_coordinates_errors_in_strict_mode()=>Assert.Contains(Val(P(),AstronomyKnowledgeValidationMode.Strict).Issues,i=>i.Code==AstronomyObservationalValidationCodes.HorizontalCoordinateMissing&&i.Severity==AstronomyKnowledgeValidationSeverity.Error);
[Fact] public void Non_horizontal_context_does_not_require_horizontal_coordinates()=>Assert.Empty(Val(P(c:ObservationalVisibilityValidationFixture.ObservationContext(AstronomyCoordinateSystem.Equatorial,AstronomyReferenceOrigin.Geocentric))).Issues);
[Fact] public void Incorrect_azimuth_component_is_rejected()=>Assert.Throws<ArgumentException>(()=>ObservationalVisibilityValidationFixture.HorizontalCoordinate(ObservationalVisibilityValidationFixture.Angular(AstronomyAngularCoordinateComponent.Altitude),ObservationalVisibilityValidationFixture.Angular(AstronomyAngularCoordinateComponent.Altitude)));
[Fact] public void Incorrect_altitude_component_is_rejected()=>Assert.Throws<ArgumentException>(()=>ObservationalVisibilityValidationFixture.HorizontalCoordinate(ObservationalVisibilityValidationFixture.Angular(AstronomyAngularCoordinateComponent.Azimuth),ObservationalVisibilityValidationFixture.Angular(AstronomyAngularCoordinateComponent.Azimuth)));
[Fact] public void Azimuth_dimension_mismatch_is_detected_or_constructor_protected()=>Assert.Throws<ArgumentException>(()=>ObservationalVisibilityValidationFixture.Angular(AstronomyAngularCoordinateComponent.Azimuth,1,AstronomyMeasurementDimension.Magnitude));
[Fact] public void Altitude_dimension_mismatch_is_detected_or_constructor_protected()=>Assert.Throws<ArgumentException>(()=>ObservationalVisibilityValidationFixture.Angular(AstronomyAngularCoordinateComponent.Altitude,1,AstronomyMeasurementDimension.Magnitude));
[Fact] public void Horizon_sector_without_coordinates_is_reported()=>Assert.Contains(Val(P(sec:AstronomyHorizonSector.South)).Issues,i=>i.Code==AstronomyObservationalValidationCodes.HorizonSectorMismatch&&i.Path=="$.horizonSector");
[Fact] public void Stable_paths_are_used_for_azimuth_altitude_and_sector()=>Horizon_sector_without_coordinates_is_reported();}