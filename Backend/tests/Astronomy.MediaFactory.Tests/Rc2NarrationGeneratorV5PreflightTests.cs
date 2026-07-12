using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2NarrationGeneratorV5PreflightTests
{
    [Fact]
    public async Task HindiPhase7MissingDocumentaryContract_FailsWithDescriptiveArtifactError()
    {
        var root = CreateRoot();
        WriteEditorialAndStoryboard(root, "hi");
        var generator = new NarrationGeneratorV5(NullLogger<NarrationGeneratorV5>.Instance);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => generator.BuildAndWriteDiagnosticsAsync(Request("hi"), Response(root), CancellationToken.None));
        Assert.Contains("Phase 7 cannot start because creative/documentary-contract.long.json was not found", ex.Message);
        Assert.DoesNotContain("Sequence contains no elements", ex.Message);
        using var validation = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "narration-v5", "generator-preflight-diagnostics.json")));
        Assert.True(validation.RootElement.GetProperty("unsafeSequenceOperationPrevented").GetBoolean());
        Assert.Equal("hi", validation.RootElement.GetProperty("languageResolved").GetString());
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("hi-IN")]
    [InlineData("Hindi")]
    public async Task HindiLanguageAliasesResolveToHindiBeforePreflightFailure(string language)
    {
        var root = CreateRoot();
        WriteEditorialAndStoryboard(root, language);
        var generator = new NarrationGeneratorV5(NullLogger<NarrationGeneratorV5>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.BuildAndWriteDiagnosticsAsync(Request(language), Response(root), CancellationToken.None));
        using var validation = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "narration-v5", "generator-preflight-diagnostics.json")));
        Assert.Equal("hi", validation.RootElement.GetProperty("languageResolved").GetString());
        Assert.True(validation.RootElement.GetProperty("languageProfileFound").GetBoolean());
        Assert.False(validation.RootElement.GetProperty("languageProfileFallbackUsed").GetBoolean());
    }

    [Fact]
    public void SceneIdentityDiagnostics_RootTypeMatchesPhase7ValidatorContract()
    {
        var buildMethod = typeof(NarrationGeneratorV5).GetMethod("BuildSceneIdentityDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(buildMethod);

        var longCards = new[]
        {
            new SceneFactCard(
                "long-scene-001",
                1,
                "long",
                ["Jupiter and Venus appear close together."],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                45,
                "intent-001",
                "frame-001")
        };
        var result = buildMethod!.Invoke(null, [longCards, Array.Empty<SceneFactCard>(), new[] { "long-scene-001" }, Array.Empty<string>(), new[] { "long" }]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("diagnostics", out var diagnostics));
        Assert.Equal(JsonValueKind.Array, diagnostics.ValueKind);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rc2-narration-v5-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "editorial"));
        Directory.CreateDirectory(Path.Combine(root, "creative"));
        return root;
    }

    private static void WriteEditorialAndStoryboard(string root, string language)
    {
        File.WriteAllText(Path.Combine(root, "editorial", "editorial-contract.json"), $$"""
        { "language": "{{language}}", "requiredNarrationFacts": [ { "name": "event", "value": "Moon and Jupiter appear close together." } ], "prohibitedPhrases": [], "preferredPhrases": [] }
        """);
        File.WriteAllText(Path.Combine(root, "creative", "creative-storyboard.json"), $$"""
        { "language": "{{language}}", "storyArc": "Hook → Observation", "scenes": [ { "sceneId": "scene-001", "sceneOrder": 1, "scenePurpose": "Hook", "keyMessage": "Moon and Jupiter appear close." } ] }
        """);
    }

    private static BatchGenerateFromPlansRequest Request(string language)
        => new(2026, "IN", Language: language, UseProductionPipeline: true, StartPhaseNo: 7, EndPhaseNo: 7);

    private static BatchGenerateFromPlansResponse Response(string root)
        => new(true, false, 1, 1, 1, [], [], [], [], UseProductionPipeline: true, Title: "Moon and Jupiter Close Approach", OutputRoot: root);
}
