using Astronomy.MediaFactory.Infrastructure.Persistence;
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
}
