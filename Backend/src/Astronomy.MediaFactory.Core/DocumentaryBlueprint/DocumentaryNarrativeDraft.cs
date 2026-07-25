using System.Globalization;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Identifies the narrative function of an externally authored passage.</summary>
public enum DocumentaryNarrativePassageType { Opening, Question, Orientation, Discovery, Explanation, Evidence, Context, Clarification, Observation, Guidance, Reflection, Transition, Closing }

/// <summary>Externally supplied provenance for a narrative draft.</summary>
public sealed record DocumentaryNarrativeDraftMetadata
{
    public DocumentaryNarrativeDraftMetadata(DateTimeOffset createdUtc, string createdBy, string narrativeModelVersion,
        string compositionVersion, string compositionSchemaVersion, string draftSchemaVersion, string correlationId)
    {
        CreatedUtc = createdUtc != default ? createdUtc : throw new ArgumentException("A non-default creation timestamp is required.", nameof(createdUtc));
        CreatedBy = Guard.Required(createdBy, nameof(createdBy)); NarrativeModelVersion = Guard.Required(narrativeModelVersion, nameof(narrativeModelVersion));
        CompositionVersion = Guard.Required(compositionVersion, nameof(compositionVersion)); CompositionSchemaVersion = Guard.Required(compositionSchemaVersion, nameof(compositionSchemaVersion));
        DraftSchemaVersion = draftSchemaVersion == "1.0" ? draftSchemaVersion : throw new ArgumentException("Draft schema version must be 1.0.", nameof(draftSchemaVersion));
        CorrelationId = Guard.Required(correlationId, nameof(correlationId));
    }
    public DateTimeOffset CreatedUtc { get; } public string CreatedBy { get; } public string NarrativeModelVersion { get; }
    public string CompositionVersion { get; } public string CompositionSchemaVersion { get; } public string DraftSchemaVersion { get; } public string CorrelationId { get; }
}

/// <summary>Externally authored text for exactly one composition beat.</summary>
public sealed record DocumentaryNarrativePassageInput
{
    public DocumentaryNarrativePassageInput(string sourceBeatId, string text) { SourceBeatId = Guard.Required(sourceBeatId, nameof(sourceBeatId)); Text = Guard.Required(text, nameof(text)); }
    public string SourceBeatId { get; } public string Text { get; }
}

/// <summary>The smallest immutable unit of draft narrative.</summary>
public sealed class DocumentaryNarrativePassage
{
    public DocumentaryNarrativePassage(string passageId, int passageNumber, string sourceBeatId, int sourceBeatNumber,
        string sourceSceneId, int sourceSceneNumber, string title, DocumentaryNarrativePassageType passageType,
        DocumentaryNarrativeStage narrativeStage, DocumentarySceneRole sceneRole, ViewerQuestion viewerQuestion,
        string purpose, string text, IReadOnlyList<KnowledgeReference> knowledgeReferences,
        IReadOnlyList<VisualOpportunity> visualOpportunities, SceneTransition transition,
        EditorialOutcome editorialOutcome, int estimatedDurationSeconds)
    {
        PassageId = Guard.Required(passageId, nameof(passageId)); SourceBeatId = Guard.Required(sourceBeatId, nameof(sourceBeatId)); SourceSceneId = Guard.Required(sourceSceneId, nameof(sourceSceneId));
        Title = Guard.Required(title, nameof(title)); Purpose = Guard.Required(purpose, nameof(purpose)); Text = Guard.Required(text, nameof(text));
        Guard.Enum(passageType, nameof(passageType)); Guard.Enum(narrativeStage, nameof(narrativeStage)); Guard.Enum(sceneRole, nameof(sceneRole));
        if (estimatedDurationSeconds < 0) throw new ArgumentOutOfRangeException(nameof(estimatedDurationSeconds));
        PassageNumber = passageNumber; SourceBeatNumber = sourceBeatNumber; SourceSceneNumber = sourceSceneNumber; PassageType = passageType; NarrativeStage = narrativeStage; SceneRole = sceneRole;
        ViewerQuestion = viewerQuestion ?? throw new ArgumentNullException(nameof(viewerQuestion)); KnowledgeReferences = Guard.Copy(knowledgeReferences, nameof(knowledgeReferences));
        VisualOpportunities = Guard.Copy(visualOpportunities, nameof(visualOpportunities)); Transition = transition ?? throw new ArgumentNullException(nameof(transition));
        EditorialOutcome = editorialOutcome ?? throw new ArgumentNullException(nameof(editorialOutcome)); EstimatedDurationSeconds = estimatedDurationSeconds;
    }
    public string PassageId { get; } public int PassageNumber { get; } public string SourceBeatId { get; } public int SourceBeatNumber { get; }
    public string SourceSceneId { get; } public int SourceSceneNumber { get; } public string Title { get; } public DocumentaryNarrativePassageType PassageType { get; }
    public DocumentaryNarrativeStage NarrativeStage { get; } public DocumentarySceneRole SceneRole { get; } public ViewerQuestion ViewerQuestion { get; }
    public string Purpose { get; } public string Text { get; } public IReadOnlyList<KnowledgeReference> KnowledgeReferences { get; }
    public IReadOnlyList<VisualOpportunity> VisualOpportunities { get; } public SceneTransition Transition { get; } public EditorialOutcome EditorialOutcome { get; }
    public int EstimatedDurationSeconds { get; }
}

/// <summary>An immutable ordered section of a narrative draft.</summary>
public sealed class DocumentaryNarrativeDraftSection
{
    public DocumentaryNarrativeDraftSection(string sectionId, int sectionNumber, string sourceCompositionSectionId, string title, string purpose,
        DocumentaryNarrativeStage narrativeStage, DocumentaryNarrativeSectionRole sectionRole, IReadOnlyList<DocumentaryNarrativePassage> passages, int estimatedDurationSeconds)
    {
        SectionId = Guard.Required(sectionId, nameof(sectionId)); SourceCompositionSectionId = Guard.Required(sourceCompositionSectionId, nameof(sourceCompositionSectionId));
        Title = Guard.Required(title, nameof(title)); Purpose = Guard.Required(purpose, nameof(purpose)); Guard.Enum(narrativeStage, nameof(narrativeStage)); Guard.Enum(sectionRole, nameof(sectionRole));
        if (estimatedDurationSeconds < 0) throw new ArgumentOutOfRangeException(nameof(estimatedDurationSeconds));
        SectionNumber = sectionNumber; NarrativeStage = narrativeStage; SectionRole = sectionRole; Passages = Guard.Copy(passages, nameof(passages)); EstimatedDurationSeconds = estimatedDurationSeconds;
        if (Passages.Select(x => x.PassageId).Distinct(StringComparer.Ordinal).Count() != Passages.Count) throw new ArgumentException("Passage IDs must be unique.", nameof(passages));
        if (Passages.Select(x => x.PassageNumber).Distinct().Count() != Passages.Count) throw new ArgumentException("Passage numbers must be unique.", nameof(passages));
    }
    public string SectionId { get; } public int SectionNumber { get; } public string SourceCompositionSectionId { get; } public string Title { get; } public string Purpose { get; }
    public DocumentaryNarrativeStage NarrativeStage { get; } public DocumentaryNarrativeSectionRole SectionRole { get; } public IReadOnlyList<DocumentaryNarrativePassage> Passages { get; }
    public int EstimatedDurationSeconds { get; }
}

/// <summary>The immutable, ordered narrative-draft aggregate.</summary>
public sealed class DocumentaryNarrativeDraft
{
    public DocumentaryNarrativeDraft(string draftId, string compositionId, string blueprintId, string knowledgeId, string subjectId, string subjectName,
        BlueprintPublicationFormat publicationFormat, string primaryLanguage, string version, DocumentaryNarrativeDraftMetadata metadata, IReadOnlyList<DocumentaryNarrativeDraftSection> sections)
    {
        DraftId = Guard.Required(draftId, nameof(draftId)); CompositionId = Guard.Required(compositionId, nameof(compositionId)); BlueprintId = Guard.Required(blueprintId, nameof(blueprintId)); KnowledgeId = Guard.Required(knowledgeId, nameof(knowledgeId));
        SubjectId = Guard.Required(subjectId, nameof(subjectId)); SubjectName = Guard.Required(subjectName, nameof(subjectName)); PrimaryLanguage = Guard.Required(primaryLanguage, nameof(primaryLanguage)); Version = Guard.Required(version, nameof(version));
        Guard.Enum(publicationFormat, nameof(publicationFormat)); PublicationFormat = publicationFormat; Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata)); Sections = Guard.Copy(sections, nameof(sections));
        if (Sections.Select(x => x.SectionId).Distinct(StringComparer.Ordinal).Count() != Sections.Count) throw new ArgumentException("Section IDs must be unique.", nameof(sections));
        if (Sections.Select(x => x.SectionNumber).Distinct().Count() != Sections.Count) throw new ArgumentException("Section numbers must be unique.", nameof(sections));
    }
    public string DraftId { get; } public string CompositionId { get; } public string BlueprintId { get; } public string KnowledgeId { get; } public string SubjectId { get; } public string SubjectName { get; }
    public BlueprintPublicationFormat PublicationFormat { get; } public string PrimaryLanguage { get; } public string Version { get; } public DocumentaryNarrativeDraftMetadata Metadata { get; }
    public IReadOnlyList<DocumentaryNarrativeDraftSection> Sections { get; }
}

/// <summary>Immutable input to the deterministic draft assembler.</summary>
public sealed class DocumentaryNarrativeDraftRequest
{
    public DocumentaryNarrativeDraftRequest(string draftId, string version, DocumentaryNarrativeDraftMetadata metadata, DocumentaryNarrativeComposition composition, IReadOnlyList<DocumentaryNarrativePassageInput> passageInputs)
    {
        DraftId = Guard.Required(draftId, nameof(draftId)); Version = Guard.Required(version, nameof(version)); Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata)); Composition = composition ?? throw new ArgumentNullException(nameof(composition));
        PassageInputs = Guard.Copy(passageInputs, nameof(passageInputs));
        if (PassageInputs.Select(x => x.SourceBeatId).Distinct(StringComparer.Ordinal).Count() != PassageInputs.Count) throw new ArgumentException("Source beat IDs must be unique.", nameof(passageInputs));
    }
    public string DraftId { get; } public string Version { get; } public DocumentaryNarrativeDraftMetadata Metadata { get; } public DocumentaryNarrativeComposition Composition { get; }
    public IReadOnlyList<DocumentaryNarrativePassageInput> PassageInputs { get; }
}

/// <summary>Approved exhaustive beat-to-passage mapping.</summary>
public static class DocumentaryNarrativeDraftMappings
{
    public static DocumentaryNarrativePassageType PassageType(DocumentaryNarrativeBeatType type) => type switch
    {
        DocumentaryNarrativeBeatType.Hook => DocumentaryNarrativePassageType.Opening, DocumentaryNarrativeBeatType.Question => DocumentaryNarrativePassageType.Question,
        DocumentaryNarrativeBeatType.Orientation => DocumentaryNarrativePassageType.Orientation, DocumentaryNarrativeBeatType.Discovery => DocumentaryNarrativePassageType.Discovery,
        DocumentaryNarrativeBeatType.Explanation => DocumentaryNarrativePassageType.Explanation, DocumentaryNarrativeBeatType.Evidence => DocumentaryNarrativePassageType.Evidence,
        DocumentaryNarrativeBeatType.Context => DocumentaryNarrativePassageType.Context, DocumentaryNarrativeBeatType.Clarification => DocumentaryNarrativePassageType.Clarification,
        DocumentaryNarrativeBeatType.Observation => DocumentaryNarrativePassageType.Observation, DocumentaryNarrativeBeatType.Guidance => DocumentaryNarrativePassageType.Guidance,
        DocumentaryNarrativeBeatType.Reflection => DocumentaryNarrativePassageType.Reflection, DocumentaryNarrativeBeatType.Transition => DocumentaryNarrativePassageType.Transition,
        DocumentaryNarrativeBeatType.Closure => DocumentaryNarrativePassageType.Closing, _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}

/// <summary>Stateless binding of externally authored text to certified composition beats.</summary>
public sealed class DocumentaryNarrativeDraftAssembler
{
    public DocumentaryNarrativeDraft Assemble(DocumentaryNarrativeDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var beats = request.Composition.Sections.SelectMany(x => x.Beats).ToArray();
        var inputs = request.PassageInputs.ToDictionary(x => x.SourceBeatId, StringComparer.Ordinal);
        if (inputs.Count != beats.Length || inputs.Keys.Any(id => !beats.Any(beat => string.Equals(beat.BeatId, id, StringComparison.Ordinal))))
            throw new ArgumentException("Passage inputs must match composition beats exactly.", nameof(request));
        var sections = request.Composition.Sections.Select(section => new DocumentaryNarrativeDraftSection(
            request.DraftId + ".section." + section.SectionNumber.ToString(CultureInfo.InvariantCulture), section.SectionNumber, section.SectionId, section.Title, section.Purpose,
            section.NarrativeStage, section.SectionRole, section.Beats.Select(beat => Map(beat, inputs[beat.BeatId])).ToArray(), section.EstimatedDurationSeconds)).ToArray();
        var c = request.Composition;
        return new(request.DraftId, c.CompositionId, c.BlueprintId, c.KnowledgeId, c.SubjectId, c.SubjectName, c.PublicationFormat, c.PrimaryLanguage, request.Version, request.Metadata, sections);
    }

    private static DocumentaryNarrativePassage Map(DocumentaryNarrativeBeat beat, DocumentaryNarrativePassageInput input) => new(
        beat.BeatId + ".passage", beat.BeatNumber, beat.BeatId, beat.BeatNumber, beat.SourceSceneId, beat.SourceSceneNumber, beat.Title,
        DocumentaryNarrativeDraftMappings.PassageType(beat.BeatType), beat.NarrativeStage, beat.SceneRole, beat.ViewerQuestion, beat.Purpose, input.Text,
        beat.KnowledgeReferences, beat.VisualOpportunities, beat.Transition, beat.EditorialOutcome, beat.EstimatedDurationSeconds);
}
