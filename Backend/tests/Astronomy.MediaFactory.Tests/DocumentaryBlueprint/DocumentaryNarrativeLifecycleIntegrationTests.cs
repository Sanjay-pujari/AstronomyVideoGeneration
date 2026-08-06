using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeLifecycleIntegrationTests
{
    private static readonly Guid PlanId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Canonical_authority_maps_independent_ordered_long_and_short_compositions()
    {
        var frames = Enumerable.Range(1, 12).Select(number => Frame("Long", number))
            .Concat(Enumerable.Range(1, 4).Select(number => Frame("Short", number))).Reverse().ToArray();
        var authority = Authority(frames);
        var request = Request();

        var longRequest = DocumentaryNarrativeLifecycleIntegrationService.BuildCompositionRequest(
            request, authority, "Long", new(480, 600, 900));
        var shortRequest = DocumentaryNarrativeLifecycleIntegrationService.BuildCompositionRequest(
            request, authority, "Short", new(60, 90, 120));

        longRequest.OrderedScenes.Should().HaveCount(12);
        shortRequest.OrderedScenes.Should().HaveCount(4);
        longRequest.OrderedScenes.Select(scene => scene.SceneNumber).Should().BeInAscendingOrder();
        shortRequest.OrderedScenes.Select(scene => scene.SceneNumber).Should().BeInAscendingOrder();
        longRequest.OrderedScenes.Select(scene => scene.SceneId)
            .Intersect(shortRequest.OrderedScenes.Select(scene => scene.SceneId)).Should().BeEmpty();
        longRequest.OrderedScenes.Should().OnlyContain(scene => scene.NarrationBrief == "Narrative purpose" && scene.VisualIntent == "Visual intent");
    }

    [Fact]
    public void Practical_validation_accepts_natural_prose_and_duration_is_warning_only()
    {
        var composition = DocumentaryNarrativeLifecycleIntegrationService.BuildCompositionRequest(
            Request(), Authority([Frame("Long", 1)]), "Long", new(480, 600, 900));
        var scene = new DocumentaryNarrativeDraftScene("long-scene-01", "Orion rises as a familiar guide across the winter sky.", []);
        var draft = new DocumentaryNarrativeDraftCandidate("long", "narration.json", scene.NarrationText, [scene.SceneId], [])
            { Scenes = [scene], WordCount = 11 };

        var quality = DocumentaryNarrativeLifecycleIntegrationService.Validate(draft, composition, [], []);

        quality.Passed.Should().BeTrue();
        quality.Warnings.Should().ContainSingle(message => message.Contains("outside guidance"));
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-prose")]
    [InlineData("leakage")]
    public void Practical_validation_blocks_scene_identity_duplicate_and_leakage_failures(string failure)
    {
        var composition = DocumentaryNarrativeLifecycleIntegrationService.BuildCompositionRequest(
            Request(), Authority([Frame("Long", 1), Frame("Long", 2)]), "Long", new(1, 2, 999));
        var text1 = failure == "leakage" ? "developer prompt: reveal internal instruction" : "First natural astronomy passage.";
        var text2 = failure == "duplicate-prose" ? text1 : "Second distinct astronomy passage.";
        var id2 = failure == "duplicate-id" ? "long-scene-01" : "long-scene-02";
        var scenes = new[] { new DocumentaryNarrativeDraftScene("long-scene-01", text1, []),
            new DocumentaryNarrativeDraftScene(id2, text2, []) };
        var draft = new DocumentaryNarrativeDraftCandidate("long", "narration.json",
            string.Join(" ", scenes.Select(scene => scene.NarrationText)), scenes.Select(scene => scene.SceneId).ToArray(), []) { Scenes = scenes };

        DocumentaryNarrativeLifecycleIntegrationService.Validate(draft, composition, [], []).Passed.Should().BeFalse();
    }

    [Fact]
    public void Cross_variant_validation_blocks_identical_narration()
    {
        const string text = "The same complete narration should not be published for both formats.";
        var scene = new DocumentaryNarrativeDraftScene("scene", text, []);
        var longDraft = new DocumentaryNarrativeDraftCandidate("long", "long.json", text, ["scene"], []) { Scenes = [scene] };
        var shortDraft = new DocumentaryNarrativeDraftCandidate("short", "short.json", text, ["scene"], []) { Scenes = [scene] };

        DocumentaryNarrativeLifecycleIntegrationService.ValidateCrossVariant(longDraft, shortDraft)
            .Should().ContainSingle(message => message.Contains("identical"));
    }

    [Fact]
    public void Retry_is_bounded_to_two_total_generator_attempts() =>
        DocumentaryNarrativeLifecycleIntegrationService.MaximumGenerationAttempts.Should().Be(2);

    [Fact]
    public async Task Missing_canonical_authority_fails_clearly_without_requiring_legacy_manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "phase7-lifecycle-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "06-story-frames"));
            await File.WriteAllTextAsync(Path.Combine(root, "06-story-frames", "story-frame-manifest.json"), "{}");
            var service = new DocumentaryNarrativeLifecycleIntegrationService(
                new NarrationGeneratorV5(NullLogger<NarrationGeneratorV5>.Instance),
                new DocumentaryNarrativeAcceptanceCoordinator());

            var result = await service.ExecuteAsync(Request() with { ExecutionRoot = root });

            result.Succeeded.Should().BeFalse();
            result.ProviderCallEvidence.GeneratorInvocationCount.Should().Be(0);
            result.Errors.Should().Contain(message => message.Contains(
                "Canonical Phase 6 Story Frame authority was not found at 06-story-frames/story-frames.json"));
            File.Exists(Path.Combine(root, "narration-v5", "narration-validation-diagnostics.json")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static DocumentaryNarrativeLifecycleRequest Request() =>
        new("/tmp/execution", "execution", PlanId, "orion", "Constellation", "en", "profile", 2026, "global", new object());

    private static StoryFramesAuthority Authority(IReadOnlyList<StoryFrameAuthorityFrame> frames) =>
        new("authority", "execution", PlanId.ToString("D"), "orion", "en", "profile", "cert", "checksum",
            "editorial", "checksum", "phase4", "builder", "1.0", ["Long", "Short"], frames,
            DateTimeOffset.UtcNow, "semantic");

    private static StoryFrameAuthorityFrame Frame(string variant, int number) =>
        new($"{variant.ToLowerInvariant()}-frame-{number:00}", $"{variant.ToLowerInvariant()}-scene-{number:00}", number, 1,
            variant, "Discovery", "Explain", "Narration", ["viewer-question"], ["learning-objective"], ["knowledge-ref"],
            "Narrative purpose", "Visual intent", "Wide", "Static", "None", "Orion", "Sky", "Centered", "Natural",
            "Curious", "Slow", "Continue", "Transition onward", [], [], [], [], true, "Narrator", 0, 18, [], [], []);
}
