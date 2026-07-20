using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

public sealed record AstronomyEntityClassificationPayload : ITypedAstronomyKnowledgePayload, IEquatable<AstronomyEntityClassificationPayload>
{
    [JsonConstructor]
    public AstronomyEntityClassificationPayload(
        AstronomyKnowledgeTypeId typeId,
        AstronomyEntityKind subjectKind,
        IReadOnlyList<AstronomyClassificationAssignment> assignments)
    {
        if (!typeId.IsValid)
        {
            throw new ArgumentException("Classification payload type ID is required.", nameof(typeId));
        }

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
    {
        return other is not null
            && TypeId == other.TypeId
            && SubjectKind == other.SubjectKind
            && Assignments.SequenceEqual(other.Assignments);
    }

    public override int GetHashCode()
    {
        return Assignments.Aggregate(
            HashCode.Combine(TypeId, SubjectKind),
            (hash, item) => HashCode.Combine(hash, item));
    }

    private static IReadOnlyList<AstronomyClassificationAssignment> CopyAssignments(
        IEnumerable<AstronomyClassificationAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        var ordered = assignments
            .Select(assignment => assignment ?? throw new ArgumentException(
                "Classification assignments cannot contain null entries.",
                nameof(assignments)))
            .OrderBy(assignment => assignment.SchemeId.Value, StringComparer.Ordinal)
            .ThenBy(assignment => assignment.Qualifier)
            .ThenBy(assignment => assignment.Value.Code, StringComparer.Ordinal)
            .ToArray();

        if (ordered.Length == 0)
        {
            throw new ArgumentException("At least one classification assignment is required.", nameof(assignments));
        }

        if (ordered
            .GroupBy(assignment => new
            {
                assignment.SchemeId,
                assignment.Value.Code,
                assignment.Qualifier
            })
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Classification assignments must be unique by scheme, classification code, and qualifier.",
                nameof(assignments));
        }

        if (ordered
            .Where(assignment => assignment.Qualifier == AstronomyClassificationQualifier.Primary)
            .GroupBy(assignment => assignment.SchemeId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Only one primary classification assignment is allowed per scheme.",
                nameof(assignments));
        }

        return Array.AsReadOnly(ordered);
    }
}
