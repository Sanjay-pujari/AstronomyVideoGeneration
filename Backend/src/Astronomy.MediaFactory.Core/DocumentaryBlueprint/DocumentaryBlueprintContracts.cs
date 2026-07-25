using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Identifies the narrative function performed by a documentary scene.</summary>
public enum DocumentaryNarrativeStage { Wonder, Recognition, Discovery, Science, History, Culture, ModernAstronomy, Clarification, Observation, Astrophotography, Inspiration }
/// <summary>Identifies the editorial role performed by a documentary scene.</summary>
public enum DocumentarySceneRole { OpeningHook, Orientation, RecognitionGuide, CoreDiscovery, ScientificExplanation, HistoricalContext, CulturalContext, MythologyContext, MisconceptionCorrection, PracticalObservation, AstrophotographyGuide, ReflectiveClosing }
/// <summary>Expresses the editorial importance of a scene.</summary>
public enum EditorialPriority { Critical, High, Medium, Optional }
/// <summary>Identifies the publication shape for which a blueprint was prepared.</summary>
public enum BlueprintPublicationFormat { LongDocumentary, ShortDocumentary, ObservationGuide, Article, Podcast, SocialVideo }

/// <summary>The question a scene explores for its viewer.</summary>
public sealed record ViewerQuestion
{
    public ViewerQuestion(string text) => Text = Required(text, nameof(text));
    public string Text { get; }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-blank value is required.", name) : value;
}

/// <summary>Describes why a scene exists without supplying its prose.</summary>
public sealed record SceneObjective
{
    public SceneObjective(string summary, string learningGoal, string curiosityGoal, string emotionalGoal) =>
        (Summary, LearningGoal, CuriosityGoal, EmotionalGoal) = (Guard.Required(summary, nameof(summary)), Guard.Required(learningGoal, nameof(learningGoal)), Guard.Required(curiosityGoal, nameof(curiosityGoal)), Guard.Required(emotionalGoal, nameof(emotionalGoal)));
    public string Summary { get; }
    public string LearningGoal { get; }
    public string CuriosityGoal { get; }
    public string EmotionalGoal { get; }
}

/// <summary>Records the intended editorial effect of a scene.</summary>
public sealed record EditorialOutcome(string ViewerTakeaway, string NarrativeContribution, bool IntroducesNewKnowledge, bool DeepensUnderstanding, bool CreatesCuriosity, bool ProvidesPracticalGuidance, bool DeliversEmotionalPayoff)
{
    public string ViewerTakeaway { get; init; } = Guard.Required(ViewerTakeaway, nameof(ViewerTakeaway));
    public string NarrativeContribution { get; init; } = Guard.Required(NarrativeContribution, nameof(NarrativeContribution));
}

/// <summary>References knowledge without copying its content into the blueprint.</summary>
public sealed record KnowledgeReference(string KnowledgeEntryId, string Section, string Purpose, bool IsPrimary)
{
    public string KnowledgeEntryId { get; init; } = Guard.Required(KnowledgeEntryId, nameof(KnowledgeEntryId));
    public string Section { get; init; } = Guard.Required(Section, nameof(Section));
    public string Purpose { get; init; } = Guard.Required(Purpose, nameof(Purpose));
}

/// <summary>Stores editorial transition intent, not finished transition prose.</summary>
public sealed record SceneTransition(string TransitionIntent, string NextQuestionSeed, string EditorialDirection)
{
    public string TransitionIntent { get; init; } = Guard.Required(TransitionIntent, nameof(TransitionIntent));
    public string NextQuestionSeed { get; init; } = Guard.Required(NextQuestionSeed, nameof(NextQuestionSeed));
    public string EditorialDirection { get; init; } = Guard.Required(EditorialDirection, nameof(EditorialDirection));
}

/// <summary>Describes an editorial opportunity for supporting imagery.</summary>
public sealed record VisualOpportunity(string Description, string Type, string? KnowledgeEntryId, string? SourceAssetId, bool IsScientificallyRequired)
{
    public string Description { get; init; } = Guard.Required(Description, nameof(Description));
    public string Type { get; init; } = Guard.Required(Type, nameof(Type));
    public string? KnowledgeEntryId { get; init; } = Guard.OptionalIdentifier(KnowledgeEntryId, nameof(KnowledgeEntryId));
    public string? SourceAssetId { get; init; } = Guard.OptionalIdentifier(SourceAssetId, nameof(SourceAssetId));
}

/// <summary>Externally supplied provenance and version information for a blueprint.</summary>
public sealed record DocumentaryBlueprintMetadata(DateTimeOffset CreatedUtc, string CreatedBy, string EditorialModelVersion, string KnowledgeVersion, string BlueprintSchemaVersion, string CorrelationId)
{
    public DateTimeOffset CreatedUtc { get; init; } = CreatedUtc != default ? CreatedUtc : throw new ArgumentException("A non-default creation timestamp is required.", nameof(CreatedUtc));
    public string CreatedBy { get; init; } = Guard.Required(CreatedBy, nameof(CreatedBy));
    public string EditorialModelVersion { get; init; } = Guard.Required(EditorialModelVersion, nameof(EditorialModelVersion));
    public string KnowledgeVersion { get; init; } = Guard.Required(KnowledgeVersion, nameof(KnowledgeVersion));
    public string BlueprintSchemaVersion { get; init; } = BlueprintSchemaVersion == "1.0" ? BlueprintSchemaVersion : throw new ArgumentException("Blueprint schema version must be 1.0.", nameof(BlueprintSchemaVersion));
    public string CorrelationId { get; init; } = Guard.Required(CorrelationId, nameof(CorrelationId));
}

/// <summary>An immutable unit of documentary planning and editorial structure.</summary>
public sealed class DocumentarySceneBlueprint
{
    public DocumentarySceneBlueprint(string sceneId, int sceneNumber, string title, DocumentaryNarrativeStage narrativeStage, DocumentarySceneRole sceneRole, ViewerQuestion viewerQuestion, SceneObjective sceneObjective, EditorialOutcome editorialOutcome, EditorialPriority editorialPriority, IReadOnlyList<KnowledgeReference> knowledgeReferences, IReadOnlyList<VisualOpportunity> visualOpportunities, SceneTransition transition, int estimatedDurationSeconds)
    {
        SceneId = Guard.Required(sceneId, nameof(sceneId)); Title = Guard.Required(title, nameof(title));
        Guard.Enum(narrativeStage, nameof(narrativeStage)); Guard.Enum(sceneRole, nameof(sceneRole)); Guard.Enum(editorialPriority, nameof(editorialPriority));
        if (estimatedDurationSeconds < 0) throw new ArgumentOutOfRangeException(nameof(estimatedDurationSeconds));
        SceneNumber = sceneNumber; NarrativeStage = narrativeStage; SceneRole = sceneRole;
        ViewerQuestion = viewerQuestion ?? throw new ArgumentNullException(nameof(viewerQuestion)); SceneObjective = sceneObjective ?? throw new ArgumentNullException(nameof(sceneObjective)); EditorialOutcome = editorialOutcome ?? throw new ArgumentNullException(nameof(editorialOutcome)); EditorialPriority = editorialPriority;
        KnowledgeReferences = Guard.Copy(knowledgeReferences, nameof(knowledgeReferences)); VisualOpportunities = Guard.Copy(visualOpportunities, nameof(visualOpportunities)); Transition = transition ?? throw new ArgumentNullException(nameof(transition)); EstimatedDurationSeconds = estimatedDurationSeconds;
    }
    public string SceneId { get; } public int SceneNumber { get; } public string Title { get; } public DocumentaryNarrativeStage NarrativeStage { get; } public DocumentarySceneRole SceneRole { get; } public ViewerQuestion ViewerQuestion { get; } public SceneObjective SceneObjective { get; } public EditorialOutcome EditorialOutcome { get; } public EditorialPriority EditorialPriority { get; } public IReadOnlyList<KnowledgeReference> KnowledgeReferences { get; } public IReadOnlyList<VisualOpportunity> VisualOpportunities { get; } public SceneTransition Transition { get; } public int EstimatedDurationSeconds { get; }
}

/// <summary>The immutable documentary-planning aggregate.</summary>
public sealed class DocumentaryBlueprint
{
    public DocumentaryBlueprint(string blueprintId, string knowledgeId, string subjectId, string subjectName, BlueprintPublicationFormat publicationFormat, string primaryLanguage, string version, DocumentaryBlueprintMetadata metadata, IReadOnlyList<DocumentarySceneBlueprint> scenes)
    {
        BlueprintId = Guard.Required(blueprintId, nameof(blueprintId)); KnowledgeId = Guard.Required(knowledgeId, nameof(knowledgeId)); SubjectId = Guard.Required(subjectId, nameof(subjectId)); SubjectName = Guard.Required(subjectName, nameof(subjectName)); PrimaryLanguage = Guard.Required(primaryLanguage, nameof(primaryLanguage)); Version = Guard.Required(version, nameof(version)); Guard.Enum(publicationFormat, nameof(publicationFormat)); PublicationFormat = publicationFormat; Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata)); Scenes = Guard.Copy(scenes, nameof(scenes));
        if (Scenes.Select(x => x.SceneId).Distinct(StringComparer.Ordinal).Count() != Scenes.Count) throw new ArgumentException("Scene IDs must be unique.", nameof(scenes));
        if (Scenes.Select(x => x.SceneNumber).Distinct().Count() != Scenes.Count) throw new ArgumentException("Scene numbers must be unique.", nameof(scenes));
    }
    public string BlueprintId { get; } public string KnowledgeId { get; } public string SubjectId { get; } public string SubjectName { get; } public BlueprintPublicationFormat PublicationFormat { get; } public string PrimaryLanguage { get; } public string Version { get; } public DocumentaryBlueprintMetadata Metadata { get; } public IReadOnlyList<DocumentarySceneBlueprint> Scenes { get; }
}

internal static class Guard
{
    public static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-blank value is required.", name) : value;
    public static string? OptionalIdentifier(string? value, string name) => value is not null && string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("An optional identifier cannot be blank.", name) : value;
    public static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string name) where T : class { ArgumentNullException.ThrowIfNull(values, name); if (values.Any(x => x is null)) throw new ArgumentException("Collections cannot contain null elements.", name); return new ReadOnlyCollection<T>(values.ToArray()); }
    public static void Enum<T>(T value, string name) where T : struct, Enum { if (!System.Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(name); }
}
