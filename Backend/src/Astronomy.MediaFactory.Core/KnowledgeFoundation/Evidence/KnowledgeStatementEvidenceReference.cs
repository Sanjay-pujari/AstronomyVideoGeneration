namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;

using Astronomy.MediaFactory.Core.KnowledgeFoundation;

public sealed record KnowledgeStatementEvidenceReference
{
    public const int MaxNoteLength = 512;

    public KnowledgeStatementEvidenceReference(KnowledgeId knowledgeId, KnowledgeVersion knowledgeVersion, EvidenceId evidenceId, KnowledgeEvidenceRole role, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(knowledgeId.Value)) throw new ArgumentException("Knowledge ID is required.", nameof(knowledgeId));
        if (knowledgeVersion.Revision < 1) throw new ArgumentOutOfRangeException(nameof(knowledgeVersion), knowledgeVersion, "Knowledge version must be positive.");
        if (string.IsNullOrWhiteSpace(evidenceId.Value)) throw new ArgumentException("Evidence ID is required.", nameof(evidenceId));

        KnowledgeId = knowledgeId;
        KnowledgeVersion = knowledgeVersion;
        EvidenceId = evidenceId;
        Role = EvidenceFoundationEnumGuard.RequireDefined(role, nameof(role));
        Note = NormalizeNote(note);
    }

    public KnowledgeId KnowledgeId { get; }
    public KnowledgeVersion KnowledgeVersion { get; }
    public EvidenceId EvidenceId { get; }
    public KnowledgeEvidenceRole Role { get; }
    public string? Note { get; }

    private static string? NormalizeNote(string? note)
    {
        if (note is null) return null;
        var normalized = note.Trim();
        if (normalized.Length == 0) return null;
        if (normalized.Length > MaxNoteLength) throw new ArgumentException($"Evidence association note must be {MaxNoteLength} characters or fewer.", nameof(note));
        if (normalized.Any(char.IsControl)) throw new ArgumentException("Evidence association note must not contain control characters.", nameof(note));
        return normalized;
    }
}
