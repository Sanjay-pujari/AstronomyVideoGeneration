using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Publishes renderer-neutral motion metadata bound to frozen timing and certified visuals.</summary>
internal static class Phase17MotionAuthorityPublisher
{
    internal const string Accepted = "P17_MOTION_AUTHORITY_ACCEPTED";
    internal const string UpstreamPhase16Invalid = "P17_UPSTREAM_PHASE16_INVALID";
    internal const string PhysicalEvidenceInvalid = "P17_VISUAL_PHYSICAL_EVIDENCE_INVALID";
    internal const string MotionPolicy = "motion-profile-selector-v2/bounded-amplitude-1.0";
    internal const string SafetyPolicy = "certified-regions-fail-static/1.0";
    private const string Schema = "phase17.motion-plan/1.0";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<Phase17PublicationResult> ExecuteAsync(string root, string language,
        bool overwrite, CancellationToken ct)
    {
        language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();
        SweepStaleTransactions(root);
        var p16Root = Path.Combine(root, "16-duration-calibration", language);
        var timelinePath = Path.Combine(p16Root, "calibrated-scene-timeline.json");
        var p16ManifestPath = Path.Combine(p16Root, "phase16-manifest.json");
        var p16ReportPath = Path.Combine(p16Root, "phase16-publication-report.json");
        var p16ValidationPath = Path.Combine(root, "validation", "phase-16-validation.json");
        var p8Path = Path.Combine(root, "08-scene-assets", "scene-asset-manifest.json");
        var p8ReportPath = Path.Combine(root, "08-scene-assets", "phase8-publication-report.json");
        var p9Path = Path.Combine(root, "09-long-scenes", "long-scene-image-manifest.json");
        var p9ReportPath = Path.Combine(root, "09-long-scenes", "phase9-publication-report.json");
        var p10Root = Path.Combine(root, "10-scene-validation");
        var p10Path = Path.Combine(p10Root, "scene-asset-certification.json");
        var p10DiagnosticsPath = Path.Combine(p10Root, "phase10-authority-diagnostics.json");
        var p10ReportPath = Path.Combine(p10Root, "phase10-publication-report.json");
        var inputs = new[] { timelinePath, p16ManifestPath, p16ReportPath, p16ValidationPath, p8Path,
            p8ReportPath, p9Path, p9ReportPath, p10Path, p10DiagnosticsPath, p10ReportPath };
        if (inputs.Any(path => !File.Exists(path)))
            Fail(inputs.Take(4).Any(path => !File.Exists(path)) ? UpstreamPhase16Invalid : "P17_UPSTREAM_VISUAL_AUTHORITY_INVALID",
                "A required committed authority artifact is missing.");

        using var timelineDoc = await ReadDocument(timelinePath, ct);
        using var p16ManifestDoc = await ReadDocument(p16ManifestPath, ct);
        using var p16ReportDoc = await ReadDocument(p16ReportPath, ct);
        using var p16ValidationDoc = await ReadDocument(p16ValidationPath, ct);
        var p16Checksum = RequiredString(timelineDoc.RootElement, "authorityChecksum", UpstreamPhase16Invalid);
        ValidatePhase16(p16ManifestDoc.RootElement, p16ReportDoc.RootElement, p16ValidationDoc.RootElement, p16Checksum);

        var p8 = await Read<SceneAssetManifest>(p8Path, ct);
        var p9 = await Read<LongSceneImageManifest>(p9Path, ct);
        var p10 = await Read<SceneAssetCertification>(p10Path, ct);
        using var p8Report = await ReadDocument(p8ReportPath, ct);
        using var p9Report = await ReadDocument(p9ReportPath, ct);
        using var p10Report = await ReadDocument(p10ReportPath, ct);
        using var p10Diagnostics = await ReadDocument(p10DiagnosticsPath, ct);
        ValidateVisualEvidence(p8, p9, p10, p8Report.RootElement, p9Report.RootElement,
            p10Report.RootElement, p10Diagnostics.RootElement);
        var visualChecksum = Hash($"{p8.DeterministicChecksum}\n{p9.DeterministicChecksum}\n{p10.DeterministicChecksum}");

        var finalRoot = Path.Combine(root, "17-motion", language);
        var validationRoot = Path.Combine(root, "validation");
        var validationPath = Path.Combine(validationRoot, "phase-17-validation.json");
        var existingPlanPath = Path.Combine(finalRoot, "short", "motion-plan.json");
        var existingLongPath = Path.Combine(finalRoot, "long", "motion-plan.json");
        var existingManifestPath = Path.Combine(finalRoot, "phase17-manifest.json");
        var existingReportPath = Path.Combine(finalRoot, "phase17-publication-report.json");
        var previousAuthorityExisted = Directory.Exists(finalRoot);
        var reuseEligibleBeforeOverwrite = false;
        Phase17MotionPlan? existingShortPlan = null;
        if (File.Exists(existingPlanPath) && File.Exists(existingLongPath) &&
            File.Exists(existingManifestPath) && File.Exists(existingReportPath))
        {
            existingShortPlan = await Read<Phase17MotionPlan>(existingPlanPath, ct);
            var existingLongPlan = await Read<Phase17MotionPlan>(existingLongPath, ct);
            reuseEligibleBeforeOverwrite = ExistingRequestIdentityMatches(existingShortPlan, p16Checksum, visualChecksum) &&
                ExistingRequestIdentityMatches(existingLongPlan, p16Checksum, visualChecksum) &&
                existingLongPlan.AuthorityChecksum == existingShortPlan.AuthorityChecksum &&
                await IsAcceptedPublication(existingManifestPath, existingReportPath,
                    existingShortPlan.AuthorityChecksum, ct);
        }

        // An explicit overwrite is an execution command, not an identity hint. It must reach the
        // candidate builder and transactional replacement even when deterministic output is equal.
        if (ShouldReuseExistingAuthority(overwrite, reuseEligibleBeforeOverwrite))
        {
            var reused = Result(inputs, Directory.EnumerateFiles(finalRoot, "*", SearchOption.AllDirectories)
                .Append(validationPath).ToArray(), false, true, false, p16Checksum, visualChecksum,
                existingShortPlan!.AuthorityChecksum);
            Directory.CreateDirectory(validationRoot);
            await WriteValidation(validationPath, reused, ct);
            return reused;
        }

        var shortScenes = ReadScenes(timelineDoc.RootElement, "short");
        var longScenes = ReadScenes(timelineDoc.RootElement, "long");
        if (shortScenes.Concat(longScenes).Any(x => !x.Language.Equals(language, StringComparison.OrdinalIgnoreCase)))
            Fail("P17_PHASE16_TIMING_BINDING_INVALID", "A Phase 16 scene belongs to a different language authority.");
        var shortVisuals = p8.Assets.Where(x => x.Variant.Equals("Short", StringComparison.OrdinalIgnoreCase)).ToArray();
        var longVisuals = p9.Images.ToArray();
        ValidateBijection(shortScenes, shortVisuals.Select(x => x.SceneId), "Short");
        ValidateBijection(longScenes, longVisuals.Select(x => x.SceneId), "Long");
        var shortById = shortVisuals.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var longById = longVisuals.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var shortEntries = new List<Phase17MotionEntry>();
        foreach (var scene in shortScenes.OrderBy(x => x.Sequence))
        {
            var visual = shortById[scene.SceneId];
            shortEntries.Add(await BuildEntry(root, root, scene, visual.PhysicalPath, visual.Checksum,
                visual.Width, visual.Height, p16Checksum, visualChecksum, p10.DeterministicChecksum,
                p8.DeterministicChecksum, p9.DeterministicChecksum, "Phase8.SceneAssetManifestItem.Checksum", shortScenes.Count, inputs, ct));
        }
        var longEntries = new List<Phase17MotionEntry>();
        foreach (var scene in longScenes.OrderBy(x => x.Sequence))
        {
            var visual = longById[scene.SceneId];
            // Phase 9 defines PhysicalPath relative to its package root (the same rule used by
            // LongSceneImageManifestValidator). It is not relative to the execution root.
            longEntries.Add(await BuildEntry(root, Path.Combine(root, "09-long-scenes"), scene,
                visual.PhysicalPath, visual.PhysicalSha256, visual.Width, visual.Height, p16Checksum,
                visualChecksum, p10.DeterministicChecksum, p8.DeterministicChecksum, p9.DeterministicChecksum,
                "Phase9.LongSceneImageManifestItem.PhysicalSha256", longScenes.Count, inputs, ct));
        }
        ValidateEntries(shortEntries, "Short"); ValidateEntries(longEntries, "Long");
        var authorityChecksum = Hash(JsonSerializer.Serialize(new { Schema, language, MotionPolicy, SafetyPolicy,
            sourcePhase16AuthorityChecksum = p16Checksum, sourceVisualAuthorityChecksum = visualChecksum,
            @short = shortEntries, @long = longEntries }, Json));
        var shortPlan = new Phase17MotionPlan(Schema, language, "Short", shortEntries.Count, MotionPolicy,
            SafetyPolicy, p16Checksum, visualChecksum, shortEntries, authorityChecksum);
        var longPlan = new Phase17MotionPlan(Schema, language, "Long", longEntries.Count, MotionPolicy,
            SafetyPolicy, p16Checksum, visualChecksum, longEntries, authorityChecksum);

        var transactionId = Guid.NewGuid().ToString("N"); // Filesystem transaction identity is never semantic authority input.
        var stage = Path.Combine(root, "17-motion", ".staging", transactionId, language);
        var backup = Path.Combine(root, "17-motion", ".backup", transactionId, language);
        var replacingExistingAuthority = previousAuthorityExisted;
        try
        {
            Directory.CreateDirectory(Path.Combine(stage, "short")); Directory.CreateDirectory(Path.Combine(stage, "long"));
            await Write(Path.Combine(stage, "short", "motion-plan.json"), shortPlan, ct);
            await Write(Path.Combine(stage, "long", "motion-plan.json"), longPlan, ct);
            await Write(Path.Combine(stage, "phase17-manifest.json"), new { schemaVersion = "phase17.manifest/1.0",
                language, authorityChecksum, sourcePhase16AuthorityChecksum = p16Checksum,
                sourceVisualAuthorityChecksum = visualChecksum, motionPolicyVersion = MotionPolicy,
                safetyPolicyVersion = SafetyPolicy, publicationState = "Committed", publicationCommitted = true,
                validationStatus = "Valid", downstreamReady = true, renderCallsThisPhase = 0,
                artifacts = new[] { "short/motion-plan.json", "long/motion-plan.json" } }, ct);
            await Write(Path.Combine(stage, "phase17-authority-diagnostics.json"), new { schemaVersion = "phase17.diagnostics/1.0",
                language, shortSceneCount = shortEntries.Count, longSceneCount = longEntries.Count,
                staticFallbackCount = shortEntries.Count + longEntries.Count, renderCallsThisPhase = 0,
                audioFilesOpened = 0, srtFilesRead = 0, sourcePhase16AuthorityChecksum = p16Checksum,
                sourceVisualAuthorityChecksum = visualChecksum, candidateValidationPassed = true,
                candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true,
                overwriteExistingRequested = overwrite, overwriteExistingResolved = overwrite,
                reuseEligibleBeforeOverwrite, reuseSuppressedByOverwrite = overwrite && reuseEligibleBeforeOverwrite,
                previousAuthorityExisted, candidateGenerated = true, replacedExistingAuthority = replacingExistingAuthority,
                transactionId,
                authorityChecksum, downstreamReady = true, canonicalOwnedRoots = new[] { $"17-motion/{language}" },
                compatibilityProjectionPaths = new[] { "motion/motion-plan.json" } }, ct);
            await Write(Path.Combine(stage, "phase17-publication-report.json"), Publication(authorityChecksum), ct);
            _ = await Read<Phase17MotionPlan>(Path.Combine(stage, "short", "motion-plan.json"), ct);
            await Phase16DurationCalibrationPublisher.ReplaceCommittedDirectoryAsync(stage, finalRoot, backup, async () =>
            {
                var committed = await Read<Phase17MotionPlan>(Path.Combine(finalRoot, "long", "motion-plan.json"), ct);
                if (committed.AuthorityChecksum != authorityChecksum) Fail("P17_COMMITTED_READBACK_FAILED", "Committed checksum differs.");
            });
            Directory.CreateDirectory(validationRoot);
            var result = Result(inputs, Directory.EnumerateFiles(finalRoot, "*", SearchOption.AllDirectories).Append(validationPath).ToArray(),
                true, false, replacingExistingAuthority, p16Checksum, visualChecksum, authorityChecksum);
            await WriteValidation(validationPath, result, ct);
            return result;
        }
        finally
        {
            CleanupTransactionDirectory(Path.GetDirectoryName(stage)!);
            CleanupTransactionDirectory(Path.GetDirectoryName(backup)!);
        }
    }

    internal static bool ShouldReuseExistingAuthority(bool overwriteExisting, bool existingAuthorityValidAndMatching) =>
        !overwriteExisting && existingAuthorityValidAndMatching;

    private static bool ExistingRequestIdentityMatches(Phase17MotionPlan plan, string p16Checksum, string visualChecksum) =>
        plan.SchemaVersion == Schema && plan.MotionPolicyVersion == MotionPolicy && plan.SafetyPolicyVersion == SafetyPolicy &&
        plan.SourcePhase16AuthorityChecksum == p16Checksum && plan.SourceVisualAuthorityChecksum == visualChecksum;

    /// <summary>
    /// Removes abandoned transaction directories only when they are demonstrably empty. This is
    /// deliberately run before authority discovery so the reuse path receives the same housekeeping
    /// as publication without touching a committed language directory.
    /// </summary>
    internal static void SweepStaleTransactions(string root)
    {
        var motionRoot = Path.Combine(root, "17-motion");
        foreach (var transactionParentName in new[] { ".staging", ".backup" })
        {
            var transactionParent = Path.Combine(motionRoot, transactionParentName);
            if (!Directory.Exists(transactionParent)) continue;

            foreach (var transactionDirectory in Directory.EnumerateDirectories(transactionParent))
            {
                if (!Directory.EnumerateFileSystemEntries(transactionDirectory).Any())
                    Directory.Delete(transactionDirectory);
                else
                    Trace.TraceWarning("Phase 17 retained non-empty stale transaction directory: {0}", transactionDirectory);
            }

            if (!Directory.EnumerateFileSystemEntries(transactionParent).Any())
                Directory.Delete(transactionParent);
        }
    }

    private static async Task<Phase17MotionEntry> BuildEntry(string root, string physicalPathRoot, Phase16CalibratedScene scene,
        string path, string expectedHash, int width, int height, string p16Checksum, string visualChecksum,
        string p10Checksum, string p8Checksum, string p9Checksum, string expectedAuthoritySource,
        int sceneCount, IReadOnlyList<string> loadedInputs, CancellationToken ct)
    {
        if (scene.FinalSceneDurationMs <= 0 || scene.SceneEndMs - scene.SceneStartMs != scene.FinalSceneDurationMs)
            Fail("P17_PHASE16_TIMING_BINDING_INVALID", $"Invalid frozen scene window for {scene.SceneId}.");
        if (scene.Format.Equals("Short", StringComparison.OrdinalIgnoreCase) ? width >= height : width <= height)
            Fail("P17_VISUAL_ASPECT_INVALID", $"Certified aspect family is invalid for {scene.Format}/{scene.SceneId}.");
        var physical = Path.IsPathRooted(path) ? path : Path.Combine(physicalPathRoot, path.Replace('/', Path.DirectorySeparatorChar));
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(physical);
        string? actualHash = null; int? actualWidth = null; int? actualHeight = null;
        if (full.StartsWith(fullRoot, StringComparison.Ordinal) && File.Exists(full))
        {
            actualHash = await HashFile(full, ct);
            var info = await Image.IdentifyAsync(full, ct);
            actualWidth = info?.Width; actualHeight = info?.Height;
        }
        if (!full.StartsWith(fullRoot, StringComparison.Ordinal) || actualHash != expectedHash
            || actualWidth != width || actualHeight != height)
            throw new Phase17PhysicalEvidenceException(
                $"Physical visual does not match certification. sceneId={scene.SceneId}; format={scene.Format}; " +
                $"selectedVisualPath={full}; expectedPhysicalSha256={expectedHash}; actualPhysicalSha256={actualHash ?? "<missing>"}; " +
                $"expectedWidth={width}; actualWidth={actualWidth?.ToString() ?? "<missing>"}; expectedHeight={height}; " +
                $"actualHeight={actualHeight?.ToString() ?? "<missing>"}; expectedAuthoritySource={expectedAuthoritySource}; " +
                $"phase8AuthorityChecksum={p8Checksum}; phase9AuthorityChecksum={p9Checksum}; " +
                $"phase10CertificationChecksum={p10Checksum}", loadedInputs);
        var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        var selected = new MotionProfileSelector().SelectSemantic($"planetary conjunction {scene.SceneId}", scene.Sequence - 1, sceneCount);
        var role = selected.Kind.ToString();
        // Current visual authority has no certified focus/safe/overlay geometry. The mature candidate
        // is deliberately downgraded rather than guessing from pixels or allowing an unsafe crop.
        var transform = new Phase17NormalizedTransform(1d, 0d, 0d);
        var keyframes = new[] { new Phase17Keyframe(0d, transform), new Phase17Keyframe(1d, transform) };
        var cut = new Phase17Transition(Phase17TransitionType.Cut, 0);
        return new(scene.SceneId, scene.SceneAudioUnitId, scene.Format, scene.Sequence, scene.Language,
            scene.FinalSceneDurationMs, scene.SceneStartMs, scene.SceneEndMs, scene.SubtitleSegmentIds,
            scene.AudioSha256, relative, expectedHash, width, height,
            scene.Format.Equals("Short", StringComparison.OrdinalIgnoreCase) ? "Portrait" : "Landscape",
            p16Checksum, visualChecksum, p10Checksum, role, Phase17MotionType.Static, transform, transform,
            keyframes, Phase17Easing.Linear, null, null, Array.Empty<Phase17NormalizedRegion>(),
            Phase17SafetyDecision.StaticFallbackNoCertifiedFocus, true, cut, cut, MotionPolicy, SafetyPolicy);
    }

    private static void ValidatePhase16(JsonElement manifest, JsonElement report, JsonElement validation, string checksum)
    {
        RequireEqual(manifest, "authorityChecksum", checksum, UpstreamPhase16Invalid);
        RequireEqual(report, "authorityChecksum", checksum, UpstreamPhase16Invalid);
        RequireEqual(validation, "authorityChecksum", checksum, UpstreamPhase16Invalid);
        RequireEqual(validation, "status", "Succeeded", UpstreamPhase16Invalid);
        RequireEqual(validation, "reasonCode", "P16_DURATION_AUTHORITY_ACCEPTED", UpstreamPhase16Invalid);
        foreach (var name in new[] { "publicationCommitted", "committedReadbackPassed", "committedStateValidationPassed",
                     "semanticValidationPassed", "checksumValidationPassed", "manifestValidationPassed", "downstreamReady" })
        { RequireTrue(report, name, UpstreamPhase16Invalid); RequireTrue(validation, name, UpstreamPhase16Invalid); }
        RequireTrue(manifest, "publicationCommitted", UpstreamPhase16Invalid);
        RequireTrue(manifest, "downstreamReady", UpstreamPhase16Invalid);
        RequireEqual(manifest, "validationStatus", "Valid", UpstreamPhase16Invalid);
        RequireEqual(validation, "validationStatus", "Valid", UpstreamPhase16Invalid);
    }

    private static void ValidateVisualEvidence(SceneAssetManifest p8, LongSceneImageManifest p9,
        SceneAssetCertification p10, params JsonElement[] evidence)
    {
        if (!p8.PublicationState.Equals("Committed", StringComparison.OrdinalIgnoreCase) ||
            !p8.ValidationStatus.Equals("Valid", StringComparison.OrdinalIgnoreCase) ||
            !p9.PublicationState.Equals("Committed", StringComparison.OrdinalIgnoreCase) ||
            !p9.ValidationStatus.Equals("Valid", StringComparison.OrdinalIgnoreCase) || !p9.DownstreamReady ||
            !p10.PublicationState.Equals("Committed", StringComparison.OrdinalIgnoreCase) ||
            !p10.ValidationStatus.Equals("Valid", StringComparison.OrdinalIgnoreCase) || !p10.DownstreamReady ||
            p10.Phase8SceneAssetAuthorityChecksum != p8.DeterministicChecksum ||
            p10.Phase9LongSceneAuthorityChecksum != p9.DeterministicChecksum ||
            !p10.ShortCertification.PhysicalChecksumValidationPassed || !p10.LongCertification.PhysicalChecksumValidationPassed)
            Fail("P17_UPSTREAM_VISUAL_AUTHORITY_INVALID", "Visual certification lineage or committed state is invalid.");
        RequireVisualPublication(evidence[0], p8.DeterministicChecksum);
        RequireVisualPublication(evidence[1], p9.DeterministicChecksum);
        RequireVisualPublication(evidence[2], p10.DeterministicChecksum);
        foreach (var gate in new[] { "physicalChecksumValidationPassed", "lineageValidationPassed",
                     "dimensionValidationPassed", "scientificEvidenceValidationPassed" })
            RequireTrue(evidence[3], gate, "P17_UPSTREAM_VISUAL_AUTHORITY_INVALID");
        foreach (var item in evidence)
        {
            if (item.TryGetProperty("publicationCommitted", out var committed) && committed.ValueKind == JsonValueKind.False)
                Fail("P17_UPSTREAM_VISUAL_AUTHORITY_INVALID", "Visual publication is not committed.");
            if (item.TryGetProperty("downstreamReady", out var ready) && ready.ValueKind == JsonValueKind.False)
                Fail("P17_UPSTREAM_VISUAL_AUTHORITY_INVALID", "Visual authority is not downstream ready.");
            if (item.TryGetProperty("validationStatus", out var status) && status.GetString() != "Valid")
                Fail("P17_UPSTREAM_VISUAL_AUTHORITY_INVALID", "Visual validation is not valid.");
        }
    }
    private static void RequireVisualPublication(JsonElement report, string checksum)
    {
        var validation = HasTrue(report, "manifestValidationPassed") || HasTrue(report, "candidateValidationPassed");
        var reported = report.TryGetProperty("authorityChecksum", out var authority) ? authority.GetString()
            : report.TryGetProperty("manifestChecksum", out var manifest) ? manifest.GetString()
            : report.TryGetProperty("certificationChecksum", out var certification) ? certification.GetString() : null;
        if (!HasTrue(report, "publicationCommitted") || !validation || !HasTrue(report, "candidateReadbackPassed") ||
            !HasTrue(report, "committedReadbackPassed") || (reported is not null && !reported.Equals(checksum, StringComparison.OrdinalIgnoreCase)))
            Fail("P17_UPSTREAM_VISUAL_AUTHORITY_INVALID", "Visual publication evidence is invalid.");
    }

    private static List<Phase16CalibratedScene> ReadScenes(JsonElement root, string name) =>
        root.GetProperty(name).Deserialize<List<Phase16CalibratedScene>>(Json) ?? [];
    private static void ValidateBijection(IReadOnlyList<Phase16CalibratedScene> scenes, IEnumerable<string> visualIds, string format)
    {
        var ids = visualIds.ToArray();
        if (scenes.Select(x => x.SceneId).Distinct(StringComparer.Ordinal).Count() != scenes.Count ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Length ||
            !scenes.Select(x => x.SceneId).Order().SequenceEqual(ids.Order()))
            Fail("P17_VISUAL_BIJECTION_INVALID", $"Phase 16 and certified {format} visual identities are not bijective.");
    }
    private static void ValidateEntries(IReadOnlyList<Phase17MotionEntry> entries, string format)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var x = entries[i];
            if (x.Format != format || x.SceneEndMs - x.SceneStartMs != x.DurationMs || x.Keyframes.Count < 2 ||
                x.Keyframes[0].NormalizedTime != 0d || x.Keyframes[^1].NormalizedTime != 1d ||
                x.Keyframes.Zip(x.Keyframes.Skip(1)).Any(pair => pair.First.NormalizedTime >= pair.Second.NormalizedTime))
                Fail("P17_CANDIDATE_VALIDATION_FAILED", $"Invalid motion semantics for {x.SceneId}.");
        }
    }
    private static object Publication(string checksum) => new { schemaVersion = "phase17.publication/1.0",
        candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true,
        committedReadbackPassed = true, committedStateValidationPassed = true, semanticValidationPassed = true,
        checksumValidationPassed = true, manifestValidationPassed = true, validationStatus = "Valid",
        downstreamReady = true, authorityChecksum = checksum };
    private static async Task<bool> IsAcceptedPublication(string manifestPath, string reportPath,
        string checksum, CancellationToken ct)
    {
        using var manifest = await ReadDocument(manifestPath, ct);
        using var report = await ReadDocument(reportPath, ct);
        return HasString(manifest.RootElement, "authorityChecksum", checksum) &&
            HasString(report.RootElement, "authorityChecksum", checksum) &&
            HasTrue(manifest.RootElement, "publicationCommitted") && HasTrue(manifest.RootElement, "downstreamReady") &&
            HasTrue(report.RootElement, "publicationCommitted") && HasTrue(report.RootElement, "committedReadbackPassed") &&
            HasTrue(report.RootElement, "committedStateValidationPassed") && HasTrue(report.RootElement, "semanticValidationPassed") &&
            HasTrue(report.RootElement, "checksumValidationPassed") && HasTrue(report.RootElement, "manifestValidationPassed") &&
            HasTrue(report.RootElement, "downstreamReady");
    }
    private static Phase17PublicationResult Result(IReadOnlyList<string> inputs, IReadOnlyList<string> outputs,
        bool generated, bool reused, bool regenerated, string p16, string visual, string checksum) =>
        new(inputs, outputs, Accepted, "Phase 17 motion authority accepted.", generated, reused, regenerated,
            true, true, true, true, true, true, true, true, p16, visual, checksum, "Valid", true);
    private static Task WriteValidation(string path, Phase17PublicationResult result, CancellationToken ct) =>
        Write(path, new { phaseNo = 17, phaseName = "Governed Motion Authority", status = "Succeeded",
            result.ReasonCode, result.Reason, result.Generated, result.Reused, result.Regenerated,
            manifestValidationStatus = result.ValidationStatus, result.ValidationStatus,
            result.CandidateValidationPassed, result.CandidateReadbackPassed, result.PublicationCommitted,
            result.CommittedReadbackPassed, result.CommittedStateValidationPassed, result.SemanticValidationPassed,
            result.ChecksumValidationPassed, result.ManifestValidationPassed, result.DownstreamReady,
            inputFiles = result.LoadedAuthorityArtifacts, result.SourcePhase16AuthorityChecksum,
            result.SourceVisualAuthorityChecksum, result.AuthorityChecksum }, ct);
    private static void CleanupTransactionDirectory(string transactionRoot)
    {
        if (Directory.Exists(transactionRoot)) Directory.Delete(transactionRoot, true);
        var parent = Path.GetDirectoryName(transactionRoot);
        if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            Directory.Delete(parent);
    }
    private static async Task<T> Read<T>(string path, CancellationToken ct) =>
        JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new InvalidDataException($"Invalid JSON: {path}");
    private static async Task<JsonDocument> ReadDocument(string path, CancellationToken ct) =>
        JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
    private static async Task Write(string path, object value, CancellationToken ct) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false), ct);
    private static string RequiredString(JsonElement root, string name, string code) =>
        root.TryGetProperty(name, out var value) && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw Error(code, $"Missing {name}.");
    private static void RequireTrue(JsonElement root, string name, string code)
    { if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.True) Fail(code, $"{name} must be true."); }
    private static void RequireEqual(JsonElement root, string name, string expected, string code)
    { if (!root.TryGetProperty(name, out var value) || value.GetString() != expected) Fail(code, $"{name} does not match."); }
    private static bool HasTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static bool HasString(JsonElement root, string name, string expected) =>
        root.TryGetProperty(name, out var value) && value.GetString() == expected;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static async Task<string> HashFile(string path, CancellationToken ct)
    { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); }
    private static InvalidOperationException Error(string code, string message) => new($"{code}: {message}");
    private static void Fail(string code, string message) => throw Error(code, message);
}

internal sealed class Phase17PhysicalEvidenceException(string detail, IReadOnlyList<string> loadedAuthorityArtifacts)
    : InvalidOperationException($"{Phase17MotionAuthorityPublisher.PhysicalEvidenceInvalid}: {detail}")
{
    internal string ReasonCode => Phase17MotionAuthorityPublisher.PhysicalEvidenceInvalid;
    internal string Reason => detail;
    internal IReadOnlyList<string> LoadedAuthorityArtifacts { get; } = loadedAuthorityArtifacts;
}
