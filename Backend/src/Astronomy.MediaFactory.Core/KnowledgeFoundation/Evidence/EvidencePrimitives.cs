namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;

using Astronomy.MediaFactory.Core.KnowledgeFoundation;

public enum AstronomyEvidenceType
{
    Observation,
    Measurement,
    CatalogRecord,
    Ephemeris,
    ResearchPublication,
    ReferencePublication,
    InstitutionalDataset,
    HistoricalRecord,
    ExpertAssessment,
    DerivedAnalysis
}

public enum AstronomyEvidenceSourceType
{
    Observatory,
    SpaceAgency,
    ResearchInstitution,
    AcademicPublication,
    ScientificCatalog,
    EphemerisService,
    Instrument,
    Researcher,
    HistoricalArchive,
    EducationalInstitution,
    Other
}

public enum EvidenceFoundationStatus
{
    Draft,
    Verified,
    Disputed,
    Superseded,
    Withdrawn,
    Archived
}

public static class EvidenceFoundationEnumGuard
{
    public static AstronomyEvidenceType RequireDefined(AstronomyEvidenceType type, string parameterName = "type")
        => Enum.IsDefined(type) ? type : throw new ArgumentOutOfRangeException(parameterName, type, "Evidence type is not defined.");

    public static AstronomyEvidenceSourceType RequireDefined(AstronomyEvidenceSourceType sourceType, string parameterName = "sourceType")
        => Enum.IsDefined(sourceType) ? sourceType : throw new ArgumentOutOfRangeException(parameterName, sourceType, "Evidence source type is not defined.");

    public static EvidenceFoundationStatus RequireDefined(EvidenceFoundationStatus status, string parameterName = "status")
        => Enum.IsDefined(status) ? status : throw new ArgumentOutOfRangeException(parameterName, status, "Evidence status is not defined.");

    public static KnowledgeEvidenceRole RequireDefined(KnowledgeEvidenceRole role, string parameterName = "role")
        => Enum.IsDefined(role) ? role : throw new ArgumentOutOfRangeException(parameterName, role, "Knowledge evidence role is not defined.");
}

public readonly record struct EvidenceId
{
    public const int MaxLength = 256;

    public EvidenceId(string value)
    {
        Value = KnowledgeId.NormalizeToken(value, nameof(value), "Evidence ID", MaxLength);
    }

    public string Value { get; }
    public override string ToString() => Value ?? string.Empty;
    public static EvidenceId Create(string value) => new(value);
    public static bool TryParse(string? value, out EvidenceId id)
    {
        try
        {
            id = new EvidenceId(value!);
            return true;
        }
        catch (ArgumentException)
        {
            id = default;
            return false;
        }
    }
}

public sealed record EvidenceExternalIdentifier
{
    public const int MaxSchemeLength = 64;
    public const int MaxValueLength = 512;

    public EvidenceExternalIdentifier(string scheme, string value)
    {
        Scheme = NormalizeScheme(scheme);
        Value = NormalizeValue(value);
    }

    public string Scheme { get; }
    public string Value { get; }
    public override string ToString() => $"{Scheme}:{Value}";

    private static string NormalizeScheme(string scheme)
    {
        var normalized = KnowledgeId.NormalizeToken(scheme, nameof(scheme), "Evidence external identifier scheme", MaxSchemeLength).ToLowerInvariant();
        if (normalized.Any(static c => c is ':' or '/'))
            throw new ArgumentException("Evidence external identifier scheme must be a token, not a URI.", nameof(scheme));
        return normalized;
    }

    private static string NormalizeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Evidence external identifier value is required.", nameof(value));
        var normalized = value.Trim();
        if (normalized.Length > MaxValueLength)
            throw new ArgumentException($"Evidence external identifier value must be {MaxValueLength} characters or fewer.", nameof(value));
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Evidence external identifier value must not contain control characters.", nameof(value));
        return normalized;
    }
}

public sealed record AstronomyEvidenceSourceReference
{
    public const int MaxSourceIdLength = 256;
    public const int MaxDisplayNameLength = 256;

    public AstronomyEvidenceSourceReference(
        string sourceId,
        AstronomyEvidenceSourceType sourceType,
        string displayName,
        Uri? canonicalUri = null,
        EvidenceExternalIdentifier? externalIdentifier = null)
    {
        SourceId = KnowledgeId.NormalizeToken(sourceId, nameof(sourceId), "Evidence source ID", MaxSourceIdLength);
        SourceType = EvidenceFoundationEnumGuard.RequireDefined(sourceType, nameof(sourceType));
        DisplayName = NormalizeDisplayName(displayName);
        CanonicalUri = NormalizeUri(canonicalUri);
        ExternalIdentifier = externalIdentifier;
    }

    public string SourceId { get; }
    public AstronomyEvidenceSourceType SourceType { get; }
    public string DisplayName { get; }
    public Uri? CanonicalUri { get; }
    public EvidenceExternalIdentifier? ExternalIdentifier { get; }

    private static string NormalizeDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Evidence source display name is required.", nameof(displayName));
        var normalized = displayName.Trim();
        if (normalized.Length > MaxDisplayNameLength)
            throw new ArgumentException($"Evidence source display name must be {MaxDisplayNameLength} characters or fewer.", nameof(displayName));
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Evidence source display name must not contain control characters.", nameof(displayName));
        return normalized;
    }

    private static Uri? NormalizeUri(Uri? uri)
    {
        if (uri is null) return null;
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("Evidence source canonical URI must be absolute.", nameof(uri));
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Evidence source canonical URI must not contain credentials.", nameof(uri));
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Evidence source canonical URI must use HTTPS.", nameof(uri));
        return new Uri(uri.AbsoluteUri, UriKind.Absolute);
    }
}

public sealed record EvidenceAttribution : IEquatable<EvidenceAttribution>
{
    public const int MaxContributorLength = 256;
    public const int MaxOptionalTextLength = 512;

    private readonly IReadOnlyList<string> _contributors;

    public EvidenceAttribution(
        IEnumerable<string>? contributors = null,
        string? organizationName = null,
        string? publisherName = null,
        string? publicationTitle = null,
        string? editionOrVersion = null,
        string? displayCitation = null)
    {
        _contributors = CopyContributors(contributors ?? []);
        Contributors = _contributors;
        OrganizationName = NormalizeOptionalText(organizationName, nameof(organizationName));
        PublisherName = NormalizeOptionalText(publisherName, nameof(publisherName));
        PublicationTitle = NormalizeOptionalText(publicationTitle, nameof(publicationTitle));
        EditionOrVersion = NormalizeOptionalText(editionOrVersion, nameof(editionOrVersion));
        DisplayCitation = NormalizeOptionalText(displayCitation, nameof(displayCitation));
    }

    public IReadOnlyList<string> Contributors { get; }
    public string? OrganizationName { get; }
    public string? PublisherName { get; }
    public string? PublicationTitle { get; }
    public string? EditionOrVersion { get; }
    public string? DisplayCitation { get; }

    public bool Equals(EvidenceAttribution? other)
        => other is not null
        && Contributors.SequenceEqual(other.Contributors, StringComparer.Ordinal)
        && string.Equals(OrganizationName, other.OrganizationName, StringComparison.Ordinal)
        && string.Equals(PublisherName, other.PublisherName, StringComparison.Ordinal)
        && string.Equals(PublicationTitle, other.PublicationTitle, StringComparison.Ordinal)
        && string.Equals(EditionOrVersion, other.EditionOrVersion, StringComparison.Ordinal)
        && string.Equals(DisplayCitation, other.DisplayCitation, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var contributor in Contributors) hash.Add(contributor, StringComparer.Ordinal);
        hash.Add(OrganizationName, StringComparer.Ordinal);
        hash.Add(PublisherName, StringComparer.Ordinal);
        hash.Add(PublicationTitle, StringComparer.Ordinal);
        hash.Add(EditionOrVersion, StringComparer.Ordinal);
        hash.Add(DisplayCitation, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static IReadOnlyList<string> CopyContributors(IEnumerable<string> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        var copy = contributors.Select(NormalizeContributor).ToArray();
        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Evidence attribution contributors must not contain exact duplicates.", nameof(contributors));
        return Array.AsReadOnly(copy);
    }

    private static string NormalizeContributor(string contributor)
    {
        if (string.IsNullOrWhiteSpace(contributor))
            throw new ArgumentException("Evidence attribution contributor cannot be blank.", nameof(contributor));
        var normalized = contributor.Trim();
        if (normalized.Length > MaxContributorLength)
            throw new ArgumentException($"Evidence attribution contributor must be {MaxContributorLength} characters or fewer.", nameof(contributor));
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Evidence attribution contributor must not contain control characters.", nameof(contributor));
        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, string parameterName)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length == 0) return null;
        if (normalized.Length > MaxOptionalTextLength)
            throw new ArgumentException($"Evidence attribution value must be {MaxOptionalTextLength} characters or fewer.", parameterName);
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Evidence attribution value must not contain control characters.", parameterName);
        return normalized;
    }
}

public sealed record EvidenceTemporalMetadata
{
    public EvidenceTemporalMetadata(
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? publishedAtUtc = null,
        DateTimeOffset? retrievedAtUtc = null,
        KnowledgeValidityRange? applicability = null)
    {
        ObservedAtUtc = RequireUtc(observedAtUtc, nameof(observedAtUtc));
        PublishedAtUtc = RequireUtc(publishedAtUtc, nameof(publishedAtUtc));
        RetrievedAtUtc = RequireUtc(retrievedAtUtc, nameof(retrievedAtUtc));
        Applicability = applicability ?? new KnowledgeValidityRange();
        if (PublishedAtUtc.HasValue && RetrievedAtUtc.HasValue && RetrievedAtUtc.Value < PublishedAtUtc.Value)
            throw new ArgumentException("Evidence retrieval time cannot be earlier than publication time.", nameof(retrievedAtUtc));
    }

    public DateTimeOffset? ObservedAtUtc { get; }
    public DateTimeOffset? PublishedAtUtc { get; }
    public DateTimeOffset? RetrievedAtUtc { get; }
    public KnowledgeValidityRange Applicability { get; }

    private static DateTimeOffset? RequireUtc(DateTimeOffset? value, string parameterName)
        => value.HasValue ? RequireUtc(value.Value, parameterName) : null;

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        => value.Offset == TimeSpan.Zero ? value : throw new ArgumentException("Evidence temporal instants must use UTC (zero offset).", parameterName);
}
