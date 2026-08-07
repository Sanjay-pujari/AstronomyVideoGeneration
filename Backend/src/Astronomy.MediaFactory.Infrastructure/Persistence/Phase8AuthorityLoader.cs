using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Strict, typed read boundary for the three committed authorities consumed by Phase 8.</summary>
public sealed class Phase8AuthorityLoader : IPhase8AuthorityLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Phase8AuthorityInput> LoadAsync(Phase8AuthorityLoadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = request.Diagnostics ?? new Phase8AuthorityLoadDiagnostics();
        request = request with { Diagnostics = diagnostics };
        var stage = "phase4AuthorityLoad";
        try
        {
            var blueprintPath = Path.Combine(request.OutputRoot, "04-blueprint", "documentary-blueprint.json");
            var storyPath = Path.Combine(request.OutputRoot, "06-story-frames", "story-frames.json");
            var indexPath = Path.Combine(request.OutputRoot, "06-story-frames", "story-frame-index.json");
            diagnostics.Phase4AuthorityLoadStarted = true;
            RequireFile(blueprintPath, "Phase 4 documentary blueprint");
            var blueprint = await ReadAsync<DocumentaryBlueprintAggregate>(blueprintPath, cancellationToken);
            if (!DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(blueprint))
                Fail(Phase8AuthorityReasonCodes.ChecksumMismatch, "Phase 4 aggregate checksum readback failed.");
            diagnostics.Phase4AuthorityLoaded = true;

            stage = "phase6AuthorityLoad";
            diagnostics.Phase6AuthorityLoadStarted = true;
            RequireFile(storyPath, "Phase 6 Story Frame authority");
            RequireFile(indexPath, "Phase 6 Story Frame committed index");
            var story = await ReadAsync<StoryFramesAuthority>(storyPath, cancellationToken);
            var index = await ReadAsync<StoryFrameIndex>(indexPath, cancellationToken);
            if (!string.Equals(story.SemanticChecksum, StoryFrameAuthorityChecksum.Authority(story), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(index.Checksum, StoryFrameAuthorityChecksum.Index(index), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(index.SourceStoryFramesChecksum, story.SemanticChecksum, StringComparison.OrdinalIgnoreCase))
                Fail(Phase8AuthorityReasonCodes.ChecksumMismatch, "Phase 6 authority/index checksum readback failed.");
            diagnostics.Phase6AuthorityLoaded = true;

            var variants = request.RequestedVariants.Select(NormalizeVariant).Distinct(StringComparer.Ordinal).ToArray();
            if (variants.Length == 0) Fail(Phase8AuthorityReasonCodes.VariantAuthorityMissing, "No Phase 8 variant was requested.");
            if (!string.Equals(story.PlanId, request.PlanId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(story.EventId, request.EventId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(story.Language, request.Language, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(blueprint.PlanId, request.PlanId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(blueprint.EventId, request.EventId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(blueprint.Language, request.Language, StringComparison.OrdinalIgnoreCase))
                Fail(Phase8AuthorityReasonCodes.IdentityMismatch, "Plan, event, or language differs across Phase 4/6 and the execution request.");
            foreach (var variant in variants)
                if (!story.RequestedVariants.Contains(variant, StringComparer.OrdinalIgnoreCase))
                    Fail(Phase8AuthorityReasonCodes.VariantAuthorityMissing, $"Phase 6 has no committed {variant} authority.");

            Phase7AcceptedReleaseCandidate? longCandidate = null;
            Phase7AcceptedReleaseCandidate? shortCandidate = null;
            string? longChecksum = null;
            string? shortChecksum = null;
            if (variants.Contains("Long"))
            {
                stage = "longNarrationAuthorityLoad";
                diagnostics.LongNarrationAuthorityLoadStarted = true;
                (longCandidate, longChecksum) = await ReadCandidateAsync(request, story, "long", cancellationToken);
                diagnostics.LongNarrationAuthorityLoaded = true;
            }
            if (variants.Contains("Short"))
            {
                stage = "shortNarrationAuthorityLoad";
                diagnostics.ShortNarrationAuthorityLoadStarted = true;
                (shortCandidate, shortChecksum) = await ReadCandidateAsync(request, story, "short", cancellationToken);
                diagnostics.ShortNarrationAuthorityLoaded = true;
            }

            stage = "authorityProjection";
            diagnostics.AuthorityProjectionStarted = true;
            var longScenes = variants.Contains("Long") ? Project("Long", story, blueprint, longCandidate!, longChecksum!) : [];
            var shortScenes = variants.Contains("Short") ? Project("Short", story, blueprint, shortCandidate!, shortChecksum!) : [];
            diagnostics.AuthorityProjectionCompleted = true;
            return new(request.PlanId, story.ExecutionId, request.EventId, request.Language, blueprint,
                blueprint.DeterministicChecksum, story, story.SemanticChecksum,
                longCandidate, longChecksum, shortCandidate, shortChecksum, variants, longScenes, shortScenes);
        }
        catch (Exception ex)
        {
            diagnostics.AuthorityFailureStage = stage;
            diagnostics.AuthorityFailureType = ex.GetType().Name;
            diagnostics.AuthorityFailureMessage = ex.Message;
            throw;
        }
    }

    private static IReadOnlyList<Phase8SceneRequirement> Project(string variant, StoryFramesAuthority authority,
        DocumentaryBlueprintAggregate aggregate, Phase7AcceptedReleaseCandidate candidate, string candidateChecksum)
    {
        var blueprint = variant.Equals("Long", StringComparison.OrdinalIgnoreCase) ? aggregate.LongBlueprint : aggregate.ShortBlueprint;
        var frames = authority.Frames.Where(x => x.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.SceneId, StringComparer.Ordinal).OrderBy(x => x.Min(f => f.SceneNumber)).ToArray();
        if (candidate.Scenes.Count != frames.Length || candidate.AcceptedSceneCount != candidate.Scenes.Count)
            Fail(Phase8AuthorityReasonCodes.NarrationSceneMappingFailed, $"Accepted {variant} narration scene count does not match committed Story Frames.");
        var blueprintScenes = blueprint.Scenes.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var unused = candidate.Scenes.ToList();
        var projected = new List<Phase8SceneRequirement>();
        foreach (var group in frames)
        {
            var frame = group.OrderBy(x => x.FrameNumber).First();
            if (!blueprintScenes.TryGetValue(group.Key, out var scene))
                Fail(Phase8AuthorityReasonCodes.SceneLineageMismatch, $"Story Frame scene '{group.Key}' has no Phase 4 blueprint scene.");
            // Governed precedence: SceneId, then StoryFrameId, then BlueprintSceneId. Order is only checked after identity mapping.
            var narration = Unique(unused.Where(x => x.SceneId == group.Key), variant, group.Key)
                ?? Unique(unused.Where(x => group.Any(f => f.FrameId == x.StoryFrameId)), variant, group.Key)
                ?? Unique(unused.Where(x => x.BlueprintSceneId == scene.SceneId), variant, group.Key);
            if (narration is null || string.IsNullOrWhiteSpace(narration.NarrationText))
                Fail(Phase8AuthorityReasonCodes.NarrationSceneMappingFailed, $"Accepted {variant} narration has no unique identity mapping for scene '{group.Key}'.");
            unused.Remove(narration);
            if (narration.SceneNumber != frame.SceneNumber)
                Fail(Phase8AuthorityReasonCodes.NarrationSceneMappingFailed, $"Accepted {variant} narration order differs at scene '{group.Key}'.");
            var visual = string.Join(" ", group.Select(x => x.VisualIntent).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal));
            var opportunity = scene.VisualOpportunities.FirstOrDefault();
            projected.Add(new(variant, group.Key, scene.SceneId, frame.FrameId, frame.SceneNumber,
                frame.SceneRole, frame.NarrativeStage, scene.SceneObjective.Summary, visual,
                frame.CameraDirection, group.SelectMany(x => x.ImageRequirements).Distinct(StringComparer.Ordinal).ToArray(),
                group.SelectMany(x => x.KnowledgeReferenceIds).Concat(scene.KnowledgeReferences.Select(x => x.KnowledgeEntryId)).Distinct(StringComparer.Ordinal).ToArray(),
                narration.NarrationText, narration.SceneId, opportunity?.Type ?? "Cinematic", "scene-background",
                ResolveRenderingPreference(frame, opportunity), string.IsNullOrWhiteSpace(frame.Setting) ? null : frame.Setting,
                NarrationReleaseCandidateChecksum: candidateChecksum));
        }
        if (unused.Count != 0) Fail(Phase8AuthorityReasonCodes.NarrationSceneMappingFailed, $"Accepted {variant} narration contains unmapped scenes.");
        return projected;
    }

    private static Phase7AcceptedNarrationScene? Unique(IEnumerable<Phase7AcceptedNarrationScene> matches, string variant, string sceneId)
    {
        var values = matches.Take(2).ToArray();
        if (values.Length > 1) Fail(Phase8AuthorityReasonCodes.NarrationSceneMappingFailed, $"Accepted {variant} narration has duplicate identity mappings for scene '{sceneId}'.");
        return values.SingleOrDefault();
    }

    private static string ResolveRenderingPreference(StoryFrameAuthorityFrame frame, VisualOpportunity? opportunity)
    {
        var instruction = $"{opportunity?.Type} {frame.ShotType} {frame.FrameRole}";
        return instruction.Contains("sky", StringComparison.OrdinalIgnoreCase) || instruction.Contains("chart", StringComparison.OrdinalIgnoreCase)
            ? "AccurateSkyGuide" : instruction.Contains("diagram", StringComparison.OrdinalIgnoreCase) ? "Infographic" : "Cinematic";
    }

    private static async Task<(Phase7AcceptedReleaseCandidate Candidate, string Checksum)> ReadCandidateAsync(
        Phase8AuthorityLoadRequest request, StoryFramesAuthority story, string variantPath, CancellationToken ct)
    {
        var path = CandidatePath(request.OutputRoot, variantPath);
        if (!File.Exists(path)) Fail(Phase8AuthorityReasonCodes.VariantAuthorityMissing, $"Requested {variantPath} accepted release candidate is missing.");
        var candidate = await ReadAsync<Phase7AcceptedReleaseCandidate>(path, ct);
        var variant = NormalizeVariant(variantPath);
        if (!string.Equals(candidate.Variant, variant, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.PlanId, request.PlanId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.ExecutionId, story.ExecutionId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.EventId, request.EventId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.Language, request.Language, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.SourceStoryFramesAuthorityId, story.AuthorityId, StringComparison.Ordinal)
            || !string.Equals(candidate.SourceStoryFramesAuthorityChecksum, story.SemanticChecksum, StringComparison.OrdinalIgnoreCase))
            Fail(Phase8AuthorityReasonCodes.IdentityMismatch, $"Requested {variant} narration identity or lineage differs from Phase 8 authorities.");
        if (!candidate.AcceptanceResult.TryGetProperty("accepted", out var accepted) || accepted.ValueKind != JsonValueKind.True)
            Fail(Phase8AuthorityReasonCodes.NotCommitted, $"Requested {variant} narration candidate is not accepted.");
        if (candidate.Scenes.Count == 0 || candidate.Scenes.Any(x => string.IsNullOrWhiteSpace(x.SceneId) || string.IsNullOrWhiteSpace(x.NarrationText)))
            Fail(Phase8AuthorityReasonCodes.NarrationSceneMappingFailed, $"Requested {variant} narration contains empty scene identity or text.");
        var semantic = Phase7NarrationReleaseCandidateChecksum.ComputeScenes(candidate.Scenes);
        var physical = await HashFileAsync(path, ct);
        var publication = await ReadPublicationAsync(request.OutputRoot, variantPath, ct);
        request.Diagnostics!.NarrationChecksumDiagnostics = JsonSerializer.Serialize(new
        {
            variant, acceptedCandidatePath = path, candidateFileExists = true,
            candidateFileLength = new FileInfo(path).Length,
            candidateStoredDeterministicChecksum = candidate.DeterministicChecksum,
            candidateRecomputedDeterministicChecksum = semantic,
            candidatePhysicalSha256 = physical,
            phase7ManifestPath = publication.ManifestPath,
            phase7ManifestCandidateChecksum = publication.ExpectedPhysicalSha256,
            phase7CertificationPath = publication.CertificationPath,
            phase7CertificationPassed = publication.AcceptancePassed && publication.ChecksumsPassed,
            phase7PhysicalReadbackPassed = publication.PhysicalReadbackPassed,
            phase7DownstreamReady = publication.DownstreamReady,
            checksumImplementationUsedByPhase8 = Phase7NarrationReleaseCandidateChecksum.ImplementationName,
            checksumImplementationOwnedByPhase7 = Phase7NarrationReleaseCandidateChecksum.ImplementationName,
            checksumInputsEquivalent = true,
            checksumMismatchReason = candidate.DeterministicChecksum.Equals(semantic, StringComparison.OrdinalIgnoreCase) ? "None" : "Phase7SemanticChecksumMismatch"
        }, JsonOptions);
        if (!string.Equals(candidate.DeterministicChecksum, semantic, StringComparison.OrdinalIgnoreCase))
            Fail(Phase8AuthorityReasonCodes.NarrationCandidateSemanticChecksumMismatch, $"{Phase8AuthorityReasonCodes.ChecksumMismatch}: Requested {variant} narration deterministic checksum failed using the Phase 7 canonical scene projection.");
        if (string.IsNullOrWhiteSpace(publication.ExpectedPhysicalSha256))
            Fail(Phase8AuthorityReasonCodes.NarrationManifestMismatch, $"Phase 7 manifest does not reference the requested {variant} candidate.");
        if (!string.Equals(publication.ExpectedPhysicalSha256, physical, StringComparison.OrdinalIgnoreCase))
            Fail(Phase8AuthorityReasonCodes.NarrationCandidatePhysicalChecksumMismatch, $"{Phase8AuthorityReasonCodes.ChecksumMismatch}: Requested {variant} narration bytes differ from the Phase 7 manifest.");
        ValidatePublication(publication, variant);
        return (candidate, physical);
    }

    private sealed record PublicationEvidence(string ManifestPath, string CertificationPath, string? ExpectedPhysicalSha256,
        bool ManifestDownstreamReady, bool AcceptancePassed, bool PhysicalReadbackPassed, bool ChecksumsPassed, bool DownstreamReady);

    private static async Task<PublicationEvidence> ReadPublicationAsync(string root, string variant, CancellationToken ct)
    {
        var manifestPath = Path.Combine(root, "07-narration", "narration-manifest.json");
        var certificationPath = Path.Combine(root, "07-narration", "narration-certification.json");
        RequireFile(manifestPath, "Phase 7 narration manifest");
        RequireFile(certificationPath, "Phase 7 narration certification");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
        using var certification = JsonDocument.Parse(await File.ReadAllTextAsync(certificationPath, ct));
        var key = $"{variant}/accepted-release-candidate.json";
        var expected = manifest.RootElement.TryGetProperty("candidateChecksums", out var candidates) && candidates.TryGetProperty(key, out var checksum)
            ? checksum.GetString() : null;
        return new(manifestPath, certificationPath, expected,
            IsTrue(manifest.RootElement, "downstreamReady"), IsTrue(certification.RootElement, "acceptancePassed"),
            IsTrue(certification.RootElement, "physicalReadbackPassed"), IsTrue(certification.RootElement, "checksumsPassed"),
            IsTrue(certification.RootElement, "downstreamReady"));
    }

    private static void ValidatePublication(PublicationEvidence evidence, string variant)
    {
        if (!evidence.ManifestDownstreamReady || !evidence.AcceptancePassed || !evidence.PhysicalReadbackPassed || !evidence.ChecksumsPassed || !evidence.DownstreamReady)
            Fail(Phase8AuthorityReasonCodes.NarrationCertificationInvalid, $"Requested {NormalizeVariant(variant)} narration publication is not certified for downstream use.");
    }

    private static bool IsTrue(JsonElement value, string property) =>
        value.TryGetProperty(property, out var result) && result.ValueKind == JsonValueKind.True;

    private static string CandidatePath(string root, string variant) => Path.Combine(root, "07-narration", variant, "accepted-release-candidate.json");
    private static string NormalizeVariant(string value) => value.Equals("long", StringComparison.OrdinalIgnoreCase) ? "Long" : value.Equals("short", StringComparison.OrdinalIgnoreCase) ? "Short" : value;
    private static void RequireFile(string path, string label) { if (!File.Exists(path)) Fail(Phase8AuthorityReasonCodes.Missing, $"{label} is missing at '{path}'."); }
    private static async Task<T> ReadAsync<T>(string path, CancellationToken ct)
    { await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct) ?? throw new Phase8AuthorityException(Phase8AuthorityReasonCodes.NotCommitted, [$"'{path}' did not contain a typed committed artifact."]); }
    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); }
    private static void Fail(string code, string message) => throw new Phase8AuthorityException(code, [message]);
}
