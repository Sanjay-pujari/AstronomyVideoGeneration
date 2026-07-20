using System.Reflection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedTemporalKnowledgeTests
{
    private static readonly AstronomyMeasurementUnit Day = new("d", "d", AstronomyMeasurementDimension.Time);
    private static readonly AstronomyMeasurementUnit Scalar = new("ratio", "1", AstronomyMeasurementDimension.Dimensionless);
    private static readonly AstronomyMeasurementUnit Metre = new("m", "m", AstronomyMeasurementDimension.Distance);
    private static AstronomyMeasurement Time(decimal value) => new(value, Day);
    private static AstronomyMeasurement Ratio(decimal value) => new(value, Scalar);
    private static AstronomyTemporalPatternReferenceContext UtcContext() => new(AstronomyTemporalReferenceBasis.Utc);
    private static AstronomyRecurrenceDescription None() => new(AstronomyRecurrenceKind.None);

    [Fact]
    public void Temporal_ids_normalize_and_guard_tokens()
    {
        Assert.Equal("temporal.lunar-cycle", new AstronomyTemporalPatternId(" Temporal.Lunar-Cycle ").ToString());
        Assert.Equal(new AstronomyTemporalPatternId("temporal.lunar-cycle"), new AstronomyTemporalPatternId("TEMPORAL.LUNAR-CYCLE"));
        Assert.False(default(AstronomyTemporalPatternId).IsValid);
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalPatternId(" "));
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalPatternId("bad token"));
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalPatternId("bad\n"));
        Assert.Equal(128, new AstronomyTemporalPatternId(new string('A', 128)).Value.Length);
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalPatternId(new string('a', 129)));
        Assert.Equal("phase.full-moon", new AstronomyCyclePhaseId(" PHASE.FULL-MOON ").ToString());
        Assert.False(default(AstronomyCyclePhaseId).IsValid);
    }

    [Fact]
    public void Temporal_taxonomies_are_frozen_and_guarded()
    {
        Assert.Equal(new[] { "Periodic", "QuasiPeriodic", "Seasonal", "CalendarRelative", "EventRecurrence", "PhaseCycle", "RotationCycle", "OrbitalCycle", "SynodicCycle", "ActivityCycle", "ObservationSeason", "Other" }, Enum.GetNames<AstronomyTemporalPatternKind>());
        Assert.Equal(new[] { "Utc", "EpochRelative", "CalendarRelative", "EntityRelative", "EventRelative", "ObserverRelative" }, Enum.GetNames<AstronomyTemporalReferenceBasis>());
        Assert.Equal(new[] { "Day", "Week", "Month", "Year", "SiderealDay", "SynodicMonth", "JulianYear", "Custom" }, Enum.GetNames<AstronomyCadenceUnit>());
        Assert.Equal(new[] { "UtcInstant", "Epoch", "CalendarDate", "DayOfYear", "MonthOfYear", "Custom" }, Enum.GetNames<AstronomyTemporalAnchorKind>());
        Assert.Equal(new[] { "None", "FixedPeriod", "CalendarInterval", "Annual", "Monthly", "Seasonal", "Irregular", "SuppliedOccurrences" }, Enum.GetNames<AstronomyRecurrenceKind>());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyCalendarInterval(1, (AstronomyCadenceUnit)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyRecurrenceDescription((AstronomyRecurrenceKind)999));
    }

    [Fact]
    public void Cycle_period_and_phase_enforce_local_measurement_invariants()
    {
        var period = new AstronomyCyclePeriod(Time(29.5m), true);
        Assert.True(period.IsApproximate);
        Assert.Equal(period, new AstronomyCyclePeriod(Time(29.5m), true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyCyclePeriod(Time(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyCyclePeriod(Time(-1)));
        Assert.Throws<ArgumentException>(() => new AstronomyCyclePeriod(new AstronomyMeasurement(1, Metre)));
        var phase = new AstronomyCyclePhase(new("phase.full"), Ratio(.5m), Time(1), " Full ", " Note ");
        Assert.Equal("Full", phase.Name);
        Assert.Equal("Note", phase.Note);
        Assert.Throws<ArgumentException>(() => new AstronomyCyclePhase(default));
        Assert.Throws<ArgumentException>(() => new AstronomyCyclePhase(new("phase.x"), new AstronomyMeasurement(1, Metre)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyCyclePhase(new("phase.x"), Ratio(1.1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyCyclePhase(new("phase.x"), duration: Time(0)));
    }

    [Fact]
    public void Temporal_anchor_hierarchy_is_closed_and_validates_variants()
    {
        var baseCtor = typeof(AstronomyTemporalAnchor).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single(c => c.GetParameters().Length == 0);
        Assert.True(baseCtor.IsFamilyAndAssembly);
        Assert.Equal(AstronomyTemporalAnchorKind.UtcInstant, new AstronomyUtcTemporalAnchor(DateTimeOffset.Parse("2026-01-01T00:00:00Z")).Kind);
        Assert.Throws<ArgumentException>(() => new AstronomyUtcTemporalAnchor(DateTimeOffset.Parse("2026-01-01T00:00:00+01:00")));
        Assert.Equal(AstronomyTemporalAnchorKind.Epoch, new AstronomyEpochTemporalAnchor(AstronomyEpochReference.J2000).Kind);
        Assert.Equal(AstronomyTemporalAnchorKind.CalendarDate, new AstronomyCalendarDateTemporalAnchor(2, 29).Kind);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyCalendarDateTemporalAnchor(2, 30));
        Assert.Equal(366, new AstronomyDayOfYearTemporalAnchor(366).DayOfYear);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyDayOfYearTemporalAnchor(367));
        Assert.Equal(12, new AstronomyMonthTemporalAnchor(12).Month);
        Assert.Throws<ArgumentException>(() => new AstronomyCustomTemporalAnchor("bad token"));
    }

    [Fact]
    public void Recurrence_seasons_occurrences_and_contexts_enforce_local_rules()
    {
        Assert.Throws<ArgumentException>(() => new AstronomyRecurrenceDescription(AstronomyRecurrenceKind.FixedPeriod));
        Assert.Throws<ArgumentException>(() => new AstronomyRecurrenceDescription(AstronomyRecurrenceKind.CalendarInterval));
        Assert.Throws<ArgumentException>(() => new AstronomyRecurrenceDescription(AstronomyRecurrenceKind.Irregular, fixedPeriod: new(Time(1))));
        Assert.Equal("note", new AstronomyRecurrenceDescription(AstronomyRecurrenceKind.Annual, anchor: new AstronomyCalendarDateTemporalAnchor(8, 12), note: " note ").Note);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyCalendarInterval(0, AstronomyCadenceUnit.Month));
        var season = new AstronomySeasonalPattern(new(12, 1), new(1, 15), true, " Winter ");
        Assert.True(season.CrossesYearBoundary);
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Assert.Equal(start, new AstronomyTemporalOccurrence(start, start).EndUtc);
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalOccurrence(DateTimeOffset.Parse("2026-01-01T00:00:00+01:00")));
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalOccurrence(start, start.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalPatternReferenceContext(AstronomyTemporalReferenceBasis.EntityRelative));
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalPatternReferenceContext(AstronomyTemporalReferenceBasis.EpochRelative));
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalPatternReferenceContext(AstronomyTemporalReferenceBasis.ObserverRelative));
        Assert.NotNull(new AstronomyTemporalPatternReferenceContext(AstronomyTemporalReferenceBasis.EntityRelative, entity: new AstronomyEntityReference("earth")));
        Assert.NotNull(new AstronomyTemporalPatternReferenceContext(AstronomyTemporalReferenceBasis.ObserverRelative, observationContext: new AstronomyObservationContext("site", start)));
    }

    [Fact]
    public void Temporal_pattern_aggregate_orders_copies_rejects_duplicates_and_has_value_equality()
    {
        var phaseA = new AstronomyCyclePhase(new("phase.a"), Ratio(.5m));
        var phaseB = new AstronomyCyclePhase(new("phase.b"));
        var occurrences = new[] { new AstronomyTemporalOccurrence(DateTimeOffset.Parse("2026-02-01T00:00:00Z")), new AstronomyTemporalOccurrence(DateTimeOffset.Parse("2026-01-01T00:00:00Z")) };
        var pattern = new AstronomyTemporalPattern(new("temporal.test"), AstronomyTemporalPatternKind.Periodic, UtcContext(), None(), phases: [phaseA, phaseB], suppliedOccurrences: occurrences, name: " Name ", summary: " Summary ");
        occurrences[0] = new AstronomyTemporalOccurrence(DateTimeOffset.Parse("2027-01-01T00:00:00Z"));
        Assert.Equal("Name", pattern.Name);
        Assert.Equal("phase.b", pattern.Phases[0].PhaseId.Value);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), pattern.SuppliedOccurrences[0].StartUtc);
        Assert.Throws<NotSupportedException>(() => ((IList<AstronomyCyclePhase>)pattern.Phases).Add(phaseA));
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalPattern(new("temporal.test"), AstronomyTemporalPatternKind.Periodic, UtcContext(), None(), phases: [phaseA, phaseA]));
        Assert.Throws<ArgumentException>(() => new AstronomyTemporalPattern(new("temporal.test"), AstronomyTemporalPatternKind.Periodic, UtcContext(), None(), suppliedOccurrences: [occurrences[1], occurrences[1]]));
        var same = new AstronomyTemporalPattern(new("temporal.test"), AstronomyTemporalPatternKind.Periodic, UtcContext(), None(), phases: [phaseB, phaseA], suppliedOccurrences: pattern.SuppliedOccurrences, name: "Name", summary: "Summary");
        Assert.Equal(pattern, same);
        Assert.Equal(pattern.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void Temporal_payload_shape_excludes_scheduling_and_envelope_state()
    {
        var payload = new AstronomyTemporalPatternPayload(new AstronomyKnowledgeTypeId("typed.temporal.pattern.v1"), new AstronomyTemporalPattern(new("temporal.test"), AstronomyTemporalPatternKind.Other, UtcContext(), None()));
        Assert.IsAssignableFrom<ITypedAstronomyKnowledgePayload>(payload);
        Assert.Equal(AstronomyKnowledgeDomain.Temporal, payload.Domain);
        Assert.Equal(AstronomyKnowledgePayloadFamily.TemporalCycle, payload.Family);
        Assert.Equal(payload, new AstronomyTemporalPatternPayload(new AstronomyKnowledgeTypeId("typed.temporal.pattern.v1"), payload.Pattern));
        var names = typeof(AstronomyTemporalPatternPayload).GetProperties().Select(p => p.Name).ToArray();
        Assert.Contains("Pattern", names);
        Assert.DoesNotContain(names, n => n.Contains("Evidence") || n.Contains("Confidence") || n.Contains("Audit") || n.Contains("Validity") || n.Contains("Schedule") || n.Contains("Next"));
    }
}
