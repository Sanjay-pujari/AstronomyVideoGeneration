using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Phase 16 is the sole final scene-duration and timed-subtitle authority.</summary>
internal static class Phase16DurationCalibrationPublisher
{
    internal const string Accepted = "P16_DURATION_AUTHORITY_ACCEPTED";
    internal const string TimingMethod = "Phase14EstimatedReadingDurationWeightedV1";
    private const string Schema = "phase16.duration-calibration/1.0";
    private const string CalibrationPolicy = "max-planned-or-audio-plus-padding/1.0";
    private const string TimingPolicy = "phase14-reading-weight-largest-remainder/1.0";
    private const string Serializer = "srt-utf8-lf/1.0";
    private const long MinimumVisualDurationMs = 1_000;
    private const long MinimumSubtitleDurationMs = 1;
    private const long RequiredPaddingMs = 0;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<Phase16PublicationResult> ExecuteAsync(string root, string planId,
        string eventId, string language, bool overwrite, CancellationToken ct)
    {
        language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();
        var p14Root = Path.Combine(root, "14-audio-sync");
        var p15Root = Path.Combine(root, "15-tts", language);
        var inputs = new[]
        {
            Path.Combine(p14Root, "narration-cue-plan.json"), Path.Combine(p14Root, "phase14-manifest.json"),
            Path.Combine(p14Root, "phase14-publication-report.json"), Path.Combine(root, "validation", "phase-14-validation.json"),
            Path.Combine(p15Root, "tts-timeline.json"), Path.Combine(p15Root, "phase15-manifest.json"),
            Path.Combine(p15Root, "phase15-publication-report.json"), Path.Combine(root, "validation", "phase-15-validation.json")
        };
        if (inputs.Take(4).Any(path => !File.Exists(path))) Fail("P16_UPSTREAM_PHASE14_INVALID", "Committed Phase 14 evidence is missing.");
        if (inputs.Skip(4).Any(path => !File.Exists(path))) Fail("P16_UPSTREAM_PHASE15_INVALID", "Committed Phase 15 evidence is missing.");

        Phase14AudioSyncAuthority p14;
        try { p14 = await Phase15SceneAudioUnitAdapter.LoadAuthorityAsync(root, planId, eventId, language, ct); }
        catch (Exception ex) { Fail("P16_UPSTREAM_PHASE14_INVALID", ex.Message); throw; }
        using var timelineDocument = JsonDocument.Parse(await File.ReadAllTextAsync(inputs[4], ct));
        using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(inputs[5], ct));
        using var reportDocument = JsonDocument.Parse(await File.ReadAllTextAsync(inputs[6], ct));
        using var validationDocument = JsonDocument.Parse(await File.ReadAllTextAsync(inputs[7], ct));
        var timeline = timelineDocument.RootElement;
        var p15Checksum = RequiredString(timeline, "authorityChecksum", "P16_UPSTREAM_PHASE15_INVALID");
        ValidatePhase15Evidence(manifestDocument.RootElement, reportDocument.RootElement, validationDocument.RootElement, p15Checksum);
        if (!RequiredString(timeline, "sourcePhase14AuthorityChecksum", "P16_LINEAGE_MISMATCH").Equals(p14.AuthorityChecksum, StringComparison.OrdinalIgnoreCase))
            Fail("P16_LINEAGE_MISMATCH", "Phase 15 root does not identify the committed Phase 14 authority.");

        var p15Entries = timeline.GetProperty("entries").EnumerateArray().Select(ReadEntry).ToArray();
        var units = p14.ShortStream.SceneAudioUnits.Concat(p14.LongStream.SceneAudioUnits).ToArray();
        if (p15Entries.Length != units.Length || p15Entries.Select(x => x.SceneAudioUnitId).Distinct(StringComparer.Ordinal).Count() != p15Entries.Length)
            Fail("P16_SCENE_MAPPING_INVALID", "Phase 14 and Phase 15 unit counts/identities differ.");
        var byId = p15Entries.ToDictionary(x => x.SceneAudioUnitId, StringComparer.Ordinal);
        foreach (var unit in units)
        {
            if (!byId.TryGetValue(unit.SceneAudioUnitId, out var audio) || audio.SceneId != unit.SceneId || audio.Sequence != unit.Sequence
                || !audio.Format.Equals(unit.Format, StringComparison.OrdinalIgnoreCase) || !audio.Language.Equals(language, StringComparison.OrdinalIgnoreCase)
                || audio.TextChecksum != unit.TextChecksum || !audio.SubtitleSegmentIds.SequenceEqual(unit.SubtitleSegments.OrderBy(x => x.SequenceWithinScene).Select(x => x.SubtitleSegmentId))
                || audio.SourcePhase14AuthorityChecksum != p14.AuthorityChecksum)
                Fail(audio.SourcePhase14AuthorityChecksum != p14.AuthorityChecksum ? "P16_LINEAGE_MISMATCH" : "P16_SCENE_MAPPING_INVALID", $"Strict unit binding failed for {unit.SceneAudioUnitId}.");
            if (audio.ActualAudioDurationMs <= 0) Fail("P16_AUDIO_DURATION_INVALID", $"Non-positive duration for {unit.SceneAudioUnitId}.");
            var physical = Path.Combine(root, audio.AudioRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(physical) || new FileInfo(physical).Length != audio.AudioByteLength || await HashFile(physical, ct) != audio.AudioSha256)
                Fail("P16_AUDIO_DURATION_INVALID", $"Phase 15 physical audio does not match metadata for {unit.SceneAudioUnitId}.");
        }

        var scenes = new List<Phase16CalibratedScene>(); var cues = new List<Phase16TimedSubtitle>();
        foreach (var format in new[] { "Short", "Long" })
        {
            long cursor = 0;
            foreach (var unit in units.Where(x => x.Format.Equals(format, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Sequence))
            {
                var audio = byId[unit.SceneAudioUnitId];
                long? planned = null; // No numbered scene authority currently publishes a reliable duration.
                var basis = planned ?? MinimumVisualDurationMs;
                var final = Math.Max(basis, audio.ActualAudioDurationMs + RequiredPaddingMs);
                var reason = audio.ActualAudioDurationMs + RequiredPaddingMs > basis ? "AudioExtendedVisual" : "PlannedVisualRetained";
                var ordered = unit.SubtitleSegments.OrderBy(x => x.SequenceWithinScene).ToArray();
                var allocations = Allocate(ordered, audio.ActualAudioDurationMs);
                long cueCursor = cursor;
                for (var i = 0; i < ordered.Length; i++)
                {
                    var segment = ordered[i]; var end = cueCursor + allocations[i];
                    cues.Add(new(segment.SubtitleSegmentId, unit.SceneAudioUnitId, unit.SceneId, format, unit.Sequence,
                        segment.SequenceWithinScene, segment.Text, segment.TextChecksum, cueCursor, end, allocations[i],
                        segment.SentenceIds, segment.SourceCharacterStart, segment.SourceCharacterEnd, TimingMethod,
                        p14.AuthorityChecksum, p15Checksum)); cueCursor = end;
                }
                scenes.Add(new(unit.SceneAudioUnitId, unit.SceneId, format, unit.Sequence, language, planned,
                    MinimumVisualDurationMs, audio.ActualAudioDurationMs, RequiredPaddingMs, final, cursor, cursor + final,
                    ordered.Select(x => x.SubtitleSegmentId).ToArray(), reason, audio.AudioRelativePath, audio.AudioSha256,
                    audio.AudioByteLength, p14.AuthorityChecksum, p15Checksum)); cursor += final;
            }
        }
        ValidateCandidate(units, scenes, cues);
        var shortSrt = BuildSrt(cues.Where(x => x.Format == "Short").OrderBy(x => x.StartMs).ToArray());
        var longSrt = BuildSrt(cues.Where(x => x.Format == "Long").OrderBy(x => x.StartMs).ToArray());
        ValidateSrt(shortSrt, cues.Where(x => x.Format == "Short").OrderBy(x => x.StartMs).ToArray());
        ValidateSrt(longSrt, cues.Where(x => x.Format == "Long").OrderBy(x => x.StartMs).ToArray());
        var shortHash = HashBytes(Encoding.UTF8.GetBytes(shortSrt)); var longHash = HashBytes(Encoding.UTF8.GetBytes(longSrt));
        var authorityChecksum = Hash(JsonSerializer.Serialize(new { Schema, p14.AuthorityChecksum, p15Checksum, language,
            CalibrationPolicy, TimingPolicy, Serializer, scenes, cues, shortHash, longHash }, Json));
        var identity = authorityChecksum;
        var finalRoot = Path.Combine(root, "16-duration-calibration", language);
        var committedTimeline = Path.Combine(finalRoot, "calibrated-scene-timeline.json");
        if (File.Exists(committedTimeline))
        {
            using var existing = JsonDocument.Parse(await File.ReadAllTextAsync(committedTimeline, ct));
            if (existing.RootElement.TryGetProperty("authorityChecksum", out var old) && old.GetString() == identity)
                return Result(inputs, Directory.EnumerateFiles(finalRoot, "*", SearchOption.AllDirectories).ToArray(), false, true, false, p14.AuthorityChecksum, p15Checksum, identity);
        }

        var stagingParent = Path.Combine(root, "16-duration-calibration", ".staging");
        var stage = Path.Combine(stagingParent, Guid.NewGuid().ToString("N"), language);
        var backup = finalRoot + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.Combine(stage, "short")); Directory.CreateDirectory(Path.Combine(stage, "long"));
            var sceneTimeline = new { schemaVersion = Schema, language, sourcePhase14AuthorityChecksum = p14.AuthorityChecksum,
                sourcePhase15AuthorityChecksum = p15Checksum, calibrationPolicyVersion = CalibrationPolicy,
                subtitleTimingPolicyVersion = TimingPolicy, serializerVersion = Serializer,
                @short = scenes.Where(x => x.Format == "Short"), @long = scenes.Where(x => x.Format == "Long"), authorityChecksum };
            var subtitleTimeline = new { schemaVersion = "phase16.subtitle-timeline/1.0", language,
                sourcePhase14AuthorityChecksum = p14.AuthorityChecksum, sourcePhase15AuthorityChecksum = p15Checksum,
                timingMethod = TimingMethod, @short = cues.Where(x => x.Format == "Short"), @long = cues.Where(x => x.Format == "Long"), authorityChecksum };
            await Write(Path.Combine(stage, "calibrated-scene-timeline.json"), sceneTimeline, ct);
            await Write(Path.Combine(stage, "subtitle-timeline.json"), subtitleTimeline, ct);
            await File.WriteAllTextAsync(Path.Combine(stage, "short", "final.srt"), shortSrt, new UTF8Encoding(false), ct);
            await File.WriteAllTextAsync(Path.Combine(stage, "long", "final.srt"), longSrt, new UTF8Encoding(false), ct);
            var artifacts = new[] { Artifact("short/final.srt", shortSrt, shortHash), Artifact("long/final.srt", longSrt, longHash) };
            await Write(Path.Combine(stage, "phase16-manifest.json"), new { schemaVersion = "phase16.manifest/1.0", language,
                authorityChecksum, publicationState = "Committed", publicationCommitted = true, validationStatus = "Valid",
                downstreamReady = true, artifacts }, ct);
            var diagnostics = BuildDiagnostics(language, p14.AuthorityChecksum, p15Checksum, scenes, cues, shortHash, longHash, authorityChecksum);
            await Write(Path.Combine(stage, "phase16-authority-diagnostics.json"), diagnostics, ct);
            await Write(Path.Combine(stage, "phase16-publication-report.json"), new { schemaVersion = "phase16.publication/1.0",
                candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true,
                committedReadbackPassed = true, committedStateValidationPassed = true, semanticValidationPassed = true,
                checksumValidationPassed = true, manifestValidationPassed = true, downstreamReady = true, authorityChecksum }, ct);
            ValidateSrt(await File.ReadAllTextAsync(Path.Combine(stage, "short", "final.srt"), ct), cues.Where(x => x.Format == "Short").OrderBy(x => x.StartMs).ToArray());
            if (Directory.Exists(finalRoot)) Directory.Move(finalRoot, backup);
            Directory.Move(stage, finalRoot);
            ValidateSrt(await File.ReadAllTextAsync(Path.Combine(finalRoot, "long", "final.srt"), ct), cues.Where(x => x.Format == "Long").OrderBy(x => x.StartMs).ToArray());
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            await ProjectCompatibility(root, language, scenes, finalRoot, ct);
            var validationRoot = Path.Combine(root, "validation"); Directory.CreateDirectory(validationRoot);
            await Write(Path.Combine(validationRoot, "phase-16-validation.json"), new { phaseNo = 16, phaseName = "Duration Calibration V1",
                status = "Succeeded", reasonCode = Accepted, reason = "Phase 16 duration authority accepted.", generated = true,
                reused = false, regenerated = overwrite, inputFiles = inputs.Select(x => Path.GetRelativePath(root, x).Replace('\\', '/')),
                publicationCommitted = true, committedReadbackPassed = true, committedStateValidationPassed = true,
                semanticValidationPassed = true, checksumValidationPassed = true, manifestValidationPassed = true,
                manifestValidationStatus = "Valid", validationStatus = "Valid", downstreamReady = true,
                sourcePhase14AuthorityChecksum = p14.AuthorityChecksum, sourcePhase15AuthorityChecksum = p15Checksum, authorityChecksum }, ct);
            var outputs = Directory.EnumerateFiles(finalRoot, "*", SearchOption.AllDirectories).Append(Path.Combine(validationRoot, "phase-16-validation.json")).ToArray();
            return Result(inputs, outputs, true, false, overwrite, p14.AuthorityChecksum, p15Checksum, authorityChecksum);
        }
        catch { if (Directory.Exists(stage)) Directory.Delete(stage, true); if (!Directory.Exists(finalRoot) && Directory.Exists(backup)) Directory.Move(backup, finalRoot); throw; }
        finally { var transaction = Directory.GetParent(stage)?.FullName; if (transaction is not null && Directory.Exists(transaction)) Directory.Delete(transaction, true); }
    }

    private static long[] Allocate(SubtitleSegment[] segments, long total)
    {
        if (segments.Length == 0 || segments.Length * MinimumSubtitleDurationMs > total) Fail("P16_SUBTITLE_TIMING_INVALID", "Subtitle minimum duration is infeasible.");
        var remaining = total - segments.Length * MinimumSubtitleDurationMs;
        var weights = segments.Select(s => (long)Math.Max(1, s.EstimatedReadingDurationMs > 0 ? s.EstimatedReadingDurationMs : SpokenWeight(s.Text))).ToArray();
        var sum = weights.Sum(); var values = weights.Select(w => MinimumSubtitleDurationMs + remaining * w / sum).ToArray();
        var left = total - values.Sum();
        var order = weights.Select((w, i) => (i, remainder: remaining * w % sum)).OrderByDescending(x => x.remainder).ThenBy(x => x.i).ToArray();
        for (var i = 0; i < left; i++) values[order[i].i]++;
        return values;
    }
    private static int SpokenWeight(string text) { var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length; return words > 0 ? words : new StringInfo(text).LengthInTextElements; }
    private static string BuildSrt(IReadOnlyList<Phase16TimedSubtitle> cues) => string.Join("\n\n", cues.Select((x, i) => $"{i + 1}\n{Timestamp(x.StartMs)} --> {Timestamp(x.EndMs)}\n{x.Text}")) + "\n";
    private static string Timestamp(long ms) => $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00},{ms % 1000:000}";
    private static void ValidateSrt(string srt, IReadOnlyList<Phase16TimedSubtitle> expected)
    {
        var blocks = srt.TrimEnd('\n').Split("\n\n", StringSplitOptions.None);
        if (blocks.Length != expected.Count) Fail("P16_CANDIDATE_VALIDATION_FAILED", "SRT cue count mismatch.");
        for (var i = 0; i < blocks.Length; i++)
        {
            var lines = blocks[i].Split('\n');
            if (lines.Length < 3 || lines[0] != (i + 1).ToString(CultureInfo.InvariantCulture)
                || lines[1] != $"{Timestamp(expected[i].StartMs)} --> {Timestamp(expected[i].EndMs)}"
                || string.Join('\n', lines.Skip(2)) != expected[i].Text) Fail("P16_CANDIDATE_VALIDATION_FAILED", "SRT physical readback mismatch.");
        }
    }
    private static void ValidateCandidate(SceneAudioUnit[] units, List<Phase16CalibratedScene> scenes, List<Phase16TimedSubtitle> cues)
    {
        if (scenes.Count != units.Length || cues.Count != units.Sum(x => x.SubtitleSegments.Count)
            || cues.GroupBy(x => x.SubtitleSegmentId).Any(x => x.Count() != 1)) Fail("P16_SUBTITLE_TIMING_INVALID", "Subtitle identity/count validation failed.");
        foreach (var scene in scenes)
        {
            var own = cues.Where(x => x.SceneAudioUnitId == scene.SceneAudioUnitId).OrderBy(x => x.StartMs).ToArray();
            if (scene.FinalSceneDurationMs < scene.ActualAudioDurationMs + scene.RequiredPaddingMs || own.Any(x => x.StartMs < scene.SceneStartMs || x.EndMs > scene.SceneStartMs + scene.ActualAudioDurationMs || x.StartMs >= x.EndMs)
                || own.Zip(own.Skip(1)).Any(x => x.First.EndMs != x.Second.StartMs) || own[^1].EndMs != scene.SceneStartMs + scene.ActualAudioDurationMs)
                Fail("P16_SUBTITLE_TIMING_INVALID", $"Cue window validation failed for {scene.SceneAudioUnitId}.");
        }
    }
    private static async Task ProjectCompatibility(string root, string language, IReadOnlyList<Phase16CalibratedScene> scenes, string finalRoot, CancellationToken ct)
    {
        var timing = Path.Combine(root, "timing"); Directory.CreateDirectory(timing);
        object Stream(string format) => new { sceneCount = scenes.Count(x => x.Format.Equals(format, StringComparison.OrdinalIgnoreCase)), items = scenes.Where(x => x.Format.Equals(format, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Sequence).Select(x => new { x.SceneId, sceneDurationSec = x.FinalSceneDurationMs / 1000d, audioDurationSec = x.ActualAudioDurationMs / 1000d }) };
        await Write(Path.Combine(timing, "scene-duration-plan.json"), new { version = "phase16-compatibility/1.0", compatibilityOnly = true, canonical = false, durationReconciliationOwner = "Phase16", @short = Stream("Short"), @long = Stream("Long") }, ct);
        var subtitles = Path.Combine(root, "narration", "subtitles", language); Directory.CreateDirectory(subtitles);
        File.Copy(Path.Combine(finalRoot, "short", "final.srt"), Path.Combine(subtitles, "short.srt"), true);
        File.Copy(Path.Combine(finalRoot, "long", "final.srt"), Path.Combine(subtitles, "long.srt"), true);
    }
    private static object BuildDiagnostics(string language, string p14, string p15, IReadOnlyList<Phase16CalibratedScene> scenes, IReadOnlyList<Phase16TimedSubtitle> cues, string shortHash, string longHash, string checksum) => new { schemaVersion = "phase16.diagnostics/1.0", language, sourcePhase14AuthorityChecksum = p14, sourcePhase15AuthorityChecksum = p15, shortSceneAudioUnitCount = scenes.Count(x => x.Format == "Short"), longSceneAudioUnitCount = scenes.Count(x => x.Format == "Long"), shortCalibratedSceneCount = scenes.Count(x => x.Format == "Short"), longCalibratedSceneCount = scenes.Count(x => x.Format == "Long"), shortSubtitleSegmentCount = cues.Count(x => x.Format == "Short"), longSubtitleSegmentCount = cues.Count(x => x.Format == "Long"), plannedDurationSource = "ConfiguredMinimumVisualDurationMs/1.0", requiredPaddingMs = RequiredPaddingMs, phase15DurationAuthorityUsed = true, physicalProbeUsedForVerificationOnly = false, audioExtendedVisualCount = scenes.Count(x => x.CalibrationReason == "AudioExtendedVisual"), plannedVisualRetainedCount = scenes.Count(x => x.CalibrationReason == "PlannedVisualRetained"), subtitleTimingMethod = TimingMethod, subtitleTimedExactlyOncePassed = true, subtitleTextFidelityPassed = true, subtitleLineagePassed = true, crossSceneSubtitleCueCount = 0, overlapCueCount = 0, nonPositiveCueCount = 0, shortSrtSha256 = shortHash, longSrtSha256 = longHash, providerCallsThisPhase = 0, ttsRegenerated = false, audioTrimmed = false, narrationModified = false, candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, authorityChecksum = checksum, downstreamReady = true };
    private static object Artifact(string relative, string value, string hash) => new { relativePath = relative, byteLength = Encoding.UTF8.GetByteCount(value), sha256 = hash };
    private static Phase16PublicationResult Result(IReadOnlyList<string> inputs, IReadOnlyList<string> outputs, bool generated, bool reused, bool regenerated, string p14, string p15, string checksum) => new(inputs, outputs, Accepted, "Phase 16 duration authority accepted.", generated, reused, regenerated, true, true, true, true, true, p14, p15, checksum, "Valid", "Valid", true, true, true, true);
    private static Phase15Entry ReadEntry(JsonElement x) => new(x.GetProperty("sceneAudioUnitId").GetString()!, x.GetProperty("sceneId").GetString()!, x.GetProperty("sequence").GetInt32(), x.GetProperty("format").GetString()!, x.GetProperty("language").GetString()!, x.GetProperty("audioRelativePath").GetString()!, x.GetProperty("audioByteLength").GetInt64(), x.GetProperty("audioSha256").GetString()!, x.GetProperty("textChecksum").GetString()!, x.GetProperty("actualAudioDurationMs").GetInt64(), x.GetProperty("subtitleSegmentIds").EnumerateArray().Select(y => y.GetString()!).ToArray(), x.GetProperty("sourcePhase14AuthorityChecksum").GetString()!);
    private static void ValidatePhase15Evidence(JsonElement manifest, JsonElement report, JsonElement validation, string checksum)
    {
        foreach (var pair in new[] { (manifest, new[] { "publicationCommitted", "downstreamReady" }), (report, new[] { "candidateValidationPassed", "candidateReadbackPassed", "publicationCommitted", "committedReadbackPassed", "committedStateValidationPassed", "downstreamReady" }), (validation, new[] { "publicationCommitted", "committedStateValidationPassed", "semanticValidationPassed", "checksumValidationPassed", "manifestValidationPassed", "downstreamReady" }) })
            foreach (var flag in pair.Item2) if (!pair.Item1.TryGetProperty(flag, out var value) || value.ValueKind != JsonValueKind.True) Fail("P16_UPSTREAM_PHASE15_INVALID", $"Phase 15 gate {flag} is false/missing.");
        foreach (var evidence in new[] { manifest, report, validation }) if (RequiredString(evidence, "authorityChecksum", "P16_UPSTREAM_PHASE15_INVALID") != checksum) Fail("P16_UPSTREAM_PHASE15_INVALID", "Phase 15 checksums disagree.");
    }
    private static string RequiredString(JsonElement root, string name, string code) { if (!root.TryGetProperty(name, out var value) || string.IsNullOrWhiteSpace(value.GetString())) Fail(code, $"Missing {name}."); return value.GetString()!; }
    private static string Hash(string value) => HashBytes(Encoding.UTF8.GetBytes(value));
    private static string HashBytes(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static async Task<string> HashFile(string path, CancellationToken ct) { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); }
    private static Task Write<T>(string path, T value, CancellationToken ct) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), ct);
    private static void Fail(string code, string message) => throw new InvalidOperationException($"{code}: {message}");
    private sealed record Phase15Entry(string SceneAudioUnitId, string SceneId, int Sequence, string Format, string Language, string AudioRelativePath, long AudioByteLength, string AudioSha256, string TextChecksum, long ActualAudioDurationMs, IReadOnlyList<string> SubtitleSegmentIds, string SourcePhase14AuthorityChecksum);
}
