using Astronomy.MediaFactory.Infrastructure.Persistence;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase1AuthorityTests
{
    [Fact]
    public void Canonical_checksum_ignores_generated_time_and_dictionary_order()
    {
        var first = new { generatedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"), values = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" } };
        var second = new { generatedUtc = DateTimeOffset.Parse("2027-01-01T00:00:00Z"), values = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" } };

        Assert.Equal(Phase1CanonicalJson.Checksum(first, "generatedUtc"), Phase1CanonicalJson.Checksum(second, "generatedUtc"));
    }

    [Fact]
    public void Persisted_contract_contains_no_secret_bearing_properties()
    {
        var properties = new[] { typeof(Phase1ExecutionContext), typeof(Phase1SelectedPlan), typeof(Phase1ProductionRequest), typeof(Phase1PipelineState) }
            .SelectMany(type => type.GetProperties()).Select(property => property.Name).ToArray();

        Assert.DoesNotContain(properties, name => new[] { "apiKey", "connectionString", "accessToken", "refreshToken", "authorization", "sasToken", "credential", "secret" }.Any(secret => name.Contains(secret, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Reader_rejects_missing_complete_set_with_structured_codes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase1-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var result = await new Phase1AuthorityValidator().ValidateAsync(root, Path.Combine(root, "01-plan"), false, CancellationToken.None);
            Assert.False(result.IsValid);
            Assert.All(result.Errors, error => Assert.StartsWith("P1_", error.Code));
            Assert.Contains(result.Errors, error => error.Code == "P1_ARTIFACT_MISSING");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Staged_manifest_contains_both_compatibility_artifacts_and_passes_validation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase1-manifest-{Guid.NewGuid():N}");
        var canonical = Path.Combine(root, ".01-plan.staging-" + new string('a', 32));
        var compatibility = Path.Combine(root, ".plan-input.staging-" + new string('a', 32));
        Directory.CreateDirectory(canonical); Directory.CreateDirectory(compatibility);
        try
        {
            var authority = Authority(Guid.NewGuid());
            foreach (var name in new[] { "execution-context.json", "selected-plan.json", "production-request.json", "pipeline-state.json" })
                await File.WriteAllTextAsync(Path.Combine(canonical, name), "{}");
            foreach (var name in new[] { "content-plan-production-request.json", "production-event-intelligence.json" })
                await File.WriteAllTextAsync(Path.Combine(compatibility, name), "{}");
            var manifest = Path.Combine(root, ".phase-manifest.staging-test.json");
            var context = new Phase1ManifestStagingContext(root, canonical, compatibility, manifest, "transaction", authority, new(new Dictionary<string, string>(), new Dictionary<string, string>()));
            var writer = typeof(ProductionPipelineExecutionService).GetMethod("WritePhase1StagedManifestAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            await (Task)writer.Invoke(null, new object[] { context, CancellationToken.None })!;

            File.Move(manifest, Path.Combine(root, "phase-manifest.json"));
            Directory.Move(canonical, Path.Combine(root, "01-plan"));
            Directory.Move(compatibility, Path.Combine(root, "plan-input"));
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "phase-manifest.json")));
            var entries = document.RootElement.GetProperty("phase1Artifacts").EnumerateArray().ToArray();
            Assert.Equal(6, entries.Length);
            Assert.Contains(entries, entry => entry.GetProperty("path").GetString()!.EndsWith("plan-input/production-event-intelligence.json", StringComparison.Ordinal));
            Assert.True((await new Phase1ManifestValidator(new Phase1FileSystem()).ValidateAsync(root, authority, context.ExpectedCompatibilityPublication, CancellationToken.None)).IsValid);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Restored_compatibility_publication_is_rebuilt_entirely_from_physical_files()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase1-lineage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "content-plan-production-request.json"), "{\"generation\":\"old\"}");
            await File.WriteAllTextAsync(Path.Combine(root, "production-event-intelligence.json"), "{\"generation\":\"old\"}");
            var restored = await new Phase1CompatibilityPublisher(new Phase1FileSystem()).ReadDirectoryAsync(root, CancellationToken.None);
            Assert.All(restored.Payloads.Values, payload => Assert.Contains("old", payload));
            Assert.All(restored.Checksums, item => Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(restored.Payloads[item.Key]))).ToLowerInvariant(), item.Value));
        }
        finally { Directory.Delete(root, true); }
    }

    private static Phase1AuthoritySet Authority(Guid id)
    {
        var selected = new Phase1SelectedPlan(Phase1AuthorityContract.SelectedPlanContract, id, "source", "title", "short", "event", "event:id", [], [], null, null, null, null, "region", "en", "category", [], [], "source", "selected");
        var request = new Phase1ProductionRequest(Phase1AuthorityContract.ProductionRequestContract, id, id, "en", "en", [], [], 1, 2, 1, 2, false, false, false, "Generate", "request");
        var state = new Phase1PipelineState(Phase1AuthorityContract.PipelineStateContract, id, id, DateTimeOffset.UnixEpoch, 1, 2, 1, 2, "Initialized", [1, 2], 2, false, "01-plan/execution-context.json", "selected", "request", new Dictionary<int, string>());
        var execution = new Phase1ExecutionContext(Phase1AuthorityContract.ContractVersion, Phase1AuthorityContract.AuthorityType, Phase1AuthorityContract.AuthorityVersion, Phase1AuthorityContract.CgIdentifier, Phase1AuthorityContract.OrchestrationVersion, Phase1AuthorityContract.ProjectorIdentity, Phase1AuthorityContract.CanonicalizationIdentity, id, id, id, id, "event:id", "event", "en", "en", [], [], 1, 2, 1, 2, "Generate", false, false, false, id.ToString("D"), "selected", "request", "compatibility", "identity", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch, "authority");
        return new(execution, selected, request, state);
    }
}
