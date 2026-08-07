using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Strict, typed read boundary for the three committed authorities consumed by Phase 8.</summary>
public sealed class Phase8AuthorityLoader : IPhase8AuthorityLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Phase8AuthorityInput> LoadAsync(Phase8AuthorityLoadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var blueprintPath = Path.Combine(request.OutputRoot, "04-blueprint", "documentary-blueprint.json");
        var storyPath = Path.Combine(request.OutputRoot, "06-story-frames", "story-frames.json");
        var indexPath = Path.Combine(request.OutputRoot, "06-story-frames", "story-frame-index.json");
        RequireFile(blueprintPath, "Phase 4 documentary blueprint");
        RequireFile(storyPath, "Phase 6 Story Frame authority");
        RequireFile(indexPath, "Phase 6 Story Frame committed index");

        var blueprint = await ReadAsync<DocumentaryBlueprintAggregate>(blueprintPath, cancellationToken);
        var story = await ReadAsync<StoryFramesAuthority>(storyPath, cancellationToken);
        var index = await ReadAsync<StoryFrameIndex>(indexPath, cancellationToken);
        if (!string.Equals(story.SemanticChecksum, StoryFrameAuthorityChecksum.Authority(story), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(index.Checksum, StoryFrameAuthorityChecksum.Index(index), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(index.SourceStoryFramesChecksum, story.SemanticChecksum, StringComparison.OrdinalIgnoreCase))
            Fail(Phase8AuthorityReasonCodes.ChecksumMismatch, "Phase 6 authority/index checksum readback failed.");

        var variants = request.RequestedVariants.Select(NormalizeVariant).Distinct(StringComparer.Ordinal).ToArray();
        if (variants.Length == 0) Fail(Phase8AuthorityReasonCodes.VariantAuthorityMissing, "No Phase 8 variant was requested.");
        if (!string.Equals(story.PlanId, request.PlanId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(story.EventId, request.EventId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(story.Language, request.Language, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(blueprint.PlanId, request.PlanId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(blueprint.EventId, request.EventId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(blueprint.Language, request.Language, StringComparison.OrdinalIgnoreCase))
            Fail(Phase8AuthorityReasonCodes.IdentityMismatch, "Plan, event, or language differs across Phase 4/6 and the execution request.");
        if (!DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(blueprint))
            Fail(Phase8AuthorityReasonCodes.ChecksumMismatch, "Phase 4 aggregate checksum readback failed.");
        foreach (var variant in variants)
            if (!story.RequestedVariants.Contains(variant, StringComparer.OrdinalIgnoreCase))
                Fail(Phase8AuthorityReasonCodes.VariantAuthorityMissing, $"Phase 6 has no committed {variant} authority.");

        var longCandidate = variants.Contains("Long")
            ? await ReadCandidateAsync(request.OutputRoot, "long", cancellationToken) : null;
        var shortCandidate = variants.Contains("Short")
            ? await ReadCandidateAsync(request.OutputRoot, "short", cancellationToken) : null;
        var longChecksum = longCandidate is null ? null : await HashFileAsync(CandidatePath(request.OutputRoot, "long"), cancellationToken);
        var shortChecksum = shortCandidate is null ? null : await HashFileAsync(CandidatePath(request.OutputRoot, "short"), cancellationToken);

        var longScenes = variants.Contains("Long") ? Project("Long", story, blueprint, longCandidate!) : [];
        var shortScenes = variants.Contains("Short") ? Project("Short", story, blueprint, shortCandidate!) : [];
        return new(request.PlanId, story.ExecutionId, request.EventId, request.Language, blueprint,
            blueprint.DeterministicChecksum, story, story.SemanticChecksum,
            longCandidate, longChecksum, shortCandidate, shortChecksum, variants, longScenes, shortScenes);
    }

    private static IReadOnlyList<Phase8SceneRequirement> Project(string variant, StoryFramesAuthority authority,
        DocumentaryBlueprintAggregate aggregate, DocumentaryNarrativeReleaseCandidate candidate)
    {
        var blueprint = variant.Equals("Long", StringComparison.OrdinalIgnoreCase) ? aggregate.LongBlueprint : aggregate.ShortBlueprint;
        var frames = authority.Frames.Where(x => x.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.SceneId, StringComparer.Ordinal).OrderBy(x => x.Min(f => f.SceneNumber)).ToArray();
        var passages = candidate.NarrativeDraft.Sections.SelectMany(x => x.Passages)
            .GroupBy(x => x.SourceSceneId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var blueprintScenes = blueprint.Scenes.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var projected = new List<Phase8SceneRequirement>();
        foreach (var group in frames)
        {
            var frame = group.OrderBy(x => x.FrameNumber).First();
            if (!blueprintScenes.TryGetValue(group.Key, out var scene))
                Fail(Phase8AuthorityReasonCodes.SceneLineageMismatch, $"Story Frame scene '{group.Key}' has no Phase 4 blueprint scene.");
            if (!passages.TryGetValue(group.Key, out var narration) || narration.Length == 0)
                Fail(Phase8AuthorityReasonCodes.NarrationSceneMappingFailed, $"Accepted {variant} narration has no identity mapping for scene '{group.Key}'.");
            var visual = string.Join(" ", group.Select(x => x.VisualIntent).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal));
            var opportunity = scene.VisualOpportunities.FirstOrDefault();
            projected.Add(new(variant, group.Key, scene.SceneId, frame.FrameId, frame.SceneNumber,
                frame.SceneRole, frame.NarrativeStage, scene.SceneObjective.Summary, visual,
                frame.CameraDirection, group.SelectMany(x => x.ImageRequirements).Distinct(StringComparer.Ordinal).ToArray(),
                group.SelectMany(x => x.KnowledgeReferenceIds).Concat(scene.KnowledgeReferences.Select(x => x.KnowledgeEntryId)).Distinct(StringComparer.Ordinal).ToArray(),
                string.Join("\n", narration.OrderBy(x => x.PassageNumber).Select(x => x.Text)), group.Key,
                opportunity?.Type ?? "Cinematic", "scene-background", ResolveRenderingPreference(frame, opportunity),
                string.IsNullOrWhiteSpace(frame.Setting) ? null : frame.Setting));
        }
        return projected;
    }

    private static string ResolveRenderingPreference(StoryFrameAuthorityFrame frame, VisualOpportunity? opportunity)
    {
        var instruction = $"{opportunity?.Type} {frame.ShotType} {frame.FrameRole}";
        return instruction.Contains("sky", StringComparison.OrdinalIgnoreCase) || instruction.Contains("chart", StringComparison.OrdinalIgnoreCase)
            ? "AccurateSkyGuide" : instruction.Contains("diagram", StringComparison.OrdinalIgnoreCase) ? "Infographic" : "Cinematic";
    }

    private static async Task<DocumentaryNarrativeReleaseCandidate> ReadCandidateAsync(string root, string variant, CancellationToken ct)
    {
        var path = CandidatePath(root, variant);
        if (!File.Exists(path)) Fail(Phase8AuthorityReasonCodes.VariantAuthorityMissing, $"Requested {variant} accepted release candidate is missing.");
        var candidate = await ReadAsync<DocumentaryNarrativeReleaseCandidate>(path, ct);
        if (!candidate.IsAccepted || !candidate.IsClean || !candidate.IsFullyResolved)
            Fail(Phase8AuthorityReasonCodes.NotCommitted, $"Requested {variant} narration candidate is not accepted and clean.");
        return candidate;
    }

    private static string CandidatePath(string root, string variant) => Path.Combine(root, "07-narration", variant, "accepted-release-candidate.json");
    private static string NormalizeVariant(string value) => value.Equals("long", StringComparison.OrdinalIgnoreCase) ? "Long" : value.Equals("short", StringComparison.OrdinalIgnoreCase) ? "Short" : value;
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) Fail(Phase8AuthorityReasonCodes.Missing, $"{label} is missing at '{path}'."); }
    private static async Task<T> ReadAsync<T>(string path, CancellationToken ct) =>
        await JsonSerializer.DeserializeAsync<T>(File.OpenRead(path), JsonOptions, ct) ?? throw new Phase8AuthorityException(Phase8AuthorityReasonCodes.NotCommitted, [$"'{path}' did not contain a typed committed artifact."]);
    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); }
    private static void Fail(string code, string message) => throw new Phase8AuthorityException(code, [message]);
}
