using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation;

public enum KnowledgeFoundationStatus
{
    Draft,
    Candidate,
    Accepted,
    Deprecated,
    Archived
}

public enum KnowledgeStatementKind
{
    General,
    Identity,
    Classification,
    Observation,
    Education,
    Terminology,
    Safety,
    Visualization
}

public readonly record struct KnowledgeId
{
    public KnowledgeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Knowledge ID is required.", nameof(value));

        var normalized = value.Trim();
        if (normalized.Any(char.IsWhiteSpace))
            throw new ArgumentException("Knowledge ID must not contain whitespace.", nameof(value));

        Value = normalized;
    }

    public string Value { get; }
    public override string ToString() => Value;
    public static KnowledgeId Create(string value) => new(value);
}

public readonly record struct KnowledgeVersion
{
    public KnowledgeVersion(int major, int minor = 0, int patch = 0)
    {
        if (major < 1)
            throw new ArgumentOutOfRangeException(nameof(major), "Major version must be at least 1.");
        if (minor < 0)
            throw new ArgumentOutOfRangeException(nameof(minor), "Minor version cannot be negative.");
        if (patch < 0)
            throw new ArgumentOutOfRangeException(nameof(patch), "Patch version cannot be negative.");

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
    public static KnowledgeVersion Initial { get; } = new(1, 0, 0);
}

public sealed record AstronomyEntityReference(
    string EntityId,
    AstronomyEntityKind? EntityKind = null,
    string? CanonicalName = null)
{
    public string EntityId { get; init; } = RequireToken(EntityId, nameof(EntityId));

    private static string RequireToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Astronomy entity reference ID is required.", parameterName);

        return value.Trim();
    }
}

public sealed record AstronomyFamilyReference(
    string FamilyId,
    AstronomyFamilyKind? FamilyKind = null)
{
    public string FamilyId { get; init; } = RequireToken(FamilyId, nameof(FamilyId));

    private static string RequireToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Astronomy family reference ID is required.", parameterName);

        return value.Trim();
    }
}

public sealed record KnowledgeLanguageTag
{
    public KnowledgeLanguageTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Language tag is required.", nameof(value));

        Value = value.Trim();
    }

    public string Value { get; }
    public override string ToString() => Value;
    public static KnowledgeLanguageTag Invariant { get; } = new("und");
}

public sealed record KnowledgeValidityRange(DateTimeOffset? EffectiveFromUtc = null, DateTimeOffset? EffectiveToUtc = null)
{
    public bool IsOpenEnded => EffectiveToUtc is null;
    public bool Contains(DateTimeOffset instantUtc) => (!EffectiveFromUtc.HasValue || instantUtc >= EffectiveFromUtc.Value) && (!EffectiveToUtc.HasValue || instantUtc <= EffectiveToUtc.Value);
}

public sealed record KnowledgeAuditMetadata(
    DateTimeOffset CreatedUtc,
    string? CreatedBy = null,
    DateTimeOffset? UpdatedUtc = null,
    string? UpdatedBy = null)
{
    public static KnowledgeAuditMetadata Create(DateTimeOffset createdUtc, string? createdBy = null) => new(createdUtc, createdBy);
}

public sealed record KnowledgeTag(string Value)
{
    public string Value { get; init; } = Normalize(Value);
    public override string ToString() => Value;

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Knowledge tag is required.", nameof(value));

        return value.Trim();
    }
}
