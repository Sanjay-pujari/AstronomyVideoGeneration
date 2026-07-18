using System.Collections.Immutable;
using Astronomy.MediaFactory.Core.ExecutionValidation;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

/// <summary>Immutable production-state snapshot for building Meteor Shower shadow execution contexts.</summary>
public sealed record MeteorShowerProductionObservation
{
    public MeteorShowerProductionObservation(
        string ExecutionId,
        DateTimeOffset ObservedUtc,
        string? ContentStrategy = null,
        MeteorShowerObservedValue? EventIdentity = null,
        MeteorShowerObservedValue? EventStart = null,
        MeteorShowerObservedValue? EventEnd = null,
        MeteorShowerObservedValue? ObserverLocation = null,
        MeteorShowerObservedValue? Language = null,
        MeteorShowerObservedValue? Format = null,
        MeteorShowerObservedValue? LocalViewingGuide = null,
        MeteorShowerObservedValue? ObservedMeteorActivity = null,
        MeteorShowerObservedValue? ObservedRadiant = null,
        MeteorShowerObservedValue? ObservedPeakWindow = null,
        ImmutableDictionary<string, MeteorShowerObservedValue>? ObservedProjectedFacts = null,
        ImmutableDictionary<string, MeteorShowerObservedRuleValue>? ObservedRuleValues = null,
        ImmutableDictionary<string, string>? Metadata = null)
    {
        this.ExecutionId = ValidationGuard.RequireNonEmpty(ExecutionId, nameof(ExecutionId));
        this.ObservedUtc = ObservedUtc;
        this.ContentStrategy = ContentStrategy;
        this.EventIdentity = EventIdentity;
        this.EventStart = EventStart;
        this.EventEnd = EventEnd;
        this.ObserverLocation = ObserverLocation;
        this.Language = Language;
        this.Format = Format;
        this.LocalViewingGuide = LocalViewingGuide;
        this.ObservedMeteorActivity = ObservedMeteorActivity;
        this.ObservedRadiant = ObservedRadiant;
        this.ObservedPeakWindow = ObservedPeakWindow;
        this.ObservedProjectedFacts = NormalizeObserved(ObservedProjectedFacts);
        this.ObservedRuleValues = NormalizeRules(ObservedRuleValues);
        this.Metadata = ValidationGuard.Metadata(Metadata);
    }

    public string ExecutionId { get; init; }
    public DateTimeOffset ObservedUtc { get; init; }
    public string? ContentStrategy { get; init; }
    public MeteorShowerObservedValue? EventIdentity { get; init; }
    public MeteorShowerObservedValue? EventStart { get; init; }
    public MeteorShowerObservedValue? EventEnd { get; init; }
    public MeteorShowerObservedValue? ObserverLocation { get; init; }
    public MeteorShowerObservedValue? Language { get; init; }
    public MeteorShowerObservedValue? Format { get; init; }
    public MeteorShowerObservedValue? LocalViewingGuide { get; init; }
    public MeteorShowerObservedValue? ObservedMeteorActivity { get; init; }
    public MeteorShowerObservedValue? ObservedRadiant { get; init; }
    public MeteorShowerObservedValue? ObservedPeakWindow { get; init; }
    public ImmutableDictionary<string, MeteorShowerObservedValue> ObservedProjectedFacts { get; init; }
    public ImmutableDictionary<string, MeteorShowerObservedRuleValue> ObservedRuleValues { get; init; }
    public ImmutableDictionary<string, string> Metadata { get; init; }

    private static ImmutableDictionary<string, MeteorShowerObservedValue> NormalizeObserved(ImmutableDictionary<string, MeteorShowerObservedValue>? values)
    {
        if (values is null) return ImmutableDictionary<string, MeteorShowerObservedValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
        var b = ImmutableDictionary.CreateBuilder<string, MeteorShowerObservedValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            var key = ValidationGuard.RequireNonEmpty(pair.Key, "observed projection key");
            if (!b.TryAdd(key, pair.Value)) throw new ArgumentException($"Duplicate observed projection key '{key}' (case-insensitive).", nameof(values));
        }
        return b.ToImmutable();
    }

    private static ImmutableDictionary<string, MeteorShowerObservedRuleValue> NormalizeRules(ImmutableDictionary<string, MeteorShowerObservedRuleValue>? values)
    {
        if (values is null) return ImmutableDictionary<string, MeteorShowerObservedRuleValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
        var b = ImmutableDictionary.CreateBuilder<string, MeteorShowerObservedRuleValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            var key = ValidationGuard.RequireNonEmpty(pair.Key, "observed rule key");
            if (!b.TryAdd(key, pair.Value)) throw new ArgumentException($"Duplicate observed rule key '{key}' (case-insensitive).", nameof(values));
        }
        return b.ToImmutable();
    }
}

public sealed record MeteorShowerObservedValue
{
    public MeteorShowerObservedValue(object? Value, string? ValueType = null, string? SourceId = null, ImmutableArray<string> Evidence = default, ImmutableDictionary<string, string>? Metadata = null)
    { this.Value = Value; this.ValueType = ValidationGuard.Optional(ValueType); this.SourceId = ValidationGuard.Optional(SourceId); this.Evidence = ValidationGuard.Array(Evidence); this.Metadata = ValidationGuard.Metadata(Metadata); }
    public object? Value { get; init; }
    public string? ValueType { get; init; }
    public string? SourceId { get; init; }
    public ImmutableArray<string> Evidence { get; init; }
    public ImmutableDictionary<string, string> Metadata { get; init; }
}

public sealed record MeteorShowerObservedRuleValue
{
    public MeteorShowerObservedRuleValue(bool Passed, string? Actual = null, string? Expected = null, string? Message = null, ImmutableArray<string> Evidence = default, ImmutableDictionary<string, string>? Metadata = null)
    { this.Passed = Passed; this.Actual = ValidationGuard.Optional(Actual); this.Expected = ValidationGuard.Optional(Expected); this.Message = Message?.Trim(); this.Evidence = ValidationGuard.Array(Evidence); this.Metadata = ValidationGuard.Metadata(Metadata); }
    public bool Passed { get; init; }
    public string? Actual { get; init; }
    public string? Expected { get; init; }
    public string? Message { get; init; }
    public ImmutableArray<string> Evidence { get; init; }
    public ImmutableDictionary<string, string> Metadata { get; init; }
}
