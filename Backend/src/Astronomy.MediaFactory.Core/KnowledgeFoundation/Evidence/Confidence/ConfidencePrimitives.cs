namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Confidence;

using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;
using System.Globalization;

public enum KnowledgeConfidenceLevel
{
    Unknown,
    VeryLow,
    Low,
    Moderate,
    High,
    VeryHigh
}

public enum ConfidenceAssessmentMethod
{
    HumanExpertReview,
    HumanEditorialReview,
    RuleBased,
    StatisticalAnalysis,
    InstrumentDerived,
    SourceConsensus,
    Hybrid,
    Imported
}

public enum ConfidenceAssessorType
{
    HumanExpert,
    HumanEditor,
    AutomatedRule,
    StatisticalModel,
    InstrumentSystem,
    ExternalAuthority,
    HybridProcess
}

public enum ConfidenceFactorDirection
{
    Supports,
    Reduces,
    Neutral
}

public static class ConfidenceAssessmentEnumGuard
{
    public static KnowledgeConfidenceLevel RequireDefined(KnowledgeConfidenceLevel level, string parameterName = "level")
        => Enum.IsDefined(level) ? level : throw new ArgumentOutOfRangeException(parameterName, level, "Knowledge confidence level is not defined.");

    public static ConfidenceAssessmentMethod RequireDefined(ConfidenceAssessmentMethod method, string parameterName = "method")
        => Enum.IsDefined(method) ? method : throw new ArgumentOutOfRangeException(parameterName, method, "Confidence assessment method is not defined.");

    public static ConfidenceAssessorType RequireDefined(ConfidenceAssessorType assessorType, string parameterName = "assessorType")
        => Enum.IsDefined(assessorType) ? assessorType : throw new ArgumentOutOfRangeException(parameterName, assessorType, "Confidence assessor type is not defined.");

    public static ConfidenceFactorDirection RequireDefined(ConfidenceFactorDirection direction, string parameterName = "direction")
        => Enum.IsDefined(direction) ? direction : throw new ArgumentOutOfRangeException(parameterName, direction, "Confidence factor direction is not defined.");
}

public readonly record struct ConfidenceAssessmentId
{
    public const int MaxLength = 256;

    public ConfidenceAssessmentId(string value)
    {
        Value = KnowledgeId.NormalizeToken(value, nameof(value), "Confidence assessment ID", MaxLength);
    }

    public string Value { get; }
    public override string ToString() => Value ?? string.Empty;
    public static ConfidenceAssessmentId Create(string value) => new(value);
}

public readonly record struct KnowledgeConfidenceScore
{
    public KnowledgeConfidenceScore(double value)
    {
        if (double.IsNaN(value)) throw new ArgumentException("Knowledge confidence score must not be NaN.", nameof(value));
        if (double.IsInfinity(value)) throw new ArgumentException("Knowledge confidence score must be finite.", nameof(value));
        if (value < 0d || value > 1d) throw new ArgumentOutOfRangeException(nameof(value), value, "Knowledge confidence score must be between 0.0 and 1.0 inclusive.");
        Value = value;
    }

    public double Value { get; }
    public override string ToString() => Value.ToString("0.#################", CultureInfo.InvariantCulture);
    public static KnowledgeConfidenceScore FromNormalizedValue(double value) => new(value);
}

public sealed record ConfidenceAssessorReference
{
    public const int MaxAssessorIdLength = 256;
    public const int MaxDisplayNameLength = 256;
    public const int MaxOptionalTextLength = 256;

    public ConfidenceAssessorReference(
        string assessorId,
        ConfidenceAssessorType assessorType,
        string displayName,
        string? organization = null,
        string? modelOrSystemVersion = null)
    {
        AssessorId = KnowledgeId.NormalizeToken(assessorId, nameof(assessorId), "Confidence assessor ID", MaxAssessorIdLength);
        AssessorType = ConfidenceAssessmentEnumGuard.RequireDefined(assessorType, nameof(assessorType));
        DisplayName = NormalizeRequiredText(displayName, nameof(displayName), MaxDisplayNameLength, "Confidence assessor display name");
        Organization = NormalizeOptionalText(organization, nameof(organization), MaxOptionalTextLength, "Confidence assessor organization");
        ModelOrSystemVersion = NormalizeOptionalText(modelOrSystemVersion, nameof(modelOrSystemVersion), MaxOptionalTextLength, "Confidence assessor model or system version");
    }

    public string AssessorId { get; }
    public ConfidenceAssessorType AssessorType { get; }
    public string DisplayName { get; }
    public string? Organization { get; }
    public string? ModelOrSystemVersion { get; }

    internal static string NormalizeRequiredText(string value, string parameterName, int maxLength, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{displayName} is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentException($"{displayName} must be {maxLength} characters or fewer.", parameterName);
        if (normalized.Any(char.IsControl)) throw new ArgumentException($"{displayName} must not contain control characters.", parameterName);
        return normalized;
    }

    internal static string? NormalizeOptionalText(string? value, string parameterName, int maxLength, string displayName)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length == 0) return null;
        if (normalized.Length > maxLength) throw new ArgumentException($"{displayName} must be {maxLength} characters or fewer.", parameterName);
        if (normalized.Any(char.IsControl)) throw new ArgumentException($"{displayName} must not contain control characters.", parameterName);
        return normalized;
    }
}

public sealed record ConfidenceAssessmentFactor
{
    public const int MaxCodeLength = 128;
    public const int MaxNoteLength = 512;

    public ConfidenceAssessmentFactor(string code, ConfidenceFactorDirection direction, string? note = null)
    {
        Code = KnowledgeId.NormalizeToken(code, nameof(code), "Confidence assessment factor code", MaxCodeLength).ToLowerInvariant();
        Direction = ConfidenceAssessmentEnumGuard.RequireDefined(direction, nameof(direction));
        Note = ConfidenceAssessorReference.NormalizeOptionalText(note, nameof(note), MaxNoteLength, "Confidence assessment factor note");
    }

    public string Code { get; }
    public ConfidenceFactorDirection Direction { get; }
    public string? Note { get; }
}

public sealed class AstronomyKnowledgeConfidenceAssessment : IEquatable<AstronomyKnowledgeConfidenceAssessment>
{
    public const int MaxRationaleLength = 2048;

    public AstronomyKnowledgeConfidenceAssessment(
        ConfidenceAssessmentId id,
        KnowledgeId knowledgeId,
        KnowledgeVersion knowledgeVersion,
        KnowledgeConfidenceLevel level,
        KnowledgeConfidenceScore? score,
        ConfidenceAssessmentMethod method,
        ConfidenceAssessorReference assessor,
        KnowledgeAuditMetadata audit,
        IEnumerable<EvidenceId> evidenceIds,
        IEnumerable<ConfidenceAssessmentFactor> factors,
        string? rationale = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value)) throw new ArgumentException("Confidence assessment ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(knowledgeId.Value)) throw new ArgumentException("Knowledge ID is required.", nameof(knowledgeId));
        if (knowledgeVersion.Revision < 1) throw new ArgumentOutOfRangeException(nameof(knowledgeVersion), knowledgeVersion, "Knowledge version must be positive.");

        Id = id;
        KnowledgeId = knowledgeId;
        KnowledgeVersion = knowledgeVersion;
        Level = ConfidenceAssessmentEnumGuard.RequireDefined(level, nameof(level));
        Score = score;
        Method = ConfidenceAssessmentEnumGuard.RequireDefined(method, nameof(method));
        Assessor = assessor ?? throw new ArgumentNullException(nameof(assessor));
        Audit = audit ?? throw new ArgumentNullException(nameof(audit));
        EvidenceIds = CopyEvidenceIds(evidenceIds);
        Factors = CopyFactors(factors);
        Rationale = ConfidenceAssessorReference.NormalizeOptionalText(rationale, nameof(rationale), MaxRationaleLength, "Confidence assessment rationale");
    }

    public ConfidenceAssessmentId Id { get; }
    public KnowledgeId KnowledgeId { get; }
    public KnowledgeVersion KnowledgeVersion { get; }
    public KnowledgeConfidenceLevel Level { get; }
    public KnowledgeConfidenceScore? Score { get; }
    public ConfidenceAssessmentMethod Method { get; }
    public ConfidenceAssessorReference Assessor { get; }
    public IReadOnlyList<EvidenceId> EvidenceIds { get; }
    public IReadOnlyList<ConfidenceAssessmentFactor> Factors { get; }
    public string? Rationale { get; }
    public KnowledgeAuditMetadata Audit { get; }

    public bool HasSameAssessmentIdentityAs(AstronomyKnowledgeConfidenceAssessment other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Id == other.Id;
    }

    public bool Equals(AstronomyKnowledgeConfidenceAssessment? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as AstronomyKnowledgeConfidenceAssessment);
    public override int GetHashCode() => Id.GetHashCode();

    private static IReadOnlyList<EvidenceId> CopyEvidenceIds(IEnumerable<EvidenceId> evidenceIds)
    {
        ArgumentNullException.ThrowIfNull(evidenceIds);
        var ordered = evidenceIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        if (ordered.Any(id => string.IsNullOrWhiteSpace(id.Value))) throw new ArgumentException("Confidence assessment evidence IDs cannot contain default values.", nameof(evidenceIds));
        if (ordered.Distinct().Count() != ordered.Length) throw new ArgumentException("Confidence assessment evidence IDs must be unique.", nameof(evidenceIds));
        return Array.AsReadOnly(ordered);
    }

    private static IReadOnlyList<ConfidenceAssessmentFactor> CopyFactors(IEnumerable<ConfidenceAssessmentFactor> factors)
    {
        ArgumentNullException.ThrowIfNull(factors);
        var ordered = factors
            .Select(factor => factor ?? throw new ArgumentException("Confidence assessment factors cannot contain null entries.", nameof(factors)))
            .OrderBy(factor => factor.Code, StringComparer.Ordinal)
            .ToArray();
        if (ordered.GroupBy(factor => factor.Code, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Confidence assessment factors must have unique codes.", nameof(factors));
        return Array.AsReadOnly(ordered);
    }
}
