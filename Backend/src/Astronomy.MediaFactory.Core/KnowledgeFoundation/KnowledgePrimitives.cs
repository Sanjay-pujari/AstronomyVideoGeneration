using Astronomy.MediaFactory.Core.KnowledgeFoundation.Serialization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.AstronomyDomain.Families;
using Astronomy.MediaFactory.Core.AstronomyDomain.Identity;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation;

[JsonConverter(typeof(StrictKnowledgeFoundationStatusJsonConverter))]
public enum KnowledgeFoundationStatus
{
    Draft,
    Reviewed,
    Approved,
    Deprecated,
    Archived
}

[JsonConverter(typeof(StrictKnowledgeStatementKindJsonConverter))]
public enum KnowledgeStatementKind
{
    Scientific,
    Observation,
    Educational,
    Historical,
    Cultural,
    Safety,
    Terminology,
    Visual
}

public static class KnowledgeFoundationEnumGuard
{
    public static KnowledgeFoundationStatus RequireDefined(KnowledgeFoundationStatus status, string parameterName = "status")
        => Enum.IsDefined(status) ? status : throw new ArgumentOutOfRangeException(parameterName, status, "Knowledge status is not defined.");

    public static KnowledgeStatementKind RequireDefined(KnowledgeStatementKind kind, string parameterName = "kind")
        => Enum.IsDefined(kind) ? kind : throw new ArgumentOutOfRangeException(parameterName, kind, "Knowledge statement kind is not defined.");
}

public readonly record struct KnowledgeId
{
    private const int MaxLength = 256;

    public KnowledgeId(string value)
    {
        Value = NormalizeToken(value, nameof(value), "Knowledge ID", MaxLength);
    }

    public string Value { get; }
    public override string ToString() => Value ?? string.Empty;
    public static KnowledgeId Create(string value) => new(value);
    public static bool TryParse(string? value, out KnowledgeId id)
    {
        try
        {
            id = new KnowledgeId(value!);
            return true;
        }
        catch (ArgumentException)
        {
            id = default;
            return false;
        }
    }

    internal static string NormalizeToken(string value, string parameterName, string displayName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{displayName} is required.", parameterName);

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException($"{displayName} must be {maxLength} characters or fewer.", parameterName);
        if (normalized.Any(char.IsWhiteSpace))
            throw new ArgumentException($"{displayName} must not contain whitespace.", parameterName);
        if (normalized.Any(char.IsControl))
            throw new ArgumentException($"{displayName} must not contain control characters.", parameterName);

        return normalized;
    }
}

public readonly record struct KnowledgeVersion : IComparable<KnowledgeVersion>
{
    public KnowledgeVersion(int revision)
    {
        if (revision < 1)
            throw new ArgumentOutOfRangeException(nameof(revision), "Knowledge revision must be positive.");

        Revision = revision;
    }

    public int Revision { get; }
    public KnowledgeVersion Next() => Revision == int.MaxValue ? throw new OverflowException("Knowledge revision cannot exceed Int32.MaxValue.") : new(Revision + 1);
    public int CompareTo(KnowledgeVersion other) => Revision.CompareTo(other.Revision);
    public override string ToString() => Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static KnowledgeVersion Initial { get; } = new(1);
    public static bool operator <(KnowledgeVersion left, KnowledgeVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(KnowledgeVersion left, KnowledgeVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(KnowledgeVersion left, KnowledgeVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(KnowledgeVersion left, KnowledgeVersion right) => left.CompareTo(right) >= 0;
}

public sealed record AstronomyEntityReference(
    string EntityId,
    AstronomyEntityKind? EntityKind = null,
    string? CanonicalName = null)
{
    public string EntityId { get; init; } = KnowledgeId.NormalizeToken(EntityId, nameof(EntityId), "Astronomy entity reference ID", 256);
    public static AstronomyEntityReference FromIdentity(AstronomyEntityIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new(identity.EntityId, identity.EntityKind, identity.CanonicalName);
    }
}

public sealed record AstronomyFamilyReference(
    string FamilyId,
    AstronomyFamilyKind? FamilyKind = null)
{
    public string FamilyId { get; init; } = KnowledgeId.NormalizeToken(FamilyId, nameof(FamilyId), "Astronomy family reference ID", 256);
    public static AstronomyFamilyReference FromFamily(IAstronomyDomainFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return new(family.FamilyId, family.FamilyKind);
    }
}

public sealed record KnowledgeLanguageTag
{
    private static readonly Regex Pattern = new("^(und|[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public KnowledgeLanguageTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Language tag is required.", nameof(value));

        var normalized = value.Trim();
        if (normalized.Any(char.IsWhiteSpace) || normalized.Any(char.IsControl) || !Pattern.IsMatch(normalized))
            throw new ArgumentException("Language tag must be a conservative structural BCP-47-style tag.", nameof(value));

        Value = NormalizeCase(normalized);
    }

    public string Value { get; }
    public override string ToString() => Value;
    public static KnowledgeLanguageTag Invariant { get; } = new("und");

    private static string NormalizeCase(string value)
    {
        var parts = value.Split('-');
        if (parts[0].Equals("und", StringComparison.OrdinalIgnoreCase)) return "und";
        parts[0] = parts[0].ToLowerInvariant();
        for (var i = 1; i < parts.Length; i++)
            parts[i] = parts[i].Length == 2 ? parts[i].ToUpperInvariant() : parts[i].ToLowerInvariant();
        return string.Join('-', parts);
    }
}

public sealed record KnowledgeValidityRange
{
    public KnowledgeValidityRange(DateTimeOffset? effectiveFromUtc = null, DateTimeOffset? effectiveToUtc = null)
    {
        EffectiveFromUtc = NormalizeUtc(effectiveFromUtc, nameof(effectiveFromUtc));
        EffectiveToUtc = NormalizeUtc(effectiveToUtc, nameof(effectiveToUtc));
        if (EffectiveFromUtc.HasValue && EffectiveToUtc.HasValue && EffectiveToUtc.Value < EffectiveFromUtc.Value)
            throw new ArgumentException("Validity end cannot be earlier than validity start.", nameof(effectiveToUtc));
    }

    public DateTimeOffset? EffectiveFromUtc { get; }
    public DateTimeOffset? EffectiveToUtc { get; }
    public bool IsOpenEnded => EffectiveToUtc is null;
    public bool Contains(DateTimeOffset instantUtc)
    {
        var instant = NormalizeUtc(instantUtc, nameof(instantUtc));
        return (!EffectiveFromUtc.HasValue || instant >= EffectiveFromUtc.Value) && (!EffectiveToUtc.HasValue || instant <= EffectiveToUtc.Value);
    }

    private static DateTimeOffset? NormalizeUtc(DateTimeOffset? value, string parameterName)
        => value.HasValue ? NormalizeUtc(value.Value, parameterName) : null;

    private static DateTimeOffset NormalizeUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Knowledge validity instants must use UTC (zero offset).", parameterName);
        return value;
    }
}

public sealed record KnowledgeAuditMetadata
{
    public KnowledgeAuditMetadata(DateTimeOffset createdUtc, string? createdBy = null, DateTimeOffset? updatedUtc = null, string? updatedBy = null)
    {
        CreatedUtc = RequireUtc(createdUtc, nameof(createdUtc));
        CreatedBy = NormalizeActor(createdBy, nameof(createdBy));
        UpdatedUtc = updatedUtc.HasValue ? RequireUtc(updatedUtc.Value, nameof(updatedUtc)) : null;
        UpdatedBy = NormalizeActor(updatedBy, nameof(updatedBy));
        if (UpdatedUtc.HasValue && UpdatedUtc.Value < CreatedUtc)
            throw new ArgumentException("Updated time cannot be earlier than creation time.", nameof(updatedUtc));
        if (UpdatedUtc.HasValue != (UpdatedBy is not null))
            throw new ArgumentException("Updated time and updated actor must be provided together.", nameof(updatedBy));
    }

    public DateTimeOffset CreatedUtc { get; }
    public string? CreatedBy { get; }
    public DateTimeOffset? UpdatedUtc { get; }
    public string? UpdatedBy { get; }
    public static KnowledgeAuditMetadata Create(DateTimeOffset createdUtc, string? createdBy = null) => new(createdUtc, createdBy);

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        => value.Offset == TimeSpan.Zero ? value : throw new ArgumentException("Audit instants must use UTC (zero offset).", parameterName);

    private static string? NormalizeActor(string? value, string parameterName)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("Actor ID cannot be blank when provided.", parameterName);
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Actor ID must not contain control characters.", parameterName);
        return normalized;
    }
}

public sealed record KnowledgeTag
{
    private const int MaxLength = 64;

    public KnowledgeTag(string value)
    {
        Value = KnowledgeId.NormalizeToken(value, nameof(value), "Knowledge tag", MaxLength).ToLowerInvariant();
    }

    public string Value { get; }
    public override string ToString() => Value;
}
