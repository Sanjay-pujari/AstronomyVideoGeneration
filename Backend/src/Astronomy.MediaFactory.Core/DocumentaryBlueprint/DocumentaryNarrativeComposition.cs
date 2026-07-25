using System.Collections.ObjectModel;
using System.Globalization;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Identifies the structural role of a narrative section.</summary>
public enum DocumentaryNarrativeSectionRole { Opening, Orientation, Exploration, Explanation, Context, Correction, PracticalGuidance, Reflection, Closing }

/// <summary>Identifies the planning function of a narrative beat.</summary>
public enum DocumentaryNarrativeBeatType { Hook, Question, Orientation, Discovery, Explanation, Evidence, Context, Clarification, Observation, Guidance, Reflection, Transition, Closure }

/// <summary>Externally supplied provenance for a narrative composition.</summary>
public sealed record NarrativeCompositionMetadata
{
    public NarrativeCompositionMetadata(DateTimeOffset createdUtc, string createdBy, string compositionModelVersion,
        string blueprintVersion, string blueprintSchemaVersion, string compositionSchemaVersion, string correlationId)
    {
        CreatedUtc = createdUtc != default ? createdUtc : throw new ArgumentException("A non-default creation timestamp is required.", nameof(createdUtc));
        CreatedBy = Guard.Required(createdBy, nameof(createdBy)); CompositionModelVersion = Guard.Required(compositionModelVersion, nameof(compositionModelVersion));
        BlueprintVersion = Guard.Required(blueprintVersion, nameof(blueprintVersion)); BlueprintSchemaVersion = Guard.Required(blueprintSchemaVersion, nameof(blueprintSchemaVersion));
        CompositionSchemaVersion = compositionSchemaVersion == "1.0" ? compositionSchemaVersion : throw new ArgumentException("Composition schema version must be 1.0.", nameof(compositionSchemaVersion));
        CorrelationId = Guard.Required(correlationId, nameof(correlationId));
    }
    public DateTimeOffset CreatedUtc { get; } public string CreatedBy { get; } public string CompositionModelVersion { get; }
    public string BlueprintVersion { get; } public string BlueprintSchemaVersion { get; } public string CompositionSchemaVersion { get; } public string CorrelationId { get; }
}

/// <summary>The smallest immutable unit of narrative composition planning.</summary>
public sealed class DocumentaryNarrativeBeat
{
    public DocumentaryNarrativeBeat(string beatId, int beatNumber, string sourceSceneId, int sourceSceneNumber, string title,
        DocumentaryNarrativeBeatType beatType, DocumentaryNarrativeStage narrativeStage, DocumentarySceneRole sceneRole,
        ViewerQuestion viewerQuestion, string purpose, IReadOnlyList<KnowledgeReference> knowledgeReferences,
        IReadOnlyList<VisualOpportunity> visualOpportunities, SceneTransition transition, EditorialOutcome editorialOutcome,
        int estimatedDurationSeconds)
    {
        BeatId = Guard.Required(beatId, nameof(beatId)); SourceSceneId = Guard.Required(sourceSceneId, nameof(sourceSceneId)); Title = Guard.Required(title, nameof(title)); Purpose = Guard.Required(purpose, nameof(purpose));
        Guard.Enum(beatType, nameof(beatType)); Guard.Enum(narrativeStage, nameof(narrativeStage)); Guard.Enum(sceneRole, nameof(sceneRole));
        if (estimatedDurationSeconds < 0) throw new ArgumentOutOfRangeException(nameof(estimatedDurationSeconds));
        BeatNumber = beatNumber; SourceSceneNumber = sourceSceneNumber; BeatType = beatType; NarrativeStage = narrativeStage; SceneRole = sceneRole;
        ViewerQuestion = viewerQuestion ?? throw new ArgumentNullException(nameof(viewerQuestion));
        KnowledgeReferences = Guard.Copy(knowledgeReferences, nameof(knowledgeReferences)); VisualOpportunities = Guard.Copy(visualOpportunities, nameof(visualOpportunities));
        Transition = transition ?? throw new ArgumentNullException(nameof(transition)); EditorialOutcome = editorialOutcome ?? throw new ArgumentNullException(nameof(editorialOutcome)); EstimatedDurationSeconds = estimatedDurationSeconds;
    }
    public string BeatId { get; } public int BeatNumber { get; } public string SourceSceneId { get; } public int SourceSceneNumber { get; } public string Title { get; }
    public DocumentaryNarrativeBeatType BeatType { get; } public DocumentaryNarrativeStage NarrativeStage { get; } public DocumentarySceneRole SceneRole { get; }
    public ViewerQuestion ViewerQuestion { get; } public string Purpose { get; } public IReadOnlyList<KnowledgeReference> KnowledgeReferences { get; }
    public IReadOnlyList<VisualOpportunity> VisualOpportunities { get; } public SceneTransition Transition { get; } public EditorialOutcome EditorialOutcome { get; } public int EstimatedDurationSeconds { get; }
}

/// <summary>An immutable, ordered narrative movement.</summary>
public sealed class DocumentaryNarrativeSection
{
    public DocumentaryNarrativeSection(string sectionId, int sectionNumber, string title, string purpose,
        DocumentaryNarrativeStage narrativeStage, DocumentaryNarrativeSectionRole sectionRole,
        IReadOnlyList<DocumentaryNarrativeBeat> beats, int estimatedDurationSeconds)
    {
        SectionId = Guard.Required(sectionId, nameof(sectionId)); Title = Guard.Required(title, nameof(title)); Purpose = Guard.Required(purpose, nameof(purpose));
        Guard.Enum(narrativeStage, nameof(narrativeStage)); Guard.Enum(sectionRole, nameof(sectionRole));
        if (estimatedDurationSeconds < 0) throw new ArgumentOutOfRangeException(nameof(estimatedDurationSeconds));
        SectionNumber = sectionNumber; NarrativeStage = narrativeStage; SectionRole = sectionRole; Beats = Guard.Copy(beats, nameof(beats)); EstimatedDurationSeconds = estimatedDurationSeconds;
        if (Beats.Select(x => x.BeatId).Distinct(StringComparer.Ordinal).Count() != Beats.Count) throw new ArgumentException("Beat IDs must be unique.", nameof(beats));
        if (Beats.Select(x => x.BeatNumber).Distinct().Count() != Beats.Count) throw new ArgumentException("Beat numbers must be unique.", nameof(beats));
    }
    public string SectionId { get; } public int SectionNumber { get; } public string Title { get; } public string Purpose { get; }
    public DocumentaryNarrativeStage NarrativeStage { get; } public DocumentaryNarrativeSectionRole SectionRole { get; }
    public IReadOnlyList<DocumentaryNarrativeBeat> Beats { get; } public int EstimatedDurationSeconds { get; }
}

/// <summary>The immutable narrative-composition aggregate.</summary>
public sealed class DocumentaryNarrativeComposition
{
    public DocumentaryNarrativeComposition(string compositionId, string blueprintId, string knowledgeId, string subjectId, string subjectName,
        BlueprintPublicationFormat publicationFormat, string primaryLanguage, string version, NarrativeCompositionMetadata metadata,
        IReadOnlyList<DocumentaryNarrativeSection> sections)
    {
        CompositionId = Guard.Required(compositionId, nameof(compositionId)); BlueprintId = Guard.Required(blueprintId, nameof(blueprintId)); KnowledgeId = Guard.Required(knowledgeId, nameof(knowledgeId));
        SubjectId = Guard.Required(subjectId, nameof(subjectId)); SubjectName = Guard.Required(subjectName, nameof(subjectName)); PrimaryLanguage = Guard.Required(primaryLanguage, nameof(primaryLanguage)); Version = Guard.Required(version, nameof(version));
        Guard.Enum(publicationFormat, nameof(publicationFormat)); PublicationFormat = publicationFormat; Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata)); Sections = Guard.Copy(sections, nameof(sections));
        if (Sections.Select(x => x.SectionId).Distinct(StringComparer.Ordinal).Count() != Sections.Count) throw new ArgumentException("Section IDs must be unique.", nameof(sections));
        if (Sections.Select(x => x.SectionNumber).Distinct().Count() != Sections.Count) throw new ArgumentException("Section numbers must be unique.", nameof(sections));
    }
    public string CompositionId { get; } public string BlueprintId { get; } public string KnowledgeId { get; } public string SubjectId { get; } public string SubjectName { get; }
    public BlueprintPublicationFormat PublicationFormat { get; } public string PrimaryLanguage { get; } public string Version { get; } public NarrativeCompositionMetadata Metadata { get; }
    public IReadOnlyList<DocumentaryNarrativeSection> Sections { get; }
}

/// <summary>Immutable input to the deterministic narrative composer.</summary>
public sealed class DocumentaryNarrativeCompositionRequest
{
    public DocumentaryNarrativeCompositionRequest(string compositionId, string version, NarrativeCompositionMetadata metadata,
        DocumentaryBlueprint blueprint, DocumentaryBlueprintValidationResult validationResult)
    { CompositionId = Guard.Required(compositionId, nameof(compositionId)); Version = Guard.Required(version, nameof(version)); Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata)); Blueprint = blueprint ?? throw new ArgumentNullException(nameof(blueprint)); ValidationResult = validationResult ?? throw new ArgumentNullException(nameof(validationResult)); }
    public string CompositionId { get; } public string Version { get; } public NarrativeCompositionMetadata Metadata { get; }
    public DocumentaryBlueprint Blueprint { get; } public DocumentaryBlueprintValidationResult ValidationResult { get; }
}

/// <summary>Approved exhaustive deterministic narrative mappings.</summary>
public static class DocumentaryNarrativeCompositionMappings
{
    public static DocumentaryNarrativeBeatType BeatType(DocumentarySceneRole role) => role switch { DocumentarySceneRole.OpeningHook => DocumentaryNarrativeBeatType.Hook, DocumentarySceneRole.Orientation => DocumentaryNarrativeBeatType.Orientation, DocumentarySceneRole.RecognitionGuide or DocumentarySceneRole.CoreDiscovery => DocumentaryNarrativeBeatType.Discovery, DocumentarySceneRole.ScientificExplanation => DocumentaryNarrativeBeatType.Explanation, DocumentarySceneRole.HistoricalContext or DocumentarySceneRole.CulturalContext or DocumentarySceneRole.MythologyContext => DocumentaryNarrativeBeatType.Context, DocumentarySceneRole.MisconceptionCorrection => DocumentaryNarrativeBeatType.Clarification, DocumentarySceneRole.PracticalObservation => DocumentaryNarrativeBeatType.Observation, DocumentarySceneRole.AstrophotographyGuide => DocumentaryNarrativeBeatType.Guidance, DocumentarySceneRole.ReflectiveClosing => DocumentaryNarrativeBeatType.Closure, _ => throw new ArgumentOutOfRangeException(nameof(role)) };
    public static DocumentaryNarrativeSectionRole SectionRole(DocumentarySceneRole role) => role switch { DocumentarySceneRole.OpeningHook => DocumentaryNarrativeSectionRole.Opening, DocumentarySceneRole.Orientation => DocumentaryNarrativeSectionRole.Orientation, DocumentarySceneRole.RecognitionGuide or DocumentarySceneRole.CoreDiscovery => DocumentaryNarrativeSectionRole.Exploration, DocumentarySceneRole.ScientificExplanation => DocumentaryNarrativeSectionRole.Explanation, DocumentarySceneRole.HistoricalContext or DocumentarySceneRole.CulturalContext or DocumentarySceneRole.MythologyContext => DocumentaryNarrativeSectionRole.Context, DocumentarySceneRole.MisconceptionCorrection => DocumentaryNarrativeSectionRole.Correction, DocumentarySceneRole.PracticalObservation or DocumentarySceneRole.AstrophotographyGuide => DocumentaryNarrativeSectionRole.PracticalGuidance, DocumentarySceneRole.ReflectiveClosing => DocumentaryNarrativeSectionRole.Closing, _ => throw new ArgumentOutOfRangeException(nameof(role)) };
    public static string Title(DocumentaryNarrativeSectionRole role) => role switch { DocumentaryNarrativeSectionRole.Opening => "Opening", DocumentaryNarrativeSectionRole.Orientation => "Orientation", DocumentaryNarrativeSectionRole.Exploration => "Exploration", DocumentaryNarrativeSectionRole.Explanation => "Explanation", DocumentaryNarrativeSectionRole.Context => "Context", DocumentaryNarrativeSectionRole.Correction => "Clarification", DocumentaryNarrativeSectionRole.PracticalGuidance => "Practical Guidance", DocumentaryNarrativeSectionRole.Reflection => "Reflection", DocumentaryNarrativeSectionRole.Closing => "Closing", _ => throw new ArgumentOutOfRangeException(nameof(role)) };
    public static string Purpose(DocumentaryNarrativeSectionRole role) => role switch { DocumentaryNarrativeSectionRole.Opening => "Establish the documentary opening.", DocumentaryNarrativeSectionRole.Orientation => "Orient the viewer to the subject.", DocumentaryNarrativeSectionRole.Exploration => "Guide recognition and discovery.", DocumentaryNarrativeSectionRole.Explanation => "Develop scientific understanding.", DocumentaryNarrativeSectionRole.Context => "Provide historical, cultural, or mythological context.", DocumentaryNarrativeSectionRole.Correction => "Clarify a misconception or misunderstanding.", DocumentaryNarrativeSectionRole.PracticalGuidance => "Provide practical observation or astrophotography guidance.", DocumentaryNarrativeSectionRole.Reflection => "Encourage reflection on the subject.", DocumentaryNarrativeSectionRole.Closing => "Provide documentary closure.", _ => throw new ArgumentOutOfRangeException(nameof(role)) };
}

/// <summary>Stateless synchronous conversion of a certified blueprint to composition structure.</summary>
public sealed class DocumentaryNarrativeComposer
{
    public DocumentaryNarrativeComposition Compose(DocumentaryNarrativeCompositionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var blueprint = request.Blueprint; var validation = request.ValidationResult;
        if (!string.Equals(validation.BlueprintId, blueprint.BlueprintId, StringComparison.Ordinal)) throw new ArgumentException("Validation result must identify the request blueprint.", nameof(request));
        if (!validation.IsValid) throw new InvalidOperationException("An editorially invalid blueprint cannot be composed.");
        var groups = new List<(DocumentaryNarrativeSectionRole Role, List<DocumentaryNarrativeBeat> Beats)>();
        foreach (var scene in blueprint.Scenes)
        {
            var role = DocumentaryNarrativeCompositionMappings.SectionRole(scene.SceneRole);
            var beat = new DocumentaryNarrativeBeat(scene.SceneId + ".beat", scene.SceneNumber, scene.SceneId, scene.SceneNumber, scene.Title,
                DocumentaryNarrativeCompositionMappings.BeatType(scene.SceneRole), scene.NarrativeStage, scene.SceneRole, scene.ViewerQuestion,
                scene.SceneObjective.Summary, scene.KnowledgeReferences, scene.VisualOpportunities, scene.Transition, scene.EditorialOutcome, scene.EstimatedDurationSeconds);
            if (groups.Count == 0 || groups[^1].Role != role) groups.Add((role, []));
            groups[^1].Beats.Add(beat);
        }
        var sections = groups.Select((group, index) => { var number = index + 1; var duration = checked((int)group.Beats.Sum(b => (long)b.EstimatedDurationSeconds)); return new DocumentaryNarrativeSection(request.CompositionId + ".section." + number.ToString(CultureInfo.InvariantCulture), number, DocumentaryNarrativeCompositionMappings.Title(group.Role), DocumentaryNarrativeCompositionMappings.Purpose(group.Role), group.Beats[0].NarrativeStage, group.Role, group.Beats, duration); }).ToArray();
        return new(request.CompositionId, blueprint.BlueprintId, blueprint.KnowledgeId, blueprint.SubjectId, blueprint.SubjectName, blueprint.PublicationFormat, blueprint.PrimaryLanguage, request.Version, request.Metadata, sections);
    }
}
