using System.Collections.Immutable;
using Astronomy.MediaFactory.Core.ExecutionContracts;

namespace Astronomy.MediaFactory.Core.ExecutionValidation;

public enum ExecutionValidationStatus { Valid, ValidWithWarnings, Invalid }
public enum ExecutionRequirementOutcome { Satisfied, Missing, ConditionalNotApplicable, Invalid, NotEvaluated }
public enum ExecutionValidationIssueCode { RequiredInputMissing, RequiredSemanticValueMissing, RequiredProjectionMissing, RequiredArtifactMissing, ArtifactCardinalityInvalid, ArtifactEmpty, ValidationRuleFailed, ConditionalRequirementNotEvaluated, ContractMismatch, UnsupportedBoundary, ValidatorFailure }
public enum ExecutionValidationMatchKind { Exact, NotApplicable, Missing }

internal static class ValidationGuard
{
    internal static string RequireNonEmpty(string? value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} must be non-empty.", name) : value.Trim();
    internal static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    internal static ImmutableArray<T> Array<T>(ImmutableArray<T> values) => values.IsDefault ? ImmutableArray<T>.Empty : values;
    internal static ImmutableDictionary<string, string> Metadata(ImmutableDictionary<string, string>? values) => NormalizeStringDictionary(values);
    internal static ImmutableDictionary<string, string> NormalizeStringDictionary(ImmutableDictionary<string, string>? values)
    {
        if (values is null) return ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
        var b = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in values) { var k = RequireNonEmpty(p.Key, "metadata key"); if (!b.TryAdd(k, p.Value)) throw new ArgumentException($"Duplicate key '{k}' (case-insensitive).", nameof(values)); }
        return b.ToImmutable();
    }
    internal static ImmutableDictionary<string, T> NormalizeKeyed<T>(ImmutableDictionary<string, T>? values, Func<T,string> itemKey)
    {
        if (values is null) return ImmutableDictionary<string, T>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
        var b = ImmutableDictionary.CreateBuilder<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in values)
        {
            var k = RequireNonEmpty(p.Key, "value key");
            var expected = RequireNonEmpty(itemKey(p.Value), "item key");
            if (!string.Equals(k, expected, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Dictionary key '{k}' must match item key '{expected}'.", nameof(values));
            if (!b.TryAdd(k, p.Value)) throw new ArgumentException($"Duplicate key '{k}' (case-insensitive).", nameof(values));
        }
        return b.ToImmutable();
    }
    internal static bool IsBlocking(FamilyValidationSeverity severity) => severity == FamilyValidationSeverity.Blocking;
}

public sealed record ExecutionValue
{
    public ExecutionValue(string Key, object? Value, bool IsPresent, string? ValueType = null, string? SourceId = null, ImmutableArray<string> Evidence = default, ImmutableDictionary<string,string>? Metadata = null)
    { this.Key = ValidationGuard.RequireNonEmpty(Key, nameof(Key)); this.Value = Value; this.IsPresent = IsPresent; this.ValueType = ValidationGuard.Optional(ValueType); this.SourceId = ValidationGuard.Optional(SourceId); this.Evidence = ValidationGuard.Array(Evidence); this.Metadata = ValidationGuard.Metadata(Metadata); }
    public string Key { get; init; } public object? Value { get; init; } public string? ValueType { get; init; } public bool IsPresent { get; init; } public string? SourceId { get; init; } public ImmutableArray<string> Evidence { get; init; } public ImmutableDictionary<string,string> Metadata { get; init; }
}
public sealed record ExecutionArtifactValue
{
    public ExecutionArtifactValue(string ArtifactId, bool Exists, bool IsNonEmpty, int ObservedCount, string? RelativePath = null, string? ContentType = null, ImmutableDictionary<string,string>? Metadata = null)
    { if (ObservedCount < 0) throw new ArgumentOutOfRangeException(nameof(ObservedCount)); this.ArtifactId = ValidationGuard.RequireNonEmpty(ArtifactId, nameof(ArtifactId)); this.RelativePath = ValidationGuard.Optional(RelativePath); this.Exists = Exists; this.IsNonEmpty = IsNonEmpty; this.ObservedCount = ObservedCount; this.ContentType = ValidationGuard.Optional(ContentType); this.Metadata = ValidationGuard.Metadata(Metadata); }
    public string ArtifactId { get; init; } public string? RelativePath { get; init; } public bool Exists { get; init; } public bool IsNonEmpty { get; init; } public int ObservedCount { get; init; } public string? ContentType { get; init; } public ImmutableDictionary<string,string> Metadata { get; init; }
}
public sealed record ExecutionRuleValue
{
    public ExecutionRuleValue(string RuleId, bool Passed, string? Actual = null, string? Expected = null, string? Message = null, ImmutableArray<string> Evidence = default, ImmutableDictionary<string,string>? Metadata = null)
    { this.RuleId = ValidationGuard.RequireNonEmpty(RuleId, nameof(RuleId)); this.Passed = Passed; this.Actual = ValidationGuard.Optional(Actual); this.Expected = ValidationGuard.Optional(Expected); this.Message = Message?.Trim(); this.Evidence = ValidationGuard.Array(Evidence); this.Metadata = ValidationGuard.Metadata(Metadata); }
    public string RuleId { get; init; } public bool Passed { get; init; } public string? Actual { get; init; } public string? Expected { get; init; } public string? Message { get; init; } public ImmutableArray<string> Evidence { get; init; } public ImmutableDictionary<string,string> Metadata { get; init; }
}
public sealed record FamilyExecutionContext
{
    public FamilyExecutionContext(string ExecutionId, string DomainId, string FamilyId, string ContractVersion, DateTimeOffset CreatedUtc, string? Format = null, string? Language = null, string? RegionId = null, string? TimeZone = null, ImmutableDictionary<string, ExecutionValue>? InputValues = null, ImmutableDictionary<string, ExecutionValue>? SemanticValues = null, ImmutableDictionary<string, ExecutionValue>? ProjectionValues = null, ImmutableDictionary<string, ExecutionArtifactValue>? ArtifactValues = null, ImmutableDictionary<string, ExecutionRuleValue>? ValidationRuleValues = null, ImmutableDictionary<string,string>? Metadata = null)
    { this.ExecutionId = ValidationGuard.RequireNonEmpty(ExecutionId, nameof(ExecutionId)); this.DomainId = ValidationGuard.RequireNonEmpty(DomainId, nameof(DomainId)); this.FamilyId = ValidationGuard.RequireNonEmpty(FamilyId, nameof(FamilyId)); this.ContractVersion = ValidationGuard.RequireNonEmpty(ContractVersion, nameof(ContractVersion)); this.CreatedUtc = CreatedUtc; this.Format = ValidationGuard.Optional(Format); this.Language = ValidationGuard.Optional(Language); this.RegionId = ValidationGuard.Optional(RegionId); this.TimeZone = ValidationGuard.Optional(TimeZone); this.InputValues = ValidationGuard.NormalizeKeyed(InputValues, v => v.Key); this.SemanticValues = ValidationGuard.NormalizeKeyed(SemanticValues, v => v.Key); this.ProjectionValues = ValidationGuard.NormalizeKeyed(ProjectionValues, v => v.Key); this.ArtifactValues = ValidationGuard.NormalizeKeyed(ArtifactValues, v => v.ArtifactId); this.ValidationRuleValues = ValidationGuard.NormalizeKeyed(ValidationRuleValues, v => v.RuleId); this.Metadata = ValidationGuard.Metadata(Metadata); }
    public string ExecutionId { get; init; } public string DomainId { get; init; } public string FamilyId { get; init; } public string ContractVersion { get; init; } public string? Format { get; init; } public string? Language { get; init; } public string? RegionId { get; init; } public string? TimeZone { get; init; } public ImmutableDictionary<string, ExecutionValue> InputValues { get; init; } public ImmutableDictionary<string, ExecutionValue> SemanticValues { get; init; } public ImmutableDictionary<string, ExecutionValue> ProjectionValues { get; init; } public ImmutableDictionary<string, ExecutionArtifactValue> ArtifactValues { get; init; } public ImmutableDictionary<string, ExecutionRuleValue> ValidationRuleValues { get; init; } public ImmutableDictionary<string,string> Metadata { get; init; } public DateTimeOffset CreatedUtc { get; init; }
}
