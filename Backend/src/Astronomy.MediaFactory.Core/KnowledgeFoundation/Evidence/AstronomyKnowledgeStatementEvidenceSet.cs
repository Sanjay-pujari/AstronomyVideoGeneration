namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;

using Astronomy.MediaFactory.Core.KnowledgeFoundation;

public sealed class AstronomyKnowledgeStatementEvidenceSet : IEquatable<AstronomyKnowledgeStatementEvidenceSet>
{
    public AstronomyKnowledgeStatementEvidenceSet(KnowledgeId knowledgeId, KnowledgeVersion knowledgeVersion, IEnumerable<KnowledgeStatementEvidenceReference> associations)
    {
        if (string.IsNullOrWhiteSpace(knowledgeId.Value)) throw new ArgumentException("Knowledge ID is required.", nameof(knowledgeId));
        if (knowledgeVersion.Revision < 1) throw new ArgumentOutOfRangeException(nameof(knowledgeVersion), knowledgeVersion, "Knowledge version must be positive.");

        KnowledgeId = knowledgeId;
        KnowledgeVersion = knowledgeVersion;
        Associations = CopyAssociations(associations);
    }

    public KnowledgeId KnowledgeId { get; }
    public KnowledgeVersion KnowledgeVersion { get; }
    public IReadOnlyList<KnowledgeStatementEvidenceReference> Associations { get; }

    public bool HasSameStatementVersionAs(IAstronomyKnowledgeStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return KnowledgeId == statement.Id && KnowledgeVersion == statement.Version;
    }

    public bool Equals(AstronomyKnowledgeStatementEvidenceSet? other)
        => other is not null && KnowledgeId == other.KnowledgeId && KnowledgeVersion == other.KnowledgeVersion && Associations.SequenceEqual(other.Associations);
    public override bool Equals(object? obj) => Equals(obj as AstronomyKnowledgeStatementEvidenceSet);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(KnowledgeId); hash.Add(KnowledgeVersion);
        foreach (var association in Associations) hash.Add(association);
        return hash.ToHashCode();
    }

    private IReadOnlyList<KnowledgeStatementEvidenceReference> CopyAssociations(IEnumerable<KnowledgeStatementEvidenceReference> associations)
    {
        ArgumentNullException.ThrowIfNull(associations);
        var ordered = associations
            .Select(association => association ?? throw new ArgumentException("Evidence associations cannot contain null entries.", nameof(associations)))
            .Select(RequireOwnedAssociation)
            .OrderBy(association => association.EvidenceId.Value, StringComparer.Ordinal)
            .ThenBy(association => association.Role.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (ordered.GroupBy(association => association.EvidenceId).Any(group => group.Count() > 1))
            throw new ArgumentException("A statement evidence set may contain each evidence ID only once.", nameof(associations));
        if (ordered.Count(association => association.Role == KnowledgeEvidenceRole.Primary) > 1)
            throw new ArgumentException("A statement evidence set may contain at most one primary evidence association.", nameof(associations));
        return Array.AsReadOnly(ordered);
    }

    private KnowledgeStatementEvidenceReference RequireOwnedAssociation(KnowledgeStatementEvidenceReference association)
    {
        if (association.KnowledgeId != KnowledgeId)
            throw new ArgumentException("Evidence association knowledge ID must match the evidence set owner.", nameof(association));
        if (association.KnowledgeVersion != KnowledgeVersion)
            throw new ArgumentException("Evidence association knowledge version must match the evidence set owner.", nameof(association));
        return association;
    }
}
