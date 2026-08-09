using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Rendering;

/// <summary>Adapts frozen upstream contracts into Phase 13 semantics without changing their authority.</summary>
internal static class Phase13GallerySemanticHydrator
{
    internal enum GallerySemanticUsage
    {
        PublicFact, PublicIdentity, PublicObservation, PublicObjectIdentity,
        EditorialIntent, NarrativeInstruction, WorkflowInstruction, InternalReference, DiagnosticOnly
    }
    private static readonly Regex EditorialReferencePattern = new(
        @"^(?:Outcome|Objective|Scene|Beat|Knowledge|Frame)\d+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    internal const string Phase2Authority = "02-intelligence/production-event-intelligence.json";
    internal const string Phase2Knowledge = "02-intelligence/certified-knowledge-context.json";
    internal const string Phase4Blueprint = "04-blueprint/documentary-blueprint.json";
    internal const string Phase4Knowledge = "04-blueprint/knowledge-selection.json";
    internal const string Phase6Authority = "06-story-frames/story-frames.json";

    internal sealed record GallerySemanticItem(string SemanticId, string SemanticCategory,
        GallerySemanticUsage Usage, string SourceArtifact, string SourceJsonPointer, string SourceValue,
        string? ResolvedPublicValue, string AuthorityChecksum, bool Certified, string TransformationRule,
        string? NormalizedVisualTreatment = null)
    {
        internal bool IsPublicationEligible => Usage is GallerySemanticUsage.PublicFact or GallerySemanticUsage.PublicIdentity
            or GallerySemanticUsage.PublicObservation or GallerySemanticUsage.PublicObjectIdentity;
        internal bool IsVisualPlanningEligible => Usage is not GallerySemanticUsage.InternalReference
            and not GallerySemanticUsage.DiagnosticOnly && (!string.IsNullOrWhiteSpace(ResolvedPublicValue)
                || !string.IsNullOrWhiteSpace(NormalizedVisualTreatment));
        internal bool IsInternalIdentifier => Usage == GallerySemanticUsage.InternalReference;
        internal string Text => ResolvedPublicValue ?? SourceValue;
        internal string Category => SemanticCategory;
        internal string? SourceId => SemanticId;
        internal string AuthoritySource => SourceArtifact;
        internal string AuthorityPath => SourceJsonPointer;
    }
    internal sealed record ResolvedGalleryEditorialReference(string ReferenceId, string ReferenceType,
        string? ResolvedText, string? ResolvedSemanticCategory, string SourceArtifact,
        string SourceJsonPointer, string SourceChecksum, bool Certified, string ResolutionStatus);
    internal sealed record GalleryCertifiedSemanticContext(string EventType, IReadOnlyList<string> PrimaryObjects,
        IReadOnlyList<string> SecondaryObjects, IReadOnlyList<GallerySemanticItem> IdentityFacts,
        IReadOnlyList<GallerySemanticItem> IdentificationFacts, IReadOnlyList<GallerySemanticItem> BrightObjectFacts,
        IReadOnlyList<GallerySemanticItem> DeepSkyFacts, IReadOnlyList<GallerySemanticItem> ScienceFacts,
        IReadOnlyList<GallerySemanticItem> HistoryStoryFacts, IReadOnlyList<GallerySemanticItem> ObservationFacts,
        IReadOnlyList<GallerySemanticItem> LearningObjectives, IReadOnlyList<GallerySemanticItem> ViewerTakeaways)
    {
        internal int SemanticItemCount => IdentityFacts.Count + IdentificationFacts.Count + BrightObjectFacts.Count
            + DeepSkyFacts.Count + ScienceFacts.Count + HistoryStoryFacts.Count + ObservationFacts.Count
            + LearningObjectives.Count + ViewerTakeaways.Count;
        internal IEnumerable<GallerySemanticItem> AllItems => IdentityFacts.Concat(IdentificationFacts)
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
        var buckets = Enumerable.Range(0, 9).Select(_ => new List<GallerySemanticItem>()).ToArray();
        void Add(int bucket, string? text, string source, string pointer, GallerySemanticUsage usage,
            string? id = null, string? visualTreatment = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var candidate = text.Trim();
            if (IsInternalReference(candidate))
            {
                var resolved = ResolveGalleryEditorialReference(candidate, p4, p6, p2, source, pointer);
                RequireResolvedEditorialReference(resolved, source, pointer);
                buckets[bucket].Add(Item(candidate, resolved.ResolvedSemanticCategory ?? Category(bucket),
                    GallerySemanticUsage.InternalReference, resolved.SourceArtifact, resolved.SourceJsonPointer,
                    candidate, null, resolved.Certified, "reference-resolution-only", visualTreatment));
                return;
            }
            var publishable = usage is GallerySemanticUsage.PublicFact or GallerySemanticUsage.PublicIdentity
                or GallerySemanticUsage.PublicObservation or GallerySemanticUsage.PublicObjectIdentity;
            buckets[bucket].Add(Item(id ?? candidate, Category(bucket), usage, source, pointer, candidate,
                publishable ? candidate : null, true, publishable ? "verbatim-certified-authority" : "planning-only", visualTreatment));
        }
        static GallerySemanticItem Item(string id, string category, GallerySemanticUsage usage, string artifact,
            string pointer, string source, string? resolved, bool certified, string rule, string? visual) =>
            new(id, category, usage, artifact, pointer, source, resolved,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant(), certified, rule, visual);

        var identity = eventAuthority.EventIdentity;
        foreach (var (value, index) in identity.PrimaryObjects.Select((x, i) => (x, i)))
            Add(0, $"{value} is the primary {identity.EventType} object.", Phase2Authority,
                $"/eventIdentity/primaryObjects/{index}", GallerySemanticUsage.PublicObjectIdentity, value);
        foreach (var (claim, index) in p2.Claims.Select((claim, index) => (claim, index)).Where(entry =>
                     entry.claim.ReviewStatus.Equals("Accepted", StringComparison.OrdinalIgnoreCase) || entry.claim.Classification.Equals("Certified", StringComparison.OrdinalIgnoreCase)))
        {
            var bucket = Bucket($"{claim.Category} {claim.ClaimType}", claim.Text);
            Add(bucket, claim.Text, Phase2Knowledge, $"/claims/{index}/text",
                bucket == 0 ? GallerySemanticUsage.PublicIdentity : bucket == 6 ? GallerySemanticUsage.PublicObservation : GallerySemanticUsage.PublicFact,
                claim.KnowledgeId);
        }
        foreach (var (scene, index) in p4.LongBlueprint.Scenes.Select((x, i) => (x, i)))
        {
            var treatment = NormalizeVisualTreatment(scene.SceneRole.ToString());
            Add(7, scene.SceneObjective.LearningGoal, Phase4Blueprint, $"/longVariant/blueprint/scenes/{index}/sceneObjective/learningGoal", GallerySemanticUsage.EditorialIntent, scene.SceneId, treatment);
            Add(8, scene.EditorialOutcome.ViewerTakeaway, Phase4Blueprint, $"/longVariant/blueprint/scenes/{index}/editorialOutcome/viewerTakeaway", GallerySemanticUsage.EditorialIntent, scene.SceneId, treatment);
            Add(Bucket(scene.SceneRole.ToString(), scene.EditorialOutcome.NarrativeContribution), scene.EditorialOutcome.NarrativeContribution,
                Phase4Blueprint, $"/longVariant/blueprint/scenes/{index}/editorialOutcome/narrativeContribution", GallerySemanticUsage.NarrativeInstruction, scene.SceneId, treatment);
        }
        foreach (var (frame, index) in p6.Frames.Select((x, i) => (x, i)))
        {
            Add(Bucket(frame.SceneRole, frame.NarrativeIntent), frame.NarrativeIntent, Phase6Authority,
                $"/frames/{index}/narrativeIntent", GallerySemanticUsage.NarrativeInstruction, frame.FrameId, NormalizeVisualTreatment(frame.SceneRole));
            Add(Bucket(frame.FrameRole, frame.VisualIntent), frame.VisualIntent, Phase6Authority,
                $"/frames/{index}/visualIntent", GallerySemanticUsage.EditorialIntent, frame.FrameId, NormalizeVisualTreatment(frame.FrameRole));
            foreach (var (note, noteIndex) in frame.ProductionNotes.Select((x, i) => (x, i)))
                Add(Bucket(frame.FrameRole, note), note, Phase6Authority, $"/frames/{index}/productionNotes/{noteIndex}", GallerySemanticUsage.WorkflowInstruction, frame.FrameId);
        }
        return new(identity.EventType, identity.PrimaryObjects, eventAuthority.Intelligence.SecondaryObjects,
            buckets[0], buckets[1], buckets[2], buckets[3], buckets[4], buckets[5], buckets[6], buckets[7], buckets[8]);
    }

    internal static string NormalizeVisualTreatment(string value) => value.Contains("histor", StringComparison.OrdinalIgnoreCase)
        ? "historical astronomy context" : value.Contains("cultur", StringComparison.OrdinalIgnoreCase)
        ? "cultural astronomy context" : value.Contains("recogn", StringComparison.OrdinalIgnoreCase)
        ? "clear constellation recognition composition" : "role-appropriate astronomy composition";

    internal static bool IsInternalReference(string value) => EditorialReferencePattern.IsMatch(value.Trim());

    internal static void RequireResolvedEditorialReference(ResolvedGalleryEditorialReference resolved,
        string sourceArtifact, string sourcePath)
    {
        if (resolved.ResolutionStatus == "Resolved" && !string.IsNullOrWhiteSpace(resolved.ResolvedText)) return;
        throw new InvalidOperationException($"P13_GALLERY_EDITORIAL_REFERENCE_UNRESOLVED: referenceId={resolved.ReferenceId}; referenceType={resolved.ReferenceType}; sourceArtifact={sourceArtifact}; sourcePath={sourcePath}");
    }

    /// <summary>Dereferences only relationships declared by the frozen Phase 2/4/6 contracts.</summary>
    internal static ResolvedGalleryEditorialReference ResolveGalleryEditorialReference(string referenceId,
        DocumentaryBlueprintAggregate p4, StoryFramesAuthority p6, CertifiedKnowledgeContext knowledge,
        string sourceArtifact = Phase4Blueprint, string sourcePath = "")
    {
        static string Checksum(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        ResolvedGalleryEditorialReference Resolved(string type, string text, string artifact, string pointer, string category) =>
            new(referenceId, type, text, category, artifact, pointer, Checksum(text), true, "Resolved");

        var variants = new[] { p4.LongVariant, p4.ShortVariant };
        foreach (var variant in variants)
        {
            var variantName = variant == p4.LongVariant ? "longVariant" : "shortVariant";
            var editorialOutcome = ResolveEditorialOutcomeReference(referenceId, variant.Blueprint.Scenes, variantName);
            if (editorialOutcome.ResolutionStatus == "Resolved") return editorialOutcome;
            for (var i = 0; i < variant.Blueprint.Scenes.Count; i++)
            {
                var scene = variant.Blueprint.Scenes[i];
                var trace = variant.SceneTraceability.FirstOrDefault(x => x.SceneId == scene.SceneId);
                if (trace?.LearningObjectiveId.Equals(referenceId, StringComparison.Ordinal) == true)
                    return Resolved("learningObjectiveId", scene.SceneObjective.LearningGoal, Phase4Blueprint,
                        $"/{variantName}/blueprint/scenes/{i}/sceneObjective/learningGoal", "LearningObjective");
                if (scene.SceneId.Equals(referenceId, StringComparison.Ordinal))
                    return Resolved("sceneId", scene.SceneObjective.Summary, Phase4Blueprint,
                        $"/{variantName}/blueprint/scenes/{i}/sceneObjective/summary", "SceneObjective");
            }
        }
        var claim = knowledge.Claims.Select((value, index) => (value, index))
            .FirstOrDefault(x => x.value.KnowledgeId.Equals(referenceId, StringComparison.Ordinal));
        if (claim.value is not null && !string.IsNullOrWhiteSpace(claim.value.Text) &&
            (claim.value.Classification.Equals("Certified", StringComparison.OrdinalIgnoreCase) || claim.value.ReviewStatus.Equals("Accepted", StringComparison.OrdinalIgnoreCase)))
            return Resolved("knowledgeSelectionId", claim.value.Text!, Phase2Knowledge, $"/claims/{claim.index}/text", claim.value.Category);

        var frame = p6.Frames.Select((value, index) => (value, index))
            .FirstOrDefault(x => x.value.FrameId.Equals(referenceId, StringComparison.Ordinal));
        if (frame.value is not null)
            return Resolved("storyFrameReference", frame.value.NarrativeIntent, Phase6Authority,
                $"/frames/{frame.index}/narrativeIntent", "StoryFrame");

        var type = referenceId.StartsWith("Outcome", StringComparison.OrdinalIgnoreCase) ? "editorialOutcomeId"
            : referenceId.StartsWith("Objective", StringComparison.OrdinalIgnoreCase) ? "learningObjectiveId"
            : referenceId.StartsWith("Knowledge", StringComparison.OrdinalIgnoreCase) ? "knowledgeSelectionId"
            : referenceId.StartsWith("Frame", StringComparison.OrdinalIgnoreCase) ? "storyFrameReference"
            : referenceId.StartsWith("Scene", StringComparison.OrdinalIgnoreCase) ? "sceneId" : "internalEditorialId";
        return new(referenceId, type, null, null, sourceArtifact, sourcePath, "", false, "Unresolved");
    }

    internal static ResolvedGalleryEditorialReference ResolveEditorialOutcomeReference(
        string referenceId, IReadOnlyList<DocumentarySceneBlueprint> scenes, string variant = "longVariant")
    {
        for (var i = 0; i < scenes.Count; i++)
        {
            var outcome = scenes[i].EditorialOutcome;
            if (!outcome.NarrativeContribution.Equals(referenceId, StringComparison.Ordinal)) continue;
            var text = outcome.ViewerTakeaway;
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            return new(referenceId, "editorialOutcomeId", text, "ViewerTakeaway", Phase4Blueprint,
                $"/{variant}/blueprint/scenes/{i}/editorialOutcome/viewerTakeaway", checksum, true, "Resolved");
        }
        return new(referenceId, "editorialOutcomeId", null, null, Phase4Blueprint,
            $"/{variant}/blueprint/scenes", "", false, "Unresolved");
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
