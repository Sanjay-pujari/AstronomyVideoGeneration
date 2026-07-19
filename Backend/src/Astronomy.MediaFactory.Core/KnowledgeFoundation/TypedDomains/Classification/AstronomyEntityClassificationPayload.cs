using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

public sealed record AstronomyEntityClassificationPayload : ITypedAstronomyKnowledgePayload, IEquatable<AstronomyEntityClassificationPayload>
{
    public AstronomyEntityClassificationPayload(AstronomyKnowledgeTypeId typeId, AstronomyEntityKind subjectKind, IEnumerable<AstronomyClassificationAssignment> assignments)
    {
        if (!typeId.IsValid) throw new ArgumentException("Classification payload type ID is required.", nameof(typeId));
        TypeId = typeId;
        SubjectKind = EnumGuard.RequireDefined(subjectKind, nameof(subjectKind));
        Assignments = CopyAssignments(assignments);
    }

    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Classification;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.EntityClassification;
    public AstronomyKnowledgeTypeId TypeId { get; }
    public AstronomyEntityKind SubjectKind { get; }
    public IReadOnlyList<AstronomyClassificationAssignment> Assignments { get; }

    public bool Equals(AstronomyEntityClassificationPayload? other)
        => other is not null && TypeId == other.TypeId && SubjectKind == other.SubjectKind && Assignments.SequenceEqual(other.Assignments);
    public override int GetHashCode() => Assignments.Aggregate(HashCode.Combine(TypeId, SubjectKind), (hash, item) => HashCode.Combine(hash, item));

    private static IReadOnlyList<AstronomyClassificationAssignment> CopyAssignments(IEnumerable<AstronomyClassificationAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var ordered = assignments.Select(a => a ?? throw new ArgumentException("Classification assignments cannot contain null entries.", nameof(assignments)))
            .OrderBy(a => a.SchemeId.Value, StringComparer.Ordinal)
            .ThenBy(a => a.Qualifier)
            .ThenBy(a => a.Value.Code, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0) throw new ArgumentException("At least one classification assignment is required.", nameof(assignments));
        if (ordered.Distinct().Count() != ordered.Length) throw new ArgumentException("Classification assignments must be unique.", nameof(assignments));
        if (ordered.Where(a => a.Qualifier == AstronomyClassificationQualifier.Primary).GroupBy(a => a.SchemeId).Any(g => g.Count() > 1)) throw new ArgumentException("Only one primary classification assignment is allowed per scheme.", nameof(assignments));
        return Array.AsReadOnly(ordered);
    }
}
