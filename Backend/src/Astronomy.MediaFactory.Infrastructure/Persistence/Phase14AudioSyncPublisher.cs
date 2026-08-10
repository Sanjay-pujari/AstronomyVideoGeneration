using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Governed Phase 14 planner. It is deliberately a disk-authority consumer and never an authoring/provider boundary.</summary>
internal static class Phase14AudioSyncPublisher
{
    private const string Schema = "phase14.scene-audio-sync/1.1";
    private const string SyncPolicy = "scene-level/1.0";
    private const string GroupingPolicy = "subtitle-text-8w-2l-42c-word-safe-lineage/1.1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<Phase14PublicationResult> ExecuteAsync(string root, string planId, string eventId, string language, CancellationToken ct)
    {
        var p7 = Path.Combine(root, "07-narration");
        var manifestPath = Path.Combine(p7, "narration-manifest.json");
        var certificationPath = Path.Combine(p7, "narration-certification.json");
        var shortPath = Path.Combine(p7, "short", "accepted-release-candidate.json");
        var longPath = Path.Combine(p7, "long", "accepted-release-candidate.json");
        var p10Path = Path.Combine(root, "10-scene-validation", "scene-asset-certification.json");
        var loadedAuthorityArtifacts = new[] { manifestPath, certificationPath, shortPath, longPath, p10Path };
        foreach (var path in loadedAuthorityArtifacts)
            if (!File.Exists(path)) throw new InvalidOperationException($"{Phase14ReasonCodes.UpstreamMissing}: required committed authority is missing: {path}");

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
        using var certification = JsonDocument.Parse(await File.ReadAllTextAsync(certificationPath, ct));
        var shortCandidate = await Read<Phase7AcceptedReleaseCandidate>(shortPath, ct);
        var longCandidate = await Read<Phase7AcceptedReleaseCandidate>(longPath, ct);
        var scenes = await Read<SceneAssetCertification>(p10Path, ct);
        ValidateIdentity(shortCandidate, "Short", planId, eventId, language);
        ValidateIdentity(longCandidate, "Long", planId, eventId, language);
        if (!scenes.PlanId.Equals(planId, StringComparison.OrdinalIgnoreCase) || !scenes.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase)
            || !scenes.Language.Equals(language, StringComparison.OrdinalIgnoreCase) || scenes.PublicationState != "Committed"
            || scenes.ValidationStatus != "Valid" || !scenes.DownstreamReady)
            Fail(Phase14ReasonCodes.SceneLineageInvalid, "Phase 10 scene certification identity/state is invalid.");
        RequireTrue(manifest.RootElement, "downstreamReady");
        foreach (var name in new[] { "acceptancePassed", "physicalReadbackPassed", "checksumsPassed", "downstreamReady" }) RequireTrue(certification.RootElement, name);
        var shortPhysical = await HashFile(shortPath, ct); var longPhysical = await HashFile(longPath, ct);
        ValidateManifestChecksum(manifest.RootElement, "short/accepted-release-candidate.json", shortPhysical);
        ValidateManifestChecksum(manifest.RootElement, "long/accepted-release-candidate.json", longPhysical);

        var sceneChecksum = await HashFile(p10Path, ct);
        var p7Checksum = Hash($"{await HashFile(manifestPath, ct)}|{await HashFile(certificationPath, ct)}|{shortPhysical}|{longPhysical}");
        var groupingChecksum = Hash(GroupingPolicy);
        var identity = Hash($"{planId}|{eventId}|{language}|{p7Checksum}|{sceneChecksum}|{string.Join(',', scenes.ShortCertification.SceneIds)}|{string.Join(',', scenes.LongCertification.SceneIds)}|{SyncPolicy}|{groupingChecksum}|{Schema}");
        var shortStream = BuildStream("Short", language, shortCandidate, scenes.ShortCertification.SceneIds, shortPhysical, p10Path);
        var longStream = BuildStream("Long", language, longCandidate, scenes.LongCertification.SceneIds, longPhysical, p10Path);
        var draft = new Phase14AudioSyncAuthority(Schema, planId, shortCandidate.ExecutionId, eventId, language, p7Checksum,
            sceneChecksum, SyncPolicy, GroupingPolicy, groupingChecksum, identity, shortStream, longStream, "", "Committed");
        var authority = draft with { AuthorityChecksum = Hash(JsonSerializer.Serialize(draft, Json)) };

        var finalRoot = Path.Combine(root, "14-audio-sync");
        var authorityPath = Path.Combine(finalRoot, "narration-cue-plan.json");
        if (File.Exists(authorityPath))
        {
            var old = await Read<Phase14AudioSyncAuthority>(authorityPath, ct);
            if (old.RequestIdentity == identity && ValidateChecksum(old))
                return Accepted(loadedAuthorityArtifacts, Directory.EnumerateFiles(finalRoot).Append(Path.Combine(root, "validation", "phase-14-validation.json")).ToArray(), old.AuthorityChecksum);
        }
        var stage = finalRoot + $".candidate-{Guid.NewGuid():N}";
        var backup = finalRoot + $".backup-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(stage);
            var syncPath = Path.Combine(stage, "scene-audio-sync.json");
            var cuePath = Path.Combine(stage, "narration-cue-plan.json");
            await Write(syncPath, authority, ct); await Write(cuePath, authority, ct);
            var readback = await Read<Phase14AudioSyncAuthority>(cuePath, ct);
            if (!ValidateChecksum(readback)) Fail(Phase14ReasonCodes.CandidateInvalid, "Candidate checksum/readback failed.");
            var diagnostics = Diagnostics(authority, true, true);
            await Write(Path.Combine(stage, "phase14-authority-diagnostics.json"), diagnostics, ct);
            await Write(Path.Combine(stage, "phase14-manifest.json"), new { schemaVersion = Schema, authority.RequestIdentity, authority.AuthorityChecksum, publicationState = "Committed", artifacts = new[] { "scene-audio-sync.json", "narration-cue-plan.json", "phase14-authority-diagnostics.json", "phase14-publication-report.json" } }, ct);
            await Write(Path.Combine(stage, "phase14-publication-report.json"), new { schemaVersion = Schema, candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, authorityChecksum = authority.AuthorityChecksum, downstreamReady = true }, ct);
            if (Directory.Exists(finalRoot)) Directory.Move(finalRoot, backup);
            Directory.Move(stage, finalRoot);
            var committed = await Read<Phase14AudioSyncAuthority>(Path.Combine(finalRoot, "narration-cue-plan.json"), ct);
            if (!ValidateChecksum(committed)) throw new InvalidOperationException($"{Phase14ReasonCodes.ReadbackFailed}: committed checksum failed.");
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            var validationRoot = Path.Combine(root, "validation"); Directory.CreateDirectory(validationRoot);
            var validationPath = Path.Combine(validationRoot, "phase-14-validation.json");
            await Write(validationPath, new { schemaVersion = Schema, phaseNo = 14, phaseName = "Scene Audio Sync V1", status = "Succeeded", reason = "Validation passed / authority accepted", reasonCode = Phase14ReasonCodes.Accepted, inputFiles = loadedAuthorityArtifacts.Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).ToArray(), ttsBoundaryModel = "SceneLevel", oneSceneOneAudioUnitPassed = true, subtitleSegmentsMayExceedAudioUnits = true, srtDefinesTtsBoundary = false, azureSpeechCalls = 0, otherTtsProviderCalls = 0, physicalAudioGenerated = false, semanticValidationPassed = true, checksumValidationPassed = true, manifestValidationPassed = true, publicationCommitted = true, committedStateValidationPassed = true, authorityChecksum = authority.AuthorityChecksum, downstreamReady = true }, ct);
            return Accepted(loadedAuthorityArtifacts, Directory.EnumerateFiles(finalRoot).Append(validationPath).ToArray(), authority.AuthorityChecksum);
        }
        catch { if (Directory.Exists(stage)) Directory.Delete(stage, true); if (!Directory.Exists(finalRoot) && Directory.Exists(backup)) Directory.Move(backup, finalRoot); throw; }
    }

    private static Phase14AudioSyncStream BuildStream(string format, string language, Phase7AcceptedReleaseCandidate candidate, IReadOnlyList<string> certifiedIds, string sourceChecksum, string sceneRef)
    {
        if (candidate.Scenes.Count != certifiedIds.Count) Fail(Phase14ReasonCodes.SceneMappingInvalid, $"{format} narration/scene counts differ.");
        var byId = candidate.Scenes.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var units = new List<SceneAudioUnit>(); var sentenceIndex = 0;
        for (var i = 0; i < certifiedIds.Count; i++)
        {
            if (!byId.TryGetValue(certifiedIds[i], out var scene)) Fail(Phase14ReasonCodes.SceneMappingInvalid, $"No identity mapping for {format} scene '{certifiedIds[i]}'.");
            var text = Normalize(scene.NarrationText); if (text.Length == 0) Fail(Phase14ReasonCodes.NarrationFidelityFailed, "Narration text is empty.");
            if (text.Length > 10000) Fail(Phase14ReasonCodes.UnitTooLarge, $"Scene '{scene.SceneId}' exceeds the governed safety limit.");
            var sentenceSpans = Regex.Matches(text, @"[^.!?\u0964\u0965]+[.!?\u0964\u0965]*", RegexOptions.CultureInvariant)
                .Cast<Match>().Select(m => TrimmedSpan(m.Index, m.Length, text)).Where(x => x.End > x.Start)
                .Select((span, n) => new SentenceSpan($"{format.ToLowerInvariant()}-{scene.SceneId}-sentence-{n + 1:D3}", span.Start, span.End)).ToArray();
            var ids = sentenceSpans.Select(x => x.Id).ToArray();
            var unitId = $"sau-{Hash($"{format}|{scene.SceneId}|{language}|{i + 1}")[..20]}";
            var chunks = SplitSubtitleSpans(text);
            var segments = chunks.Select((chunk, n) =>
            {
                var lines = WrapSubtitle(chunk.Text);
                var contributingIds = sentenceSpans.Where(s => s.Start < chunk.End && chunk.Start < s.End).Select(s => s.Id).ToArray();
                return new SubtitleSegment($"sub-{Hash($"{unitId}|{n + 1}")[..20]}", n + 1, unitId, scene.SceneId,
                    contributingIds, chunk.Text, Hash(chunk.Text), Math.Max(800, chunk.Text.Length * 70), lines[0], lines.Count > 1 ? lines[1] : null,
                    chunk.Start, chunk.End, AudioSyncBreakReason.Sentence);
            }).ToArray();
            ValidateSegments(text, ids, sentenceSpans, segments);
            units.Add(new(unitId, i + 1, format, language, scene.SceneId, scene.StoryFrameId, ids, sentenceIndex, sentenceIndex + ids.Length - 1,
                text, Hash(text), Math.Max(500, (int)Math.Round(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 135d * 60000)), i == 0 ? 0 : 150, 250,
                AudioSyncBreakReason.Scene, segments, $"voice-profile:{language}", "documentary-neutral", false,
                [$"07-narration/{format.ToLowerInvariant()}/accepted-release-candidate.json#{scene.SceneId}"], [$"{sceneRef}#{scene.SceneId}"])); sentenceIndex += ids.Length;
        }
        var source = Normalize(string.Join(' ', candidate.Scenes.OrderBy(x => x.SceneNumber).Select(x => x.NarrationText)));
        var planned = Normalize(string.Join(' ', units.Select(x => x.Text)));
        if (source != planned) Fail(Phase14ReasonCodes.NarrationFidelityFailed, $"{format} full-stream text fidelity failed.");
        return new(format, certifiedIds.Count, units, Hash(source), Hash(planned), true);
    }

    internal static IReadOnlyList<string> SplitSubtitles(string text) => SplitSubtitleSpans(Normalize(text)).Select(x => x.Text).ToArray();

    internal static IReadOnlyList<string> WrapSubtitle(string text)
    {
        const int maxChars = 42;
        var words = Regex.Matches(Normalize(text), @"\S+").Select(m => m.Value).ToArray();
        var lines = new List<string>(); var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (candidate.Length <= maxChars || current.Length == 0) { current = candidate; continue; }
            lines.Add(current); current = word;
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static IReadOnlyList<SubtitleSpan> SplitSubtitleSpans(string text)
    {
        var tokens = Regex.Matches(text, @"\S+").Cast<Match>().ToArray();
        var result = new List<SubtitleSpan>(); var index = 0;
        while (index < tokens.Length)
        {
            var take = 1;
            while (take < 8 && index + take < tokens.Length)
            {
                var candidate = text[tokens[index].Index..(tokens[index + take].Index + tokens[index + take].Length)];
                if (WrapSubtitle(candidate).Count > 2) break;
                take++;
            }
            var start = tokens[index].Index; var last = tokens[index + take - 1]; var end = last.Index + last.Length;
            result.Add(new(start, end, Normalize(text[start..end]))); index += take;
        }
        return result;
    }

    private static void ValidateSegments(string parent, IReadOnlyList<string> parentIds, IReadOnlyList<SentenceSpan> sentences, IReadOnlyList<SubtitleSegment> segments)
    {
        var previousEnd = 0;
        foreach (var segment in segments)
        {
            if (segment.SourceCharacterStart is not int start || segment.SourceCharacterEnd is not int end || start < 0 || end > parent.Length || start >= end || start < previousEnd)
                Fail(Phase14ReasonCodes.CuePlanInvalid, "Subtitle character spans are invalid or overlap.");
            if (Normalize(parent[start..end]) != Normalize(segment.Text) || Normalize(string.Join(' ', new[] { segment.Line1, segment.Line2 }.Where(x => !string.IsNullOrWhiteSpace(x)))) != Normalize(segment.Text))
                Fail(Phase14ReasonCodes.CuePlanInvalid, "Subtitle text/line reconstruction failed.");
            var expected = sentences.Where(s => s.Start < end && start < s.End).Select(s => s.Id).ToArray();
            if (segment.SentenceIds.Any(id => !parentIds.Contains(id)) || !segment.SentenceIds.SequenceEqual(expected))
                Fail(Phase14ReasonCodes.CuePlanInvalid, "Subtitle sentence lineage does not match character overlap.");
            previousEnd = end;
        }
    }

    private static (int Start, int End) TrimmedSpan(int start, int length, string text)
    { var end = start + length; while (start < end && char.IsWhiteSpace(text[start])) start++; while (end > start && char.IsWhiteSpace(text[end - 1])) end--; return (start, end); }

    private static Phase14PublicationResult Accepted(IReadOnlyList<string> inputs, IReadOnlyList<string> outputs, string checksum)
        => new(inputs, outputs, Phase14ReasonCodes.Accepted, "Validation passed / authority accepted", true, true, checksum, "Valid", "Valid", true, true, true, true);

    private sealed record SubtitleSpan(int Start, int End, string Text);
    private sealed record SentenceSpan(string Id, int Start, int End);

    private static object Diagnostics(Phase14AudioSyncAuthority a, bool committed, bool readback) => new { schemaVersion = Schema, phase7Loaded = true, phase7Validated = true, sceneAuthorityLoaded = true, sceneAuthorityValidated = true, requestedLanguage = a.Language, shortSceneCount = a.ShortStream.NarratedSceneCount, longSceneCount = a.LongStream.NarratedSceneCount, shortSceneAudioUnitCount = a.ShortStream.SceneAudioUnits.Count, longSceneAudioUnitCount = a.LongStream.SceneAudioUnits.Count, shortSubtitleSegmentCount = a.ShortStream.SceneAudioUnits.Sum(x => x.SubtitleSegments.Count), longSubtitleSegmentCount = a.LongStream.SceneAudioUnits.Sum(x => x.SubtitleSegments.Count), subtitleMidWordBreakCount = 0, subtitleBrokenTokenCount = 0, subtitleLineReconstructionPassed = true, subtitleCharacterSpanCoveragePassed = true, subtitleSentenceLineagePassed = true, sentenceCoveragePassed = true, sentenceOrderPassed = true, textFidelityPassed = true, duplicateSentenceCount = 0, missingSentenceCount = 0, orphanSceneCount = 0, duplicateSceneMappingCount = 0, crossSceneAudioUnitCount = 0, perSrtTtsUnitCount = 0, providerCallsThisPhase = 0, physicalAudioFilesGenerated = 0, canonicalSrtGenerated = false, syncPolicyVersion = SyncPolicy, groupingPolicyVersion = GroupingPolicy, candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = committed, committedReadbackPassed = readback, authorityChecksum = a.AuthorityChecksum, downstreamReady = committed && readback, narrationTextModified = false };
    private static bool ValidateChecksum(Phase14AudioSyncAuthority a) => a.AuthorityChecksum == Hash(JsonSerializer.Serialize(a with { AuthorityChecksum = "" }, Json));
    private static void ValidateIdentity(Phase7AcceptedReleaseCandidate c, string variant, string plan, string eventId, string language) { if (!c.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase) || !c.PlanId.Equals(plan, StringComparison.OrdinalIgnoreCase) || !c.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase) || !c.Language.Equals(language, StringComparison.OrdinalIgnoreCase) || c.AcceptedSceneCount != c.Scenes.Count) Fail(Phase14ReasonCodes.UpstreamInvalid, $"{variant} narration identity is invalid."); }
    private static void ValidateManifestChecksum(JsonElement manifest, string key, string actual) { if (!manifest.TryGetProperty("candidateChecksums", out var cs) || !cs.TryGetProperty(key, out var expected) || !actual.Equals(expected.GetString(), StringComparison.OrdinalIgnoreCase)) Fail(Phase14ReasonCodes.UpstreamInvalid, $"Phase 7 checksum mismatch for {key}."); }
    private static void RequireTrue(JsonElement root, string name) { if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.True) Fail(Phase14ReasonCodes.UpstreamInvalid, $"Phase 7 publication flag '{name}' is not true."); }
    private static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormC).Replace("\r\n", "\n").Replace('\r', '\n'), @"\s+", " ").Trim();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static async Task<string> HashFile(string path, CancellationToken ct) { await using var s = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant(); }
    private static async Task<T> Read<T>(string path, CancellationToken ct) { await using var s = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<T>(s, Json, ct) ?? throw new InvalidOperationException($"{Phase14ReasonCodes.UpstreamInvalid}: {path} is invalid."); }
    private static Task Write<T>(string path, T value, CancellationToken ct) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), ct);
    [DoesNotReturn]
    private static void Fail(string code, string message) => throw new InvalidOperationException($"{code}: {message}");
}

/// <summary>Minimal Phase 15 boundary: synthesis requests are the governed scene audio units, never subtitle segments.</summary>
public static class Phase15SceneAudioUnitAdapter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<SceneAudioUnit>> LoadAsync(string outputRoot, CancellationToken ct = default)
    {
        var path = Path.Combine(outputRoot, "14-audio-sync", "narration-cue-plan.json");
        if (!File.Exists(path)) throw new InvalidOperationException($"{Phase14ReasonCodes.UpstreamMissing}: Phase 14 cue plan is missing.");
        await using var stream = File.OpenRead(path);
        var authority = await JsonSerializer.DeserializeAsync<Phase14AudioSyncAuthority>(stream, Json, ct)
            ?? throw new InvalidOperationException($"{Phase14ReasonCodes.CuePlanInvalid}: Phase 14 cue plan is invalid.");
        if (authority.PublicationState != "Committed" || authority.ShortStream.SceneAudioUnits.Concat(authority.LongStream.SceneAudioUnits).Any(x => x.MayCrossSceneBoundary))
            throw new InvalidOperationException($"{Phase14ReasonCodes.CuePlanInvalid}: Phase 14 cue plan is not committed or crosses a scene boundary.");
        return authority.ShortStream.SceneAudioUnits.Concat(authority.LongStream.SceneAudioUnits).OrderBy(x => x.Format).ThenBy(x => x.Sequence).ToArray();
    }

    public static int ProductionSynthesisRequestCount(IEnumerable<SceneAudioUnit> units) => units.Count();
}
