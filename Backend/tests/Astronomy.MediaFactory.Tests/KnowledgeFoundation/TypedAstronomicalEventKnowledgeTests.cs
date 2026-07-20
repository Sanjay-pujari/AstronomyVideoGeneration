using System.Reflection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedAstronomicalEventKnowledgeTests
{
    [Fact]
    public void Event_identifiers_and_taxonomies_are_stable()
    {
        var id = new AstronomyEventId(" Event.2026.Jupiter-Venus-Conjunction ");
        Assert.Equal("event.2026.jupiter-venus-conjunction", id.ToString());
        Assert.Equal(id, new AstronomyEventId("event.2026.jupiter-venus-conjunction"));
        Assert.False(default(AstronomyEventId).IsValid);
        Assert.Equal(string.Empty, default(AstronomyEventId).ToString());
        Assert.Throws<ArgumentException>(() => new AstronomyEventId(" "));
        Assert.Throws<ArgumentException>(() => new AstronomyEventId("event bad"));
        Assert.Throws<ArgumentException>(() => new AstronomyEventId("event\tbad"));
        Assert.Throws<ArgumentException>(() => new AstronomyEventId("event\r\nbad"));
        Assert.Equal(new string('a', 128), new AstronomyEventId(new string('a', 128)).Value);
        Assert.Throws<ArgumentException>(() => new AstronomyEventId(new string('a', 129)));
        Assert.Equal(new[] { "Conjunction", "Opposition", "Occultation", "Transit", "SolarEclipse", "LunarEclipse", "MeteorShower", "LunarPhase", "Equinox", "Solstice", "GreatestElongation", "StationaryPoint", "Periapsis", "Apoapsis", "ClosestApproach", "Alignment", "VisibilityPhenomenon", "Other" }, Enum.GetNames<AstronomyEventKind>());
        Assert.Equal(new[] { "Global", "Regional", "Local", "ObserverSpecific", "ReferenceFrameSpecific" }, Enum.GetNames<AstronomyEventScope>());
        Assert.Equal(new[] { "Primary", "Secondary", "Host", "Occulter", "Occulted", "TransitingBody", "TransitedBody", "EclipsingBody", "EclipsedBody", "ShadowCastingBody", "ShadowReceivingBody", "Radiant", "ParentBody", "ReferenceBody", "ObserverTarget", "Additional" }, Enum.GetNames<AstronomyEventParticipantRole>());
        Assert.Equal(new[] { "Instant", "Interval", "ApproximateInstant", "ApproximateInterval" }, Enum.GetNames<AstronomyEventTimeKind>());
        Assert.Equal(new[] { "Start", "Maximum", "Peak", "End", "FirstContact", "SecondContact", "ThirdContact", "FourthContact", "Ingress", "Midpoint", "Egress", "TotalityStart", "TotalityMaximum", "TotalityEnd", "RadiantRise", "Custom" }, Enum.GetNames<AstronomyEventPhaseMarkerKind>());
        Assert.Equal(new[] { "AngularSeparation", "PositionAngle", "Distance", "Obscuration", "Magnitude", "Phase", "Alignment", "ShadowGeometry", "ContactGeometry", "Other" }, Enum.GetNames<AstronomyEventGeometryCategory>());
        Assert.Equal(new[] { "Unspecified", "Routine", "Notable", "Significant", "Exceptional" }, Enum.GetNames<AstronomyEventSignificance>());
    }

    [Fact]
    public void Event_components_guard_local_invariants()
    {
        var entity = new AstronomyEntityReference("body.jupiter");
        var participant = new AstronomyEventParticipant(entity, AstronomyEventParticipantRole.Primary, " Jupiter ");
        Assert.Equal("Jupiter", participant.Label);
        Assert.Null(new AstronomyEventParticipant(entity, AstronomyEventParticipantRole.Secondary, " ").Label);
        Assert.Throws<ArgumentNullException>(() => new AstronomyEventParticipant(null!, AstronomyEventParticipantRole.Primary));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyEventParticipant(entity, (AstronomyEventParticipantRole)999));
        Assert.Throws<ArgumentException>(() => new AstronomyEventParticipant(entity, AstronomyEventParticipantRole.Primary, "bad\nlabel"));

        var instant = new AstronomyInstantEventTemporalExtent(Utc(2026, 8, 1), true);
        var interval = new AstronomyIntervalEventTemporalExtent(Utc(2026, 8, 1), Utc(2026, 8, 2));
        Assert.Equal(AstronomyEventTimeKind.ApproximateInstant, instant.Kind);
        Assert.Equal(AstronomyEventTimeKind.Interval, interval.Kind);
        Assert.Throws<ArgumentException>(() => new AstronomyInstantEventTemporalExtent(DateTimeOffset.Parse("2026-08-01T00:00:00+05:30")));
        Assert.Throws<ArgumentException>(() => new AstronomyIntervalEventTemporalExtent(Utc(2026, 8, 2), Utc(2026, 8, 1)));
        Assert.Equal(new AstronomyIntervalEventTemporalExtent(Utc(2026, 8, 1), Utc(2026, 8, 1)), new AstronomyIntervalEventTemporalExtent(Utc(2026, 8, 1), Utc(2026, 8, 1)));
        Assert.Equal(typeof(AstronomyEventTemporalExtent), typeof(AstronomyInstantEventTemporalExtent).BaseType);
        Assert.Equal(typeof(AstronomyEventTemporalExtent), typeof(AstronomyIntervalEventTemporalExtent).BaseType);
        Assert.Null(typeof(AstronomyEventTemporalExtent).GetConstructor(Type.EmptyTypes));

        var marker = new AstronomyEventPhaseMarker(AstronomyEventPhaseMarkerKind.Peak, Utc(2026, 8, 1), " Peak ");
        Assert.Equal("Peak", marker.Label);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyEventPhaseMarker((AstronomyEventPhaseMarkerKind)999, Utc(2026, 8, 1)));
        Assert.Throws<ArgumentException>(() => new AstronomyEventPhaseMarker(AstronomyEventPhaseMarkerKind.Peak, DateTimeOffset.Parse("2026-08-01T00:00:00+05:30")));

        var qid = new AstronomyEventGeometryQuantityId(" Event.Minimum-Angular-Separation ");
        Assert.Equal("event.minimum-angular-separation", qid.Value);
        Assert.False(default(AstronomyEventGeometryQuantityId).IsValid);
        var measurement = new AstronomyMeasurement(1.2m, new AstronomyMeasurementUnit("degree", "deg", AstronomyMeasurementDimension.Angle));
        var geometry = new AstronomyEventGeometryQuantity(qid, AstronomyEventGeometryCategory.AngularSeparation, measurement, AstronomyEpochReference.J2000, " supplied ");
        Assert.Equal("supplied", geometry.Note);
        Assert.Throws<ArgumentException>(() => new AstronomyEventGeometryQuantity(default, AstronomyEventGeometryCategory.AngularSeparation, measurement));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyEventGeometryQuantity(qid, (AstronomyEventGeometryCategory)999, measurement));

        var circumstance = new AstronomyEventCircumstance(new AstronomyEventCircumstanceId(" Circumstance.Totality "), " yes ", " note ");
        Assert.Equal("circumstance.totality", circumstance.CircumstanceId.Value);
        Assert.Equal("yes", circumstance.Value);
        Assert.Throws<ArgumentException>(() => new AstronomyEventCircumstance(default));
        Assert.Throws<ArgumentException>(() => new AstronomyEventCircumstance(new("circumstance.partial"), "bad\nvalue"));
    }

    [Fact]
    public void Event_aggregate_and_payload_are_immutable_deterministic_and_typed()
    {
        var jupiter = new AstronomyEventParticipant(new AstronomyEntityReference("body.jupiter"), AstronomyEventParticipantRole.Secondary);
        var venus = new AstronomyEventParticipant(new AstronomyEntityReference("body.venus"), AstronomyEventParticipantRole.Primary);
        var participants = new List<AstronomyEventParticipant> { jupiter, venus };
        var peak = new AstronomyEventPhaseMarker(AstronomyEventPhaseMarkerKind.Peak, Utc(2026, 8, 2));
        var start = new AstronomyEventPhaseMarker(AstronomyEventPhaseMarkerKind.Start, Utc(2026, 8, 1));
        var geometry = new AstronomyEventGeometryQuantity(new("event.angular-separation"), AstronomyEventGeometryCategory.AngularSeparation, new(1m, new("degree", "deg", AstronomyMeasurementDimension.Angle)));
        var circumstances = new[] { new AstronomyEventCircumstance(new("circumstance.eastern-elongation")) };
        var evt = new AstronomyEvent(new("event.2026.jupiter-venus-conjunction"), AstronomyEventKind.Conjunction, new AstronomyIntervalEventTemporalExtent(Utc(2026, 8, 1), Utc(2026, 8, 3)), new AstronomyEventReferenceContext(AstronomyEventScope.Global), participants, [peak, start], [geometry], circumstances, AstronomyEventSignificance.Notable, " Conjunction ", " Supplied event knowledge ");
        participants.Clear();
        Assert.Equal([AstronomyEventParticipantRole.Primary, AstronomyEventParticipantRole.Secondary], evt.Participants.Select(p => p.Role).ToArray());
        Assert.Equal([Utc(2026, 8, 1), Utc(2026, 8, 2)], evt.PhaseMarkers.Select(p => p.TimeUtc).ToArray());
        Assert.Equal("Conjunction", evt.Name);
        Assert.Equal("Supplied event knowledge", evt.Summary);
        Assert.Throws<NotSupportedException>(() => ((IList<AstronomyEventParticipant>)evt.Participants).Add(jupiter));
        Assert.Throws<ArgumentException>(() => new AstronomyEvent(new("event.x"), AstronomyEventKind.Conjunction, evt.TemporalExtent, evt.ReferenceContext, []));
        Assert.Throws<ArgumentException>(() => new AstronomyEvent(new("event.x"), AstronomyEventKind.Conjunction, evt.TemporalExtent, evt.ReferenceContext, [jupiter, jupiter]));
        Assert.Throws<ArgumentException>(() => new AstronomyEvent(new("event.x"), AstronomyEventKind.Conjunction, evt.TemporalExtent, evt.ReferenceContext, [jupiter], [peak, peak]));
        Assert.Throws<ArgumentException>(() => new AstronomyEvent(new("event.x"), AstronomyEventKind.Conjunction, evt.TemporalExtent, evt.ReferenceContext, [jupiter], geometry: [geometry, geometry]));
        Assert.Throws<ArgumentException>(() => new AstronomyEvent(new("event.x"), AstronomyEventKind.Conjunction, evt.TemporalExtent, evt.ReferenceContext, [jupiter], circumstances: [circumstances[0], circumstances[0]]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyEvent(new("event.x"), (AstronomyEventKind)999, evt.TemporalExtent, evt.ReferenceContext, [jupiter]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyEvent(new("event.x"), AstronomyEventKind.Conjunction, evt.TemporalExtent, evt.ReferenceContext, [jupiter], significance: (AstronomyEventSignificance)999));

        var payload = new AstronomyEventPayload(new AstronomyKnowledgeTypeId("typed.event.astronomical.v1"), evt);
        Assert.IsAssignableFrom<ITypedAstronomyKnowledgePayload>(payload);
        Assert.Equal(AstronomyKnowledgeDomain.Event, payload.Domain);
        Assert.Equal(AstronomyKnowledgePayloadFamily.AstronomicalEvent, payload.Family);
        Assert.Same(evt, payload.Event);
        Assert.Equal(payload, new AstronomyEventPayload(new("typed.event.astronomical.v1"), evt));
        Assert.Equal(payload.GetHashCode(), new AstronomyEventPayload(new("typed.event.astronomical.v1"), evt).GetHashCode());
        Assert.Single(typeof(AstronomyEventPayload).GetProperties().Where(p => p.PropertyType == typeof(AstronomyEvent)));
        Assert.DoesNotContain(typeof(AstronomyEventPayload).GetProperties(), p => p.Name.Contains("Catalog") || p.Name.Contains("History") || p.Name.Contains("Forecast") || p.Name.Contains("Rank"));
    }

    [Fact]
    public void Observer_specific_reference_context_requires_observation_context()
    {
        Assert.Throws<ArgumentException>(() => new AstronomyEventReferenceContext(AstronomyEventScope.ObserverSpecific));
        var observationContext = new Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation.AstronomyObservationContext("site.udaipur", Utc(2026, 8, 1));
        var context = new AstronomyEventReferenceContext(AstronomyEventScope.ObserverSpecific, observationContext: observationContext);
        Assert.Equal(observationContext, context.ObservationContext);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyEventReferenceContext((AstronomyEventScope)999));
        Assert.Equal(context, new AstronomyEventReferenceContext(AstronomyEventScope.ObserverSpecific, observationContext: observationContext));
    }

    private static DateTimeOffset Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);
}
