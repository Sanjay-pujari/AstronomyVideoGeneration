namespace Astronomy.MediaFactory.Core.KnowledgeFoundation;

/// <summary>
/// Marker contract for structured, content-neutral astronomy knowledge payloads.
/// </summary>
public interface IAstronomyKnowledgePayload
{
}

public sealed record KnowledgeLocalizationReference
{
    private const int MaxResourceKeyLength = 256;

    public KnowledgeLocalizationReference(KnowledgeLanguageTag languageTag, string resourceKey, bool isOriginalTerm = false, bool isCanonicalLabel = false)
    {
        ArgumentNullException.ThrowIfNull(languageTag);

        LanguageTag = languageTag;
        ResourceKey = KnowledgeId.NormalizeToken(resourceKey, nameof(resourceKey), "Knowledge localization resource key", MaxResourceKeyLength);
        IsOriginalTerm = isOriginalTerm;
        IsCanonicalLabel = isCanonicalLabel;
    }

    public KnowledgeLanguageTag LanguageTag { get; }
    public string ResourceKey { get; }
    public bool IsOriginalTerm { get; }
    public bool IsCanonicalLabel { get; }
}

public interface IAstronomyKnowledgeStatement
{
    KnowledgeId Id { get; }
    KnowledgeVersion Version { get; }
    KnowledgeStatementKind Kind { get; }
    KnowledgeFoundationStatus Status { get; }
    AstronomyEntityReference PrimarySubject { get; }
    AstronomyFamilyReference? FamilyContext { get; }
    IAstronomyKnowledgePayload Payload { get; }
    IReadOnlyList<KnowledgeLocalizationReference> LocalizationReferences { get; }
    IReadOnlyList<KnowledgeTag> Tags { get; }
    KnowledgeValidityRange Validity { get; }
    KnowledgeAuditMetadata Audit { get; }
}

public interface IAstronomyKnowledgeStatement<out TPayload> : IAstronomyKnowledgeStatement
    where TPayload : IAstronomyKnowledgePayload
{
    new TPayload Payload { get; }
}

public sealed class AstronomyKnowledgeStatement<TPayload> : IAstronomyKnowledgeStatement<TPayload>, IEquatable<AstronomyKnowledgeStatement<TPayload>>
    where TPayload : IAstronomyKnowledgePayload
{
    public AstronomyKnowledgeStatement(
        KnowledgeId id,
        KnowledgeVersion version,
        KnowledgeStatementKind kind,
        KnowledgeFoundationStatus status,
        AstronomyEntityReference primarySubject,
        TPayload payload,
        KnowledgeAuditMetadata audit,
        AstronomyFamilyReference? familyContext = null,
        IEnumerable<KnowledgeLocalizationReference>? localizationReferences = null,
        IEnumerable<KnowledgeTag>? tags = null,
        KnowledgeValidityRange? validity = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("Knowledge statement ID is required.", nameof(id));
        if (version.Revision < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Knowledge statement version must be positive.");

        Id = id;
        Version = version;
        Kind = KnowledgeFoundationEnumGuard.RequireDefined(kind, nameof(kind));
        Status = KnowledgeFoundationEnumGuard.RequireDefined(status, nameof(status));
        PrimarySubject = primarySubject ?? throw new ArgumentNullException(nameof(primarySubject));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Audit = audit ?? throw new ArgumentNullException(nameof(audit));
        FamilyContext = familyContext;
        Validity = validity ?? new KnowledgeValidityRange();
        LocalizationReferences = CopyLocalizationReferences(localizationReferences ?? []);
        Tags = CopyTags(tags ?? []);
    }

    public KnowledgeId Id { get; }
    public KnowledgeVersion Version { get; }
    public KnowledgeStatementKind Kind { get; }
    public KnowledgeFoundationStatus Status { get; }
    public AstronomyEntityReference PrimarySubject { get; }
    public AstronomyFamilyReference? FamilyContext { get; }
    public TPayload Payload { get; }
    IAstronomyKnowledgePayload IAstronomyKnowledgeStatement.Payload => Payload;
    public IReadOnlyList<KnowledgeLocalizationReference> LocalizationReferences { get; }
    public IReadOnlyList<KnowledgeTag> Tags { get; }
    public KnowledgeValidityRange Validity { get; }
    public KnowledgeAuditMetadata Audit { get; }
    public bool HasSameVersionIdentityAs(IAstronomyKnowledgeStatement other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Id == other.Id && Version == other.Version;
    }

    public bool Equals(AstronomyKnowledgeStatement<TPayload>? other)
        => other is not null && Id == other.Id && Version == other.Version;

    public override bool Equals(object? obj) => Equals(obj as AstronomyKnowledgeStatement<TPayload>);
    public override int GetHashCode() => HashCode.Combine(Id, Version);

    private static IReadOnlyList<KnowledgeLocalizationReference> CopyLocalizationReferences(IEnumerable<KnowledgeLocalizationReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        var ordered = references
            .Select(reference => reference ?? throw new ArgumentException("Localization references cannot contain null entries.", nameof(references)))
            .OrderBy(reference => reference.LanguageTag.Value, StringComparer.Ordinal)
            .ThenBy(reference => reference.ResourceKey, StringComparer.Ordinal)
            .ThenBy(reference => reference.IsOriginalTerm)
            .ThenBy(reference => reference.IsCanonicalLabel)
            .ToArray();

        if (ordered.GroupBy(reference => new { reference.LanguageTag, reference.ResourceKey }).Any(group => group.Count() > 1))
            throw new ArgumentException("Localization references must be unique by language tag and resource key.", nameof(references));

        return Array.AsReadOnly(ordered);
    }

    private static IReadOnlyList<KnowledgeTag> CopyTags(IEnumerable<KnowledgeTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var ordered = tags
            .Select(tag => tag ?? throw new ArgumentException("Knowledge tags cannot contain null entries.", nameof(tags)))
            .OrderBy(tag => tag.Value, StringComparer.Ordinal)
            .ToArray();

        if (ordered.Distinct().Count() != ordered.Length)
            throw new ArgumentException("Knowledge tags must be unique.", nameof(tags));

        return Array.AsReadOnly(ordered);
    }
}
