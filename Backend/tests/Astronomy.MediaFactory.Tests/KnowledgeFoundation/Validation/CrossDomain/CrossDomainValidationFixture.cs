using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public static class CrossDomainValidationFixture
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset T1 = new(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
    public static readonly AstronomyEntityReference Mars = new("entity:mars", AstronomyEntityKind.Planet, "Mars");
    public static readonly AstronomyEntityReference Venus = new("entity:venus", AstronomyEntityKind.Planet, "Mars");
    public static AstronomyCrossDomainValidationSet EmptySet() => new(Array.Empty<ITypedAstronomyKnowledgePayload>());
    public static AstronomyCrossDomainValidationSet Set(params ITypedAstronomyKnowledgePayload[] payloads) => new(payloads);
    public static AstronomyCrossDomainRelationship Relationship(int l, int r, AstronomyCrossDomainRelationshipKind k) => new(l, r, k);
    public static AstronomyCrossDomainValidationContext Context(AstronomyKnowledgeValidationSeverity minimumSeverity = AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode mode = AstronomyKnowledgeValidationMode.Standard, params AstronomyCrossDomainRelationship[] relationships) => new(new AstronomyKnowledgeValidationRunId("cross-domain-test"), T0, mode, minimumSeverity, relationships: relationships);
    public static AstronomyEntityClassificationPayload Classification(AstronomyEntityReference e) => new(new AstronomyKnowledgeTypeId(e.EntityId), e.EntityKind ?? AstronomyEntityKind.Planet, new[]{new AstronomyClassificationAssignment(new AstronomyClassificationSchemeId("scheme"), new AstronomyClassificationValue("planet", "Planet"), AstronomyClassificationQualifier.Primary)});
    public static AstronomyOrbitalParametersPayload Orbital(AstronomyEntityReference e, AstronomyEpochReference? epoch=null, AstronomyReferenceFrame frame=AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin origin=AstronomyReferenceOrigin.Barycentric, AstronomyMeasurementDimension dim=AstronomyMeasurementDimension.Angle, string id="inclination") => new(new AstronomyKnowledgeTypeId("orbital-"+e.EntityId), new AstronomyOrbitalReferenceContext(e, frame, origin, epoch ?? AstronomyEpochReference.J2000), new[]{new AstronomyOrbitalParameter(new AstronomyOrbitalParameterId(id), AstronomyOrbitalParameterCategory.Orientation, Measurement(dim), epoch: epoch ?? AstronomyEpochReference.J2000)});
    public static AstronomySpatialPositionPayload Position(AstronomyEntityReference origin, AstronomyEpochReference? epoch=null, AstronomyReferenceFrame frame=AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin refOrigin=AstronomyReferenceOrigin.Barycentric) { var u=new AstronomyMeasurementUnit("km","km",AstronomyMeasurementDimension.Distance); var m=new AstronomyMeasurement(1,u); return new(new AstronomyKnowledgeTypeId("pos-"+origin.EntityId), new AstronomySpatialPosition(new AstronomyPositionReferenceContext(frame, refOrigin, AstronomyCoordinateSystem.Cartesian, epoch ?? AstronomyEpochReference.J2000, origin), new AstronomyCartesianPositionValue(new AstronomyCartesianCoordinate(m,m,m)))); }
    public static AstronomyMeasurement Measurement(AstronomyMeasurementDimension d) => new(1, new AstronomyMeasurementUnit("u-"+d, "u", d));
    public static AstronomyObservationConditionsPayload Observation(string loc="site", DateTimeOffset? t=null, AstronomyReferenceFrame f=AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin o=AstronomyReferenceOrigin.Topocentric, AstronomyCoordinateSystem c=AstronomyCoordinateSystem.Horizontal) => new(new AstronomyKnowledgeTypeId("obs-"+loc), new AstronomyObservationContext(loc, t ?? T0, f, o, c), new AstronomyObservationConditions(AstronomySkyConditionKind.Clear, AstronomySeeingQuality.Good, AstronomyTransparencyQuality.Good));
    public static AstronomyVisibilityWindowsPayload Visibility(string loc="site", DateTimeOffset? start=null, DateTimeOffset? end=null, AstronomyReferenceFrame f=AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin o=AstronomyReferenceOrigin.Topocentric, AstronomyCoordinateSystem c=AstronomyCoordinateSystem.Horizontal) => new(new AstronomyKnowledgeTypeId("vis-"+loc), new AstronomyObservationContext(loc, start ?? T0, f, o, c), new[]{new AstronomyVisibilityWindow(new AstronomyObservationTimeWindow(start ?? T0, end ?? T1), new AstronomyVisibilityAssessment(AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod.NakedEye))});
    public static AstronomyEventPayload Event(AstronomyEntityReference e, DateTimeOffset? t=null) => new(new AstronomyKnowledgeTypeId("event"), new AstronomyEvent(new AstronomyEventId("event"), AstronomyEventKind.Conjunction, new AstronomyInstantEventTemporalExtent(t ?? T0), new AstronomyEventReferenceContext(AstronomyEventScope.Global), new[]{new AstronomyEventParticipant(e, AstronomyEventParticipantRole.Primary)}, geometry:new[]{new AstronomyEventGeometryQuantity(new AstronomyEventGeometryQuantityId("inclination"), AstronomyEventGeometryCategory.AngularSeparation, Measurement(AstronomyMeasurementDimension.Angle), AstronomyEpochReference.J2000)}));
    public static AstronomyTemporalPatternPayload Temporal(DateTimeOffset? from=null, DateTimeOffset? through=null) => new(new AstronomyKnowledgeTypeId("temporal"), new AstronomyTemporalPattern(new AstronomyTemporalPatternId("temporal"), AstronomyTemporalPatternKind.EventRecurrence, new AstronomyTemporalPatternReferenceContext(AstronomyTemporalReferenceBasis.Utc), new AstronomyRecurrenceDescription(AstronomyRecurrenceKind.SuppliedOccurrences), suppliedOccurrences:new[]{new AstronomyTemporalOccurrence(from ?? T0, through ?? T1)}, applicability:new AstronomyTemporalApplicability(from ?? T0, through ?? T1)));
    public static void AssertIssue(AstronomyKnowledgeValidationIssue i, string code, string path, string rule) { Assert.Equal(code, i.Code); Assert.Equal(path, i.Path); Assert.Equal(rule, i.RuleId); Assert.True(i.Severity >= AstronomyKnowledgeValidationSeverity.Warning); }
}
