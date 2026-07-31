using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase4ArtifactSerializer:IPhase4ArtifactSerializer
{
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public byte[] Serialize<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Options);
    public T Deserialize<T>(byte[] bytes)=>JsonSerializer.Deserialize<T>(bytes,Options) ?? throw new JsonException($"Could not deserialize {typeof(T).Name}.");
    public string SemanticChecksum<T>(T value,Func<T,T> clearChecksum)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(clearChecksum(value),new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant();
}
public sealed class Phase4FileSystem:IPhase4FileSystem
{
    public async Task WriteAsync(string path,byte[] bytes,CancellationToken token){Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes,token);}
    public byte[] Read(string path)=>File.ReadAllBytes(path);
    public string Sha256(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
public sealed class Phase4ExecutionLock:IPhase4ExecutionLock
{
    private static readonly ConcurrentDictionary<string,SemaphoreSlim> Locks=new(StringComparer.OrdinalIgnoreCase);
    public async ValueTask<IAsyncDisposable> AcquireAsync(string root,string id,CancellationToken token){var key=Path.GetFullPath(root)+"|"+id;var gate=Locks.GetOrAdd(key,_=>new(1,1));await gate.WaitAsync(token);try{Directory.CreateDirectory(Path.Combine(root,".locks"));var stream=new FileStream(Path.Combine(root,".locks",Safe(id)+".phase-04.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);return new Releaser(gate,stream);}catch{gate.Release();throw;}static string Safe(string x)=>string.Concat(x.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));}
    private sealed class Releaser(SemaphoreSlim gate,FileStream stream):IAsyncDisposable{public ValueTask DisposeAsync(){stream.Dispose();gate.Release();return ValueTask.CompletedTask;}}
}
public sealed class Phase4RecoveryService(
    IPhase4ArtifactSerializer serializer,
    IPhase4ExecutionLock executionLock) : IPhase4RecoveryService
{
    private static readonly HashSet<string> RecoverableStates = new(["Staging", "BackingUp"], StringComparer.Ordinal);

    public async Task<bool> RecoverAsync(
        string root,
        string executionId,
        TimeSpan staleAge,
        CancellationToken token)
    {
        if (staleAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(staleAge));

        // Recovery owns the same execution lock as publication. This prevents deletion or
        // restoration of a transaction while another process is actively publishing it.
        await using var held = await executionLock.AcquireAsync(root, executionId, token);
        var recovered = false;

        foreach (var path in Directory.Exists(root)
                     ? Directory.EnumerateDirectories(root, ".*.phase-04.tmp")
                     : [])
        {
            token.ThrowIfCancellationRequested();
            if (!TryRead(path, executionId, staleAge, out var tx) || tx!.State != "Staging")
                continue;

            Directory.Delete(path, true);
            recovered = true;
        }

        foreach (var backup in Directory.Exists(root)
                     ? Directory.EnumerateDirectories(root, ".*.phase-04.backup")
                     : [])
        {
            token.ThrowIfCancellationRequested();
            if (!TryRead(backup, executionId, staleAge, out var tx) || tx!.State != "BackingUp")
                continue;

            var savedPhase = Path.Combine(backup, "04-blueprint");
            var livePhase = Path.Combine(root, "04-blueprint");

            // A moved authority plus an absent live authority proves that backup mutation
            // interrupted publication before a replacement authority was committed.
            if (!Directory.Exists(savedPhase) || Directory.Exists(livePhase))
                continue;

            Directory.Move(savedPhase, livePhase);
            Restore(Path.Combine(backup, "phase-manifest.json"), Path.Combine(root, "phase-manifest.json"));
            Restore(Path.Combine(backup, "phase-04-validation.json"),
                Path.Combine(root, "validation", "phase-04-validation.json"));

            Directory.Delete(backup, true);
            recovered = true;
        }

        return recovered;

        bool TryRead(
            string directory,
            string owner,
            TimeSpan age,
            out Phase4TransactionRecord? tx)
        {
            tx = null;
            try
            {
                tx = serializer.Deserialize<Phase4TransactionRecord>(
                    File.ReadAllBytes(Path.Combine(directory, "transaction.json")));
                var checksum = serializer.SemanticChecksum(tx, x => x with { DeterministicChecksum = "" });
                return tx.ExecutionId == owner &&
                       RecoverableStates.Contains(tx.State) &&
                       tx.DeterministicChecksum == checksum &&
                       DateTimeOffset.UtcNow - tx.CreatedUtc >= age;
            }
            catch
            {
                return false;
            }
        }

        static void Restore(string source, string target)
        {
            if (!File.Exists(source))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target, true);
        }
    }
}
public sealed class Phase4ManifestUpdater(IPhase4ArtifactSerializer serializer):IPhase4ManifestUpdater
{
    public byte[] Merge(byte[]? existing,IReadOnlyList<Phase4ArtifactEntry> entries){JsonObject root;if(existing is {Length:>0})root=JsonNode.Parse(existing)?.AsObject()??new();else root=new JsonObject{{"schemaVersion","phase-manifest.v1"}};root["phase4Artifacts"]=JsonSerializer.SerializeToNode(entries.OrderBy(x=>x.RelativePath,StringComparer.Ordinal),new JsonSerializerOptions(JsonSerializerDefaults.Web));return serializer.Serialize(root);}
}
public sealed class Phase4PublishedAuthorityValidator(
    IPhase4ArtifactSerializer serializer) : IPhase4PublishedAuthorityValidator
{
    private static readonly string[] AllowedFiles =
    [
        "documentary-blueprint.json",
        "documentary-blueprint.long.json",
        "documentary-blueprint.short.json",
        "knowledge-selection.json",
        "long-scene-index.json",
        "short-scene-index.json",
        "blueprint-build-report.json"
    ];

    public Task<IReadOnlyList<Phase4PublicationDiagnostic>> ValidateAsync(
        string dir,
        DocumentaryBlueprintAggregate expected,
        CancellationToken token)
    {
        var errors = new List<Phase4PublicationDiagnostic>();

        try
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException(dir);

            var allowed = new HashSet<string>(AllowedFiles, StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                token.ThrowIfCancellationRequested();
                if (!allowed.Contains(Path.GetFileName(file)))
                    Fail("Unknown staged file.", file);
            }

            foreach (var name in AllowedFiles)
            {
                token.ThrowIfCancellationRequested();
                var path = Path.Combine(dir, name);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    Fail($"Required artifact is missing or empty: {name}", path);
            }

            if (errors.Count > 0)
                return Task.FromResult<IReadOnlyList<Phase4PublicationDiagnostic>>(errors);

            var aggregate = Read<DocumentaryBlueprintAggregate>("documentary-blueprint.json");
            var longVariant = Read<DocumentaryBlueprintVariantArtifact>("documentary-blueprint.long.json");
            var shortVariant = Read<DocumentaryBlueprintVariantArtifact>("documentary-blueprint.short.json");
            var knowledge = Read<DocumentaryBlueprintKnowledgeSelectionArtifact>("knowledge-selection.json");
            var longIndex = Read<Phase4SceneIndex>("long-scene-index.json");
            var shortIndex = Read<Phase4SceneIndex>("short-scene-index.json");
            var report = Read<Phase4BlueprintBuildReport>("blueprint-build-report.json");

            if (!DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(aggregate) ||
                aggregate.DeterministicChecksum != expected.DeterministicChecksum ||
                !serializer.Serialize(aggregate).SequenceEqual(serializer.Serialize(expected)))
            {
                Fail("Aggregate authority does not match the expected certified aggregate.");
            }

            ValidateVariant(longVariant, aggregate.LongVariant, "Long");
            ValidateVariant(shortVariant, aggregate.ShortVariant, "Short");
            ValidateKnowledge(knowledge, aggregate);
            ValidateIndex(longIndex, aggregate, aggregate.LongVariant, "Long");
            ValidateIndex(shortIndex, aggregate, aggregate.ShortVariant, "Short");
            ValidateBuildReport(report, aggregate, longVariant, shortVariant);

            CheckChecksum(knowledge, knowledge.DeterministicChecksum,
                x => x with { DeterministicChecksum = "" });
            CheckChecksum(longIndex, longIndex.DeterministicChecksum,
                x => x with { DeterministicChecksum = "" });
            CheckChecksum(shortIndex, shortIndex.DeterministicChecksum,
                x => x with { DeterministicChecksum = "" });
            CheckChecksum(report, report.DeterministicChecksum,
                x => x with { DeterministicChecksum = "" });
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        return Task.FromResult<IReadOnlyList<Phase4PublicationDiagnostic>>(errors);

        T Read<T>(string name) => serializer.Deserialize<T>(File.ReadAllBytes(Path.Combine(dir, name)));

        void ValidateVariant(
            DocumentaryBlueprintVariantArtifact actual,
            DocumentaryBlueprintVariantArtifact embedded,
            string variant)
        {
            if (!DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(actual) ||
                !serializer.Serialize(actual).SequenceEqual(serializer.Serialize(embedded)))
            {
                Fail($"{variant} projection is not exactly the embedded variant.");
            }
        }

        void ValidateKnowledge(
            DocumentaryBlueprintKnowledgeSelectionArtifact artifact,
            DocumentaryBlueprintAggregate aggregateValue)
        {
            static Phase4KnowledgeSelectionEntry Map(
                DocumentarySceneBlueprintTraceability trace,
                DocumentaryKnowledgeSelection selection) =>
                new(
                    selection.KnowledgeSelectionId,
                    selection.Variant,
                    selection.SceneOpportunityId,
                    trace.SceneId,
                    selection.PrimaryViewerQuestionId,
                    selection.KnowledgeReferenceId,
                    selection.SourceArtifact,
                    selection.SourcePointer,
                    selection.SemanticChecksum,
                    selection.PurposeCode,
                    selection.SelectionReasonCode,
                    selection.IsPrimary,
                    selection.EvidenceStatus);

            static Phase4KnowledgeSelectionEntry[] ExpectedEntries(DocumentaryBlueprintVariantArtifact variant) =>
                variant.SceneTraceability
                    .SelectMany(trace => trace.KnowledgeSelections.Select(selection => Map(trace, selection)))
                    .OrderBy(x => x.KnowledgeSelectionId, StringComparer.Ordinal)
                    .ThenBy(x => x.SceneId, StringComparer.Ordinal)
                    .ToArray();

            var expectedLong = ExpectedEntries(aggregateValue.LongVariant);
            var expectedShort = ExpectedEntries(aggregateValue.ShortVariant);

            if (!serializer.Serialize(expectedLong).SequenceEqual(serializer.Serialize(artifact.LongSelections)) ||
                !serializer.Serialize(expectedShort).SequenceEqual(serializer.Serialize(artifact.ShortSelections)))
            {
                Fail("Knowledge selection entries do not exactly match aggregate traceability.");
            }

            var all = expectedLong.Concat(expectedShort).ToArray();
            if (all.Any(x =>
                    string.IsNullOrWhiteSpace(x.SourceArtifact) ||
                    string.IsNullOrWhiteSpace(x.SourcePointer) ||
                    string.IsNullOrWhiteSpace(x.SemanticChecksum) ||
                    x.SourceArtifact.Contains("compatibility", StringComparison.OrdinalIgnoreCase)))
            {
                Fail("Knowledge selection contains an invalid or compatibility-only source authority.");
            }

            var reuse = all
                .GroupBy(x => x.KnowledgeReferenceId, StringComparer.Ordinal)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(g => new Phase4KnowledgeReuse(
                    g.Key,
                    g.Count(x => x.Variant.Equals("Long", StringComparison.OrdinalIgnoreCase)),
                    g.Count(x => x.Variant.Equals("Short", StringComparison.OrdinalIgnoreCase)),
                    g.Count()))
                .ToArray();

            var unique = reuse.Select(x => x.KnowledgeReferenceId).ToArray();
            if (!artifact.UniqueKnowledgeReferences.SequenceEqual(unique, StringComparer.Ordinal) ||
                !serializer.Serialize(artifact.KnowledgeReuseSummary).SequenceEqual(serializer.Serialize(reuse)))
            {
                Fail("Knowledge reuse summary or unique-reference projection is invalid.");
            }

            var traces = aggregateValue.LongVariant.SceneTraceability
                .Concat(aggregateValue.ShortVariant.SceneTraceability)
                .ToArray();
            var expectedEditorialOnly = traces.Count(x =>
                x.QuestionEvidenceStatus == QuestionEvidenceStatus.EditorialOnly);
            var expectedMixed = traces.Count(x =>
                x.QuestionEvidenceStatus == QuestionEvidenceStatus.Mixed);

            if (artifact.EditorialOnlySceneCount != expectedEditorialOnly ||
                artifact.MixedSceneCount != expectedMixed)
            {
                Fail("Knowledge evidence-status totals do not match aggregate traceability.");
            }

            var editorialOnlySceneIds = traces
                .Where(x => x.QuestionEvidenceStatus == QuestionEvidenceStatus.EditorialOnly)
                .Select(x => x.SceneId)
                .ToHashSet(StringComparer.Ordinal);
            if (all.Any(x => editorialOnlySceneIds.Contains(x.SceneId) ||
                             x.EvidenceStatus == QuestionEvidenceStatus.EditorialOnly))
            {
                Fail("Editorial-only scenes must not contain selected certified knowledge.");
            }

            if (artifact.ExecutionId != aggregateValue.ExecutionId ||
                artifact.PlanId != aggregateValue.PlanId ||
                artifact.EventId != aggregateValue.EventId ||
                artifact.Language != aggregateValue.Language ||
                artifact.ProfileId != aggregateValue.ProfileId ||
                artifact.ProfileVersion != aggregateValue.ProfileVersion ||
                artifact.SourceAggregateId != aggregateValue.AggregateId ||
                artifact.SourceAggregateChecksum != aggregateValue.DeterministicChecksum)
            {
                Fail("Knowledge-selection artifact identity drift detected.");
            }
        }

        void ValidateIndex(
            Phase4SceneIndex index,
            DocumentaryBlueprintAggregate aggregateValue,
            DocumentaryBlueprintVariantArtifact variant,
            string name)
        {
            var traces = variant.SceneTraceability.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
            var expectedRows = variant.Blueprint.Scenes
                .OrderBy(x => x.SceneNumber)
                .Select(scene =>
                {
                    var trace = traces[scene.SceneId];
                    return new Phase4SceneIndexEntry(
                        scene.SceneId,
                        scene.SceneNumber,
                        trace.SourceOpportunityId,
                        trace.ProfileSlotId,
                        scene.NarrativeStage.ToString(),
                        scene.SceneRole.ToString(),
                        trace.PrimaryViewerQuestionId,
                        trace.SupportingViewerQuestionIds,
                        trace.LearningObjectiveId,
                        trace.QuestionEvidenceStatus,
                        scene.EstimatedDurationSeconds,
                        trace.MinimumDurationSeconds,
                        trace.MaximumDurationSeconds,
                        scene.Transition.TransitionIntent,
                        trace.KnowledgeSelections.Select(x => x.KnowledgeReferenceId)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(x => x, StringComparer.Ordinal)
                            .ToArray(),
                        trace.EditorialConstraints.Select(x => x.Code)
                            .OrderBy(x => x, StringComparer.Ordinal)
                            .ToArray(),
                        trace.MustNotClaim,
                        Convert.ToHexString(SHA256.HashData(serializer.Serialize(scene))).ToLowerInvariant(),
                        trace.SourceOpportunityChecksum);
                })
                .ToArray();

            if (index.Variant != name ||
                index.SourceAggregateId != aggregateValue.AggregateId ||
                index.SourceAggregateChecksum != aggregateValue.DeterministicChecksum ||
                !serializer.Serialize(index.Scenes).SequenceEqual(serializer.Serialize(expectedRows)))
            {
                Fail($"{name} scene index does not exactly match blueprint and traceability authority.");
            }
        }

        void ValidateBuildReport(
            Phase4BlueprintBuildReport reportValue,
            DocumentaryBlueprintAggregate aggregateValue,
            DocumentaryBlueprintVariantArtifact longValue,
            DocumentaryBlueprintVariantArtifact shortValue)
        {
            var allFlagsPassed =
                reportValue.QuestionReconciliationPassed &&
                reportValue.ObjectiveReconciliationPassed &&
                reportValue.KnowledgeReconciliationPassed &&
                reportValue.SafetyReconciliationPassed &&
                reportValue.DurationReconciliationPassed &&
                reportValue.TransitionReconciliationPassed &&
                reportValue.VariantIndependencePassed &&
                reportValue.ChecksumValidationPassed;

            if (reportValue.ExecutionId != aggregateValue.ExecutionId ||
                reportValue.PlanId != aggregateValue.PlanId ||
                reportValue.EventId != aggregateValue.EventId ||
                reportValue.Language != aggregateValue.Language ||
                reportValue.ProfileId != aggregateValue.ProfileId ||
                reportValue.ProfileVersion != aggregateValue.ProfileVersion ||
                reportValue.IntentId != aggregateValue.SourceIntentId ||
                reportValue.IntentChecksum != aggregateValue.SourceIntentChecksum ||
                reportValue.AggregateId != aggregateValue.AggregateId ||
                reportValue.AggregateChecksum != aggregateValue.DeterministicChecksum ||
                reportValue.LongVariantId != longValue.VariantArtifactId ||
                reportValue.LongVariantChecksum != longValue.DeterministicChecksum ||
                reportValue.ShortVariantId != shortValue.VariantArtifactId ||
                reportValue.ShortVariantChecksum != shortValue.DeterministicChecksum ||
                reportValue.LongSceneCount != longValue.Blueprint.Scenes.Count ||
                reportValue.ShortSceneCount != shortValue.Blueprint.Scenes.Count ||
                reportValue.LongDurationSeconds != longValue.TotalAllocatedDurationSeconds ||
                reportValue.ShortDurationSeconds != shortValue.TotalAllocatedDurationSeconds ||
                !allFlagsPassed ||
                !reportValue.ArtifactInventory.SequenceEqual(AllowedFiles, StringComparer.Ordinal) ||
                reportValue.CompatibilityProjectionGenerated ||
                reportValue.PublicationStatus != "Prepared")
            {
                Fail("Blueprint build report does not exactly reconcile with the certified publication.");
            }
        }

        void CheckChecksum<T>(T value, string checksum, Func<T, T> clear)
        {
            if (serializer.SemanticChecksum(value, clear) != checksum)
                Fail($"{typeof(T).Name} deterministic checksum mismatch.");
        }

        void Fail(string message, string? path = null) =>
            errors.Add(new Phase4PublicationDiagnostic(
                Phase4PublicationReasonCodes.TemporaryValidationFailed,
                message,
                path));
    }
}

public sealed class Phase4CommittedStateValidator(IPhase4PublishedAuthorityValidator physical,IPhase4ArtifactSerializer serializer):IPhase4CommittedStateValidator
{
    public async Task<IReadOnlyList<Phase4PublicationDiagnostic>> ValidateAuthorityAndManifestAsync(string root,DocumentaryBlueprintAggregate expected,CancellationToken token){var errors=(await physical.ValidateAsync(Path.Combine(root,"04-blueprint"),expected,token)).ToList();try{var manifest=JsonNode.Parse(File.ReadAllBytes(Path.Combine(root,"phase-manifest.json")))?.AsObject()??throw new JsonException("Manifest root missing.");var entries=manifest["phase4Artifacts"]?.Deserialize<Phase4ArtifactEntry[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??[];if(entries.Length!=7)throw new JsonException("Manifest must contain seven Phase 4 entries.");foreach(var entry in entries){var path=Path.Combine(root,entry.RelativePath.Replace('/',Path.DirectorySeparatorChar));var bytes=File.ReadAllBytes(path);if(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=entry.PhysicalSha256||bytes.LongLength!=entry.SizeBytes)throw new JsonException($"Manifest mismatch: {entry.RelativePath}");}}catch(Exception ex){errors.Add(new(Phase4PublicationReasonCodes.PostCommitValidationFailed,ex.Message));}return errors;}
    public Task<IReadOnlyList<Phase4PublicationDiagnostic>> ValidateCommitMarkerAsync(string root,DocumentaryBlueprintAggregate expected,CancellationToken token){var errors=new List<Phase4PublicationDiagnostic>();try{var validation=serializer.Deserialize<Phase4ValidationRecord>(File.ReadAllBytes(Path.Combine(root,"validation","phase-04-validation.json")));var actual=serializer.SemanticChecksum(validation,x=>x with{DeterministicChecksum=""});var evidence=new Phase4PublicationValidationEvidence(validation.SemanticValidationPassed,validation.ChecksumValidationPassed,validation.ManifestValidationPassed,validation.ProjectionValidationPassed,validation.KnowledgeSelectionValidationPassed,validation.SceneIndexValidationPassed,validation.BuildReportValidationPassed,validation.FrozenUpstreamValidationPassed);if(!validation.PublicationCommitted||validation.ValidationStatus!="Valid"||validation.AggregateChecksum!=expected.DeterministicChecksum||validation.DeterministicChecksum!=actual||!evidence.IsComplete)throw new JsonException("Success validation is not a valid commit marker.");}catch(Exception ex){errors.Add(new(Phase4PublicationReasonCodes.PostCommitValidationFailed,ex.Message));}return Task.FromResult<IReadOnlyList<Phase4PublicationDiagnostic>>(errors);}
    public async Task<IReadOnlyList<Phase4PublicationDiagnostic>> ValidateAsync(string root,DocumentaryBlueprintAggregate expected,CancellationToken token)=>(await ValidateAuthorityAndManifestAsync(root,expected,token)).Concat(await ValidateCommitMarkerAsync(root,expected,token)).ToArray();
}
