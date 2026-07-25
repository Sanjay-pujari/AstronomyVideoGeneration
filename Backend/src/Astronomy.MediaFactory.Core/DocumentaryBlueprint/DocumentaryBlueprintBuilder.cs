namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>
/// Immutable, complete input for constructing a documentary scene blueprint.
/// Editorial decisions are supplied by the caller and are not inferred.
/// </summary>
public sealed class DocumentarySceneBlueprintInput
{
    public DocumentarySceneBlueprintInput(
        string sceneId,
        int sceneNumber,
        string title,
        DocumentaryNarrativeStage narrativeStage,
        DocumentarySceneRole sceneRole,
        ViewerQuestion viewerQuestion,
        SceneObjective sceneObjective,
        EditorialOutcome editorialOutcome,
        EditorialPriority editorialPriority,
        IReadOnlyList<KnowledgeReference> knowledgeReferences,
        IReadOnlyList<VisualOpportunity> visualOpportunities,
        SceneTransition transition,
        int estimatedDurationSeconds)
    {
        SceneId = Guard.Required(sceneId, nameof(sceneId));
        Title = Guard.Required(title, nameof(title));
        Guard.Enum(narrativeStage, nameof(narrativeStage));
        Guard.Enum(sceneRole, nameof(sceneRole));
        Guard.Enum(editorialPriority, nameof(editorialPriority));
        if (estimatedDurationSeconds < 0) throw new ArgumentOutOfRangeException(nameof(estimatedDurationSeconds));

        SceneNumber = sceneNumber;
        NarrativeStage = narrativeStage;
        SceneRole = sceneRole;
        ViewerQuestion = viewerQuestion ?? throw new ArgumentNullException(nameof(viewerQuestion));
        SceneObjective = sceneObjective ?? throw new ArgumentNullException(nameof(sceneObjective));
        EditorialOutcome = editorialOutcome ?? throw new ArgumentNullException(nameof(editorialOutcome));
        EditorialPriority = editorialPriority;
        KnowledgeReferences = Guard.Copy(knowledgeReferences, nameof(knowledgeReferences));
        VisualOpportunities = Guard.Copy(visualOpportunities, nameof(visualOpportunities));
        Transition = transition ?? throw new ArgumentNullException(nameof(transition));
        EstimatedDurationSeconds = estimatedDurationSeconds;
    }

    public string SceneId { get; }
    public int SceneNumber { get; }
    public string Title { get; }
    public DocumentaryNarrativeStage NarrativeStage { get; }
    public DocumentarySceneRole SceneRole { get; }
    public ViewerQuestion ViewerQuestion { get; }
    public SceneObjective SceneObjective { get; }
    public EditorialOutcome EditorialOutcome { get; }
    public EditorialPriority EditorialPriority { get; }
    public IReadOnlyList<KnowledgeReference> KnowledgeReferences { get; }
    public IReadOnlyList<VisualOpportunity> VisualOpportunities { get; }
    public SceneTransition Transition { get; }
    public int EstimatedDurationSeconds { get; }
}

/// <summary>Immutable, complete input for deterministic documentary-blueprint construction.</summary>
public sealed class DocumentaryBlueprintBuildRequest
{
    public DocumentaryBlueprintBuildRequest(
        string blueprintId,
        string knowledgeId,
        string subjectId,
        string subjectName,
        BlueprintPublicationFormat publicationFormat,
        string primaryLanguage,
        string version,
        DocumentaryBlueprintMetadata metadata,
        IReadOnlyList<DocumentarySceneBlueprintInput> scenes)
    {
        BlueprintId = Guard.Required(blueprintId, nameof(blueprintId));
        KnowledgeId = Guard.Required(knowledgeId, nameof(knowledgeId));
        SubjectId = Guard.Required(subjectId, nameof(subjectId));
        SubjectName = Guard.Required(subjectName, nameof(subjectName));
        PrimaryLanguage = Guard.Required(primaryLanguage, nameof(primaryLanguage));
        Version = Guard.Required(version, nameof(version));
        Guard.Enum(publicationFormat, nameof(publicationFormat));
        PublicationFormat = publicationFormat;
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Scenes = Guard.Copy(scenes, nameof(scenes));

        if (Scenes.Select(scene => scene.SceneId).Distinct(StringComparer.Ordinal).Count() != Scenes.Count)
            throw new ArgumentException("Scene IDs must be unique.", nameof(scenes));
        if (Scenes.Select(scene => scene.SceneNumber).Distinct().Count() != Scenes.Count)
            throw new ArgumentException("Scene numbers must be unique.", nameof(scenes));
    }

    public string BlueprintId { get; }
    public string KnowledgeId { get; }
    public string SubjectId { get; }
    public string SubjectName { get; }
    public BlueprintPublicationFormat PublicationFormat { get; }
    public string PrimaryLanguage { get; }
    public string Version { get; }
    public DocumentaryBlueprintMetadata Metadata { get; }
    public IReadOnlyList<DocumentarySceneBlueprintInput> Scenes { get; }
}

/// <summary>Pure deterministic mapper from approved planning input to the O2.1 aggregate.</summary>
public sealed class DocumentaryBlueprintBuilder
{
    /// <summary>Builds a blueprint without generating, selecting, sorting, or rewriting any values.</summary>
    public DocumentaryBlueprint Build(DocumentaryBlueprintBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scenes = request.Scenes.Select(scene => new DocumentarySceneBlueprint(
            scene.SceneId,
            scene.SceneNumber,
            scene.Title,
            scene.NarrativeStage,
            scene.SceneRole,
            scene.ViewerQuestion,
            scene.SceneObjective,
            scene.EditorialOutcome,
            scene.EditorialPriority,
            scene.KnowledgeReferences,
            scene.VisualOpportunities,
            scene.Transition,
            scene.EstimatedDurationSeconds)).ToArray();

        return new DocumentaryBlueprint(
            request.BlueprintId,
            request.KnowledgeId,
            request.SubjectId,
            request.SubjectName,
            request.PublicationFormat,
            request.PrimaryLanguage,
            request.Version,
            request.Metadata,
            scenes);
    }
}
