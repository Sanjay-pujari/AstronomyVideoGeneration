using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase4CommittedAuthorityEvaluatorTests
{
    [Fact]
    public async Task Inventory_tolerates_mixed_manifest_entries_and_normalizes_valid_paths()
    {
        var root = CreateRoot();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "phase-manifest.json"), """
                {
                  "phase4Artifacts": [
                    { "RelativePath": "\\04-blueprint\\documentary-blueprint.json" },
                    null,
                    "not-an-object",
                    { "relativePath": 42 },
                    { "anotherProperty": "ignored" },
                    { "relativePath": "/04-blueprint/documentary-blueprint.json" },
                    { "RELATIVEPATH": "validation/phase-04-validation.json" },
                    { "relativePath": " " }
                  ]
                }
                """);
            Directory.CreateDirectory(Path.Combine(root, "validation"));
            await File.WriteAllTextAsync(Path.Combine(root, "validation", "phase-04-validation.json"), "{}");

            var evaluation = await CreateEvaluator().EvaluateAsync(root, "", "", "", "");

            Assert.Equal(
                [
                    "04-blueprint/documentary-blueprint.json",
                    "phase-manifest.json",
                    "validation/phase-04-validation.json"
                ],
                evaluation.ArtifactPaths);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Inventory_does_not_fail_evaluation_when_manifest_json_is_malformed()
    {
        var root = CreateRoot();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "phase-manifest.json"), "{ invalid json");

            var evaluation = await CreateEvaluator().EvaluateAsync(root, "", "", "", "");

            Assert.Equal("P4REUSE_AUTHORITY_MISSING", evaluation.ReasonCode);
            Assert.Equal(["phase-manifest.json"], evaluation.ArtifactPaths);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static Phase4CommittedAuthorityEvaluator CreateEvaluator() => new(null!, null!);

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase4-authority-evaluator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
