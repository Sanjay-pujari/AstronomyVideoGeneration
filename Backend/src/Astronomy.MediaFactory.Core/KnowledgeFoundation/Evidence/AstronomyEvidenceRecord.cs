namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;

using Astronomy.MediaFactory.Core.KnowledgeFoundation;

public sealed class AstronomyEvidenceRecord : IEquatable<AstronomyEvidenceRecord>
{
    public const int MaxTitleLength = 256;
    public const int MaxSummaryLength = 1024;

    public AstronomyEvidenceRecord(
        EvidenceId id,
        AstronomyEvidenceType type,
        EvidenceFoundationStatus status,
        AstronomyEvidenceSourceReference source,
        EvidenceTemporalMetadata temporalMetadata,
        KnowledgeAuditMetadata audit,
        EvidenceAttribution? attribution = null,
        string? title = null,
        string? summary = null,
        IEnumerable<EvidenceExternalIdentifier>? externalIdentifiers = null,
        IEnumerable<KnowledgeTag>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("Evidence record ID is required.", nameof(id));

        Id = id;
        Type = EvidenceFoundationEnumGuard.RequireDefined(type, nameof(type));
        Status = EvidenceFoundationEnumGuard.RequireDefined(status, nameof(status));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        TemporalMetadata = temporalMetadata ?? throw new ArgumentNullException(nameof(temporalMetadata));
        Audit = audit ?? throw new ArgumentNullException(nameof(audit));
        Attribution = attribution ?? new EvidenceAttribution();
        Title = NormalizeOptionalText(title, nameof(title), MaxTitleLength, "Evidence title");
        Summary = NormalizeOptionalText(summary, nameof(summary), MaxSummaryLength, "Evidence summary");
        ExternalIdentifiers = CopyExternalIdentifiers(externalIdentifiers ?? []);
        Tags = CopyTags(tags ?? []);
    }

    public EvidenceId Id { get; }
    public AstronomyEvidenceType Type { get; }
    public EvidenceFoundationStatus Status { get; }
    public AstronomyEvidenceSourceReference Source { get; }
    public EvidenceAttribution Attribution { get; }
    public EvidenceTemporalMetadata TemporalMetadata { get; }
    public KnowledgeAuditMetadata Audit { get; }
    public string? Title { get; }
    public string? Summary { get; }
    public IReadOnlyList<EvidenceExternalIdentifier> ExternalIdentifiers { get; }
    public IReadOnlyList<KnowledgeTag> Tags { get; }

    public bool HasSameEvidenceIdentityAs(AstronomyEvidenceRecord other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Id == other.Id;
    }

    public bool Equals(AstronomyEvidenceRecord? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as AstronomyEvidenceRecord);
    public override int GetHashCode() => Id.GetHashCode();

    private static IReadOnlyList<EvidenceExternalIdentifier> CopyExternalIdentifiers(IEnumerable<EvidenceExternalIdentifier> identifiers)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        var ordered = identifiers
            .Select(identifier => identifier ?? throw new ArgumentException("Evidence external identifiers cannot contain null entries.", nameof(identifiers)))
            .OrderBy(identifier => identifier.Scheme, StringComparer.Ordinal)
            .ThenBy(identifier => identifier.Value, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Distinct().Count() != ordered.Length)
            throw new ArgumentException("Evidence external identifiers must be unique by scheme and value.", nameof(identifiers));
        return Array.AsReadOnly(ordered);
    }

    private static IReadOnlyList<KnowledgeTag> CopyTags(IEnumerable<KnowledgeTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var ordered = tags
            .Select(tag => tag ?? throw new ArgumentException("Evidence tags cannot contain null entries.", nameof(tags)))
            .OrderBy(tag => tag.Value, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Distinct().Count() != ordered.Length)
            throw new ArgumentException("Evidence tags must be unique.", nameof(tags));
        return Array.AsReadOnly(ordered);
    }

    private static string? NormalizeOptionalText(string? value, string parameterName, int maxLength, string displayName)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length == 0) return null;
        if (normalized.Length > maxLength)
            throw new ArgumentException($"{displayName} must be {maxLength} characters or fewer.", parameterName);
        if (normalized.Any(char.IsControl))
            throw new ArgumentException($"{displayName} must not contain control characters.", parameterName);
        return normalized;
    }
}
