using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Rendering;

/// <summary>Adapts frozen upstream contracts into Phase 13 semantics without changing their authority.</summary>
internal static class Phase13GallerySemanticHydrator
{
    internal const string Phase2Authority = "02-intelligence/production-event-intelligence.json";
    internal const string Phase2Knowledge = "02-intelligence/certified-knowledge-context.json";
    internal const string Phase4Blueprint = "04-blueprint/documentary-blueprint.json";
    internal const string Phase4Knowledge = "04-blueprint/knowledge-selection.json";
    internal const string Phase6Authority = "06-story-frames/story-frames.json";

    internal sealed record SemanticItem(string Text, string Category, string AuthoritySource,
        string AuthorityPath, bool Certified, string? SourceId);
    internal sealed record GalleryCertifiedSemanticContext(string EventType, IReadOnlyList<string> PrimaryObjects,
        IReadOnlyList<string> SecondaryObjects, IReadOnlyList<SemanticItem> IdentityFacts,
        IReadOnlyList<SemanticItem> IdentificationFacts, IReadOnlyList<SemanticItem> BrightObjectFacts,
        IReadOnlyList<SemanticItem> DeepSkyFacts, IReadOnlyList<SemanticItem> ScienceFacts,
        IReadOnlyList<SemanticItem> HistoryStoryFacts, IReadOnlyList<SemanticItem> ObservationFacts,
        IReadOnlyList<SemanticItem> LearningObjectives, IReadOnlyList<SemanticItem> ViewerTakeaways)
    {
        internal int SemanticItemCount => IdentityFacts.Count + IdentificationFacts.Count + BrightObjectFacts.Count
            + DeepSkyFacts.Count + ScienceFacts.Count + HistoryStoryFacts.Count + ObservationFacts.Count
            + LearningObjectives.Count + ViewerTakeaways.Count;
        internal IEnumerable<SemanticItem> AllItems => IdentityFacts.Concat(IdentificationFacts)
            .Concat(BrightObjectFacts).Concat(DeepSkyFacts).Concat(ScienceFacts).Concat(HistoryStoryFacts)
            .Concat(ObservationFacts).Concat(LearningObjectives).Concat(ViewerTakeaways);
    }

    internal sealed record AuthorityFileDiagnostic(string LogicalAuthority, string ExpectedPath, bool Exists,
        string? LoadedPath, bool ParseSuccess, int SemanticItemCount, string? Error);
    internal sealed record HydrationResult(GalleryCertifiedSemanticContext Context,
        IReadOnlyList<AuthorityFileDiagnostic> Files, IReadOnlyList<string> InputFiles,
        CertifiedKnowledgeContext Phase2, DocumentaryBlueprintAggregate Phase4,
        StoryFramesAuthority Phase6, ProductionEventIntelligenceAuthority EventAuthority);

    // This deliberately mirrors only the stable fields Phase 13 consumes from the Phase 4 contract.
    private sealed record KnowledgeSelectionProjection(string PlanId, string EventId, string Language,
        IReadOnlyList<string> UniqueKnowledgeReferences);

    internal static async Task<HydrationResult> LoadAsync(string outputRoot, CancellationToken ct)
    {
        var authorityPath = Path.Combine(outputRoot, Phase2Authority.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(authorityPath)) throw new InvalidOperationException("P13_SEMANTIC_AUTHORITY_HYDRATION_FAILED: verified ProductionEventIntelligence is missing.");
        var authority = JsonSerializer.Deserialize<ProductionEventIntelligenceAuthority>(await File.ReadAllTextAsync(authorityPath, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("P13_SEMANTIC_AUTHORITY_HYDRATION_FAILED: verified ProductionEventIntelligence is invalid.");
        var phase4Path = Path.Combine(outputRoot, Phase4Blueprint.Replace('/', Path.DirectorySeparatorChar));
        var phase4 = JsonSerializer.Deserialize<DocumentaryBlueprintAggregate>(await File.ReadAllTextAsync(phase4Path, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("P13_SEMANTIC_AUTHORITY_HYDRATION_FAILED: Phase 4 authority is invalid.");
        return await LoadAsync(outputRoot, authority.Metadata.PlanId, phase4.EventId, authority.Metadata.Language, ct);
    }

    internal static async Task<HydrationResult> LoadAsync(string outputRoot, string expectedPlanId,
        string expectedEventId, string expectedLanguage, CancellationToken ct)
    {
        var diagnostics = new List<AuthorityFileDiagnostic>();
        var inputs = new List<string>();
        async Task<T> Load<T>(string logical, string relative)
        {
            var path = Path.Combine(outputRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                diagnostics.Add(new(logical, relative, false, null, false, 0, "File does not exist."));
                throw Failure(diagnostics);
            }
            try
            {
                var value = JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new JsonException($"{typeof(T).Name} deserialized to null.");
                inputs.Add(relative); diagnostics.Add(new(logical, relative, true, relative, true, Count(value), null));
                return value;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                diagnostics.Add(new(logical, relative, true, relative, false, 0, ex.Message));
                throw Failure(diagnostics);
            }
        }

        var eventAuthority = await Load<ProductionEventIntelligenceAuthority>("Verified execution event identity", Phase2Authority);
        var p2 = await Load<CertifiedKnowledgeContext>("Phase 2 certified knowledge", Phase2Knowledge);
        var p4 = await Load<DocumentaryBlueprintAggregate>("Phase 4 documentary blueprint", Phase4Blueprint);
        var selection = await Load<KnowledgeSelectionProjection>("Phase 4 knowledge selection", Phase4Knowledge);
        var p6 = await Load<StoryFramesAuthority>("Phase 6 story-frame authority", Phase6Authority);

        RequireIdentity(eventAuthority.Metadata.PlanId, eventAuthority.EventIdentity.EventFamily, p2.PlanId,
            p4.PlanId, p4.EventId, p4.Language, selection.PlanId, selection.EventId, selection.Language,
            p6.PlanId, p6.EventId, p6.Language, expectedPlanId, expectedEventId, expectedLanguage);
        var context = Normalize(eventAuthority, p2, p4, p6);
        if (context.SemanticItemCount == 0)
            throw Failure(diagnostics, "All canonical authorities parsed, but the schema adapter produced zero semantic items.");
        return new(context, diagnostics, inputs, p2, p4, p6, eventAuthority);
    }

    internal static GalleryCertifiedSemanticContext Normalize(ProductionEventIntelligenceAuthority eventAuthority,
        CertifiedKnowledgeContext p2, DocumentaryBlueprintAggregate p4, StoryFramesAuthority p6)
    {
        var buckets = Enumerable.Range(0, 9).Select(_ => new List<SemanticItem>()).ToArray();
        void Add(int bucket, string? text, string source, string pointer, string? id = null)
        { if (!string.IsNullOrWhiteSpace(text)) buckets[bucket].Add(new(text.Trim(), Category(bucket), source, pointer, true, id)); }

        var identity = eventAuthority.EventIdentity;
        foreach (var (value, index) in identity.PrimaryObjects.Select((x, i) => (x, i)))
            Add(0, $"{value} is the primary {identity.EventType} object.", Phase2Authority, $"/eventIdentity/primaryObjects/{index}", value);
        foreach (var (claim, index) in p2.Claims.Select((claim, index) => (claim, index)).Where(entry =>
                     entry.claim.ReviewStatus.Equals("Accepted", StringComparison.OrdinalIgnoreCase) || entry.claim.Classification.Equals("Certified", StringComparison.OrdinalIgnoreCase)))
        {
            var category = $"{claim.Category} {claim.ClaimType}";
            var bucket = Bucket(category, claim.Text);
            Add(bucket, claim.Text, Phase2Knowledge, $"/claims/{index}/text", claim.KnowledgeId);
        }
        var scenes = p4.LongBlueprint.Scenes;
        foreach (var (scene, index) in scenes.Select((x, i) => (x, i)))
        {
            Add(7, scene.SceneObjective.LearningGoal, Phase4Blueprint, $"/longVariant/blueprint/scenes/{index}/sceneObjective/learningGoal", scene.SceneId);
            Add(8, scene.EditorialOutcome.ViewerTakeaway, Phase4Blueprint, $"/longVariant/blueprint/scenes/{index}/editorialOutcome/viewerTakeaway", scene.SceneId);
            Add(Bucket(scene.SceneRole.ToString(), scene.EditorialOutcome.NarrativeContribution), scene.EditorialOutcome.NarrativeContribution,
                Phase4Blueprint, $"/longVariant/blueprint/scenes/{index}/editorialOutcome/narrativeContribution", scene.SceneId);
        }
        foreach (var (frame, index) in p6.Frames.Select((x, i) => (x, i)))
        {
            Add(Bucket(frame.SceneRole, frame.NarrativeIntent), frame.NarrativeIntent, Phase6Authority, $"/frames/{index}/narrativeIntent", frame.FrameId);
            Add(Bucket(frame.FrameRole, frame.VisualIntent), frame.VisualIntent, Phase6Authority, $"/frames/{index}/visualIntent", frame.FrameId);
            foreach (var (note, noteIndex) in frame.ProductionNotes.Select((x, i) => (x, i)))
                Add(Bucket(frame.FrameRole, note), note, Phase6Authority, $"/frames/{index}/productionNotes/{noteIndex}", frame.FrameId);
        }
        return new(identity.EventType, identity.PrimaryObjects, eventAuthority.Intelligence.SecondaryObjects,
            buckets[0], buckets[1], buckets[2], buckets[3], buckets[4], buckets[5], buckets[6], buckets[7], buckets[8]);
    }

    private static int Bucket(string metadata, string? text)
    {
        var value = $"{metadata} {text}";
        if (Has(metadata, "identity")) return 0;
        if (Has(value, "identif", "recogn", "belt", "locate", "spot")) return 1;
        if (Has(value, "deep sky", "nebula", "m42", "galaxy", "cluster")) return 3;
        if (Has(value, "histor", "story", "culture", "myth")) return 5;
        if (Has(value, "observ", "view", "visible", "equipment", "direction", "window")) return 6;
        if (Has(value, "bright", "star", "object")) return 2;
        if (Has(value, "science", "scientific", "distance", "formation", "physics")) return 4;
        return 4;
    }
    private static bool Has(string value, params string[] terms) => terms.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static string Category(int bucket) => new[] { "Identity", "Identification", "BrightObjects", "DeepSky", "Science", "HistoryOrStory", "Observation", "LearningObjective", "ViewerTakeaway" }[bucket];
    private static int Count<T>(T value) => value switch
    {
        ProductionEventIntelligenceAuthority x => x.EventIdentity.PrimaryObjects.Count + x.Intelligence.SecondaryObjects.Count,
        CertifiedKnowledgeContext x => x.Claims.Count,
        DocumentaryBlueprintAggregate x => x.LongBlueprint.Scenes.Count + x.ShortBlueprint.Scenes.Count,
        KnowledgeSelectionProjection x => x.UniqueKnowledgeReferences.Count,
        StoryFramesAuthority x => x.Frames.Count,
        _ => 0
    };
    private static void RequireIdentity(string eventPlan, string eventFamily, string p2Plan, string p4Plan, string p4Event,
        string p4Language, string selectionPlan, string selectionEvent, string selectionLanguage, string p6Plan, string p6Event,
        string p6Language, string expectedPlan, string expectedEvent, string expectedLanguage)
    {
        var plans = new[] { eventPlan, p2Plan, p4Plan, selectionPlan, p6Plan };
        var events = new[] { p4Event, selectionEvent, p6Event };
        var languages = new[] { p4Language, selectionLanguage, p6Language };
        if (plans.Any(x => x != expectedPlan) || events.Any(x => x != expectedEvent)
            || languages.Any(x => !x.Equals(expectedLanguage, StringComparison.OrdinalIgnoreCase)) || string.IsNullOrWhiteSpace(eventFamily))
            throw new InvalidOperationException("P13_SEMANTIC_AUTHORITY_HYDRATION_FAILED: Phase 2/4/6 plan, event, or language identity differs from Phase 10.");
    }
    private static InvalidOperationException Failure(IEnumerable<AuthorityFileDiagnostic> diagnostics, string? message = null) =>
        new($"P13_SEMANTIC_AUTHORITY_HYDRATION_FAILED: {message ?? string.Join("; ", diagnostics.Select(x => $"{x.ExpectedPath}: {x.Error}"))}");
}
