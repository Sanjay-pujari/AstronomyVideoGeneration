using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Text.Json;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeLifecycleIntegrationTests
{
    private static readonly Guid PlanId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Theory]
    [InlineData("{\"intelligence\":{\"eventType\":\"Constellation\",\"skyDirectionHint\":\"south\"}}", true)]
    [InlineData("{\"eventType\":\"Constellation\",\"skyDirectionHint\":\"north\"}", false)]
    public void Canonical_phase2_reader_unwraps_authority_and_preserves_raw_compatibility(string json, bool expectedUnwrapped)
    {
        var root = Path.Combine(Path.GetTempPath(), "phase7-intelligence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "production-event-intelligence.json");
        File.WriteAllText(path, json);
        try
        {
            var method = typeof(NarrationGeneratorV5).GetMethod("ReadCanonicalProductionEventIntelligence",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            object?[] arguments = [path, false];

            var intelligence = (JsonElement?)method.Invoke(null, arguments);

            ((bool)arguments[1]!).Should().Be(expectedUnwrapped);
            intelligence.Should().NotBeNull();
            intelligence!.Value.GetProperty("eventType").GetString().Should().Be("Constellation");
            intelligence.Value.TryGetProperty("intelligence", out _).Should().BeFalse();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Phase7_source_prefers_canonical_inputs_and_keeps_legacy_observation_optional()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Orchestration", "RC2", "NarrationGeneratorV5.cs"));

        source.Should().Contain("PreferExisting(outputRoot, \"02-intelligence/production-event-intelligence.json\", \"plan-input/production-event-intelligence.json\")");
        source.Should().Contain("ResolveObservationContext(productionPipelineRequest, productionEventIntelligence, contract, outputRoot)");
        source.Should().NotContain("requires observation metadata from Phase 5");
        source.Should().Contain("04-blueprint/documentary-blueprint.long.json");
        source.Should().Contain("04-blueprint/documentary-blueprint.short.json");
        source.Should().Contain("05-editorial/editorial-contract.json");
    }

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
    public void BlueprintMismatch_reordered_scene_blocks_publication_validation()
    {
        var composition = DocumentaryNarrativeLifecycleIntegrationService.BuildCompositionRequest(
            Request(), Authority([Frame("Long", 1), Frame("Long", 2)]), "Long", new(1, 2, 999));
        var scenes = new[]
        {
            new DocumentaryNarrativeDraftScene("long-scene-02", "Orion's second governed purpose explains the winter sky.", []),
            new DocumentaryNarrativeDraftScene("long-scene-01", "Orion's first governed purpose introduces the winter sky.", [])
        };
        var draft = new DocumentaryNarrativeDraftCandidate("Long", "narration.json",
            string.Join(' ', scenes.Select(scene => scene.NarrationText)), scenes.Select(scene => scene.SceneId).ToArray(), [])
            { Scenes = scenes };

        var result = DocumentaryNarrativeLifecycleIntegrationService.Validate(draft, composition, [], []);

        result.Passed.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("scene order") && error.Contains("DocumentaryBlueprint"));
    }

    [Theory]
    [InlineData("ViewerQuestionId VQ-ORION-01 should be repeated to the audience.")]
    [InlineData("LearningObjectiveId LO-ORION-02 explains this passage.")]
    [InlineData("ClaimId: CLM-42 supplies the answer.")]
    [InlineData("Final narration remains owned by Phase 7.")]
    [InlineData("Advance01 introduces the next idea.")]
    public void Practical_validation_blocks_internal_metadata_patterns(string text)
    {
        var composition = DocumentaryNarrativeLifecycleIntegrationService.BuildCompositionRequest(
            Request(), Authority([Frame("Long", 1)]), "Long", new(1, 2, 999));
        var scene = new DocumentaryNarrativeDraftScene("long-scene-01", text, []);
        var draft = new DocumentaryNarrativeDraftCandidate("long", "narration.json", text, [scene.SceneId], []) { Scenes = [scene] };
        DocumentaryNarrativeLifecycleIntegrationService.Validate(draft, composition, [], []).Passed.Should().BeFalse();
    }

    [Fact]
    public void Factual_substance_accepts_natural_paraphrase_and_rejects_generic_hook()
    {
        var governed = new DocumentaryNarrativeSceneInput(1, "orion-belt", "recognition", "Recognizing Orion",
            "How can Orion be found?", "Recognize the belt", "Explain Orion's Belt as three aligned stars",
            [new("belt-claim", "Orion's Belt is formed by three aligned stars", [], [], 1m, [])], [], [], [], [], "", 20, "", "");
        DocumentaryNarrativeLifecycleIntegrationService.HasFactualSubstance(
            "Three bright stars in a straight line form Orion's Belt, an easy signpost in the winter sky.", governed).Should().BeTrue();
        DocumentaryNarrativeLifecycleIntegrationService.HasFactualSubstance(
            "Look up and let wonder guide you as the story continues into another remarkable moment.", governed).Should().BeFalse();
    }

    [Theory]
    [InlineData("look")]
    [InlineData("Look")]
    [InlineData("looking")]
    [InlineData("lore")]
    [InlineData("Lore")]
    [InlineData("long")]
    [InlineData("Long")]
    [InlineData("local")]
    [InlineData("location")]
    [InlineData("logical")]
    [InlineData("lower")]
    [InlineData("Krishna")]
    public void Lifecycle_ordinary_words_are_not_internal_ids(string text) =>
        DocumentaryNarrativeLifecycleIntegrationService.ContainsInternalIdentifier(text).Should().BeFalse();

    [Theory]
    [InlineData("LO-03")]
    [InlineData("LO_03")]
    [InlineData("LO12")]
    [InlineData("VQ-03")]
    [InlineData("CLM-123")]
    [InlineData("CLAIM-ABC123")]
    [InlineData("KR-42")]
    [InlineData("KNOWLEDGE-ABC123")]
    [InlineData("Advance03")]
    public void Lifecycle_real_internal_ids_are_rejected(string text) =>
        DocumentaryNarrativeLifecycleIntegrationService.ContainsInternalIdentifier(text).Should().BeTrue();

    [Theory]
    [InlineData("Look closely, and you'll see three stars aligned in a nearly perfect row: Alnitak, Alnilam, and Mintaka form Orion's Belt, framed by Betelgeuse and Rigel.", "Alnitak Alnilam Mintaka Belt Betelgeuse Rigel Orion")]
    [InlineData("In Greek mythology Orion is a hunter, while Arabic lore, India and China preserve stories among stars and nebulae.", "Greek mythology Arabic lore India China stars nebulae Orion")]
    [InlineData("Greek and Roman Orion traditions meet India's Mriga, Chinese traditions, and Arabic names.", "Greek Roman India Mriga Chinese Arabic Orion")]
    [InlineData("The Belt stars Alnitak, Alnilam and Mintaka sit between Betelgeuse and Rigel near the celestial equator.", "Belt Alnitak Alnilam Mintaka Betelgeuse Rigel celestial equator")]
    [InlineData("To find Orion, look for its distinctive Belt: Alnitak, Alnilam and Mintaka, between Betelgeuse and Rigel.", "Belt Alnitak Alnilam Mintaka Betelgeuse Rigel Orion")]
    public void Real_Orion_scenes_have_factual_substance(string narration, string concepts)
    {
        var governed = new DocumentaryNarrativeSceneInput(1, "orion", "recognition", "Orion", "How?", "Recognize Orion", concepts,
            [new("claim", concepts, [], [], 1m, [])], [], [], [], [], "", 20, "", "");
        DocumentaryNarrativeLifecycleIntegrationService.HasFactualSubstance(narration, governed).Should().BeTrue();
    }

    [Fact]
    public void Generator_editorial_decision_is_advisory_when_objective_gates_pass()
    {
        var root = Path.Combine(Path.GetTempPath(), "phase7-editorial-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "narration-v5"));
            File.WriteAllText(Path.Combine(root, "narration-v5", "generator-validation-diagnostics.json"),
                """{"longNarrationArtifactValid":true,"shortNarrationArtifactValid":true,"languageValidationPassed":true,"sceneMappingValid":true,"finalEditorialDecision":"Do Not Publish"}""");
            var result = DocumentaryNarrativeLifecycleIntegrationService.AssessGeneratorResult(root, true, true);
            result.BlockingErrors.Should().BeEmpty();
            result.AdvisoryWarnings.Should().Contain(message => message.Contains("Do Not Publish"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Long_and_short_provider_evidence_is_read_from_performance_diagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), "phase7-provider-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "narration-v5", "documentary-script"));
            File.WriteAllText(Path.Combine(root, "narration-v5", "documentary-script", "performance-diagnostics.json"),
                """{"generatorInvocationCount":2,"longProviderInvocationCount":1,"shortProviderInvocationCount":1}""");
            DocumentaryNarrativeLifecycleIntegrationService.ReadProviderInvocationCounts(root).Should().Be((1, 1));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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
            File.Exists(Path.Combine(root, "narration-v5", "narrative-lifecycle-validation.json")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Generator_classifier_keeps_advisories_nonblocking()
    {
        var root = Path.Combine(Path.GetTempPath(), "phase7-classifier-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "narration-v5"));
            File.WriteAllText(Path.Combine(root, "narration-v5", "generator-validation-diagnostics.json"),
                """{"longNarrationArtifactValid":true,"shortNarrationArtifactValid":true,"languageValidationPassed":true,"sceneMappingValid":true,"auroraCertified":false,"overallNarrationScore":82,"promptRecommendation":"Improve cadence","warnings":["Optional fact omitted","Duration outside guidance"]}""");

            var assessment = DocumentaryNarrativeLifecycleIntegrationService.AssessGeneratorResult(root, true, true);

            assessment.BlockingErrors.Should().BeEmpty();
            assessment.AdvisoryWarnings.Should().Contain(message => message.Contains("Aurora"));
            assessment.AdvisoryWarnings.Should().Contain(message => message.Contains("Optional fact"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("{\"longNarrationArtifactValid\":false,\"shortNarrationArtifactValid\":true,\"languageValidationPassed\":true,\"sceneMappingValid\":true}")]
    [InlineData("{\"longNarrationArtifactValid\":true,\"shortNarrationArtifactValid\":true,\"languageValidationPassed\":false,\"sceneMappingValid\":true}")]
    [InlineData("{\"longNarrationArtifactValid\":true,\"shortNarrationArtifactValid\":true,\"languageValidationPassed\":true,\"sceneMappingValid\":false}")]
    public void Generator_classifier_blocks_material_generation_failures(string diagnostics)
    {
        var root = Path.Combine(Path.GetTempPath(), "phase7-classifier-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "narration-v5"));
            File.WriteAllText(Path.Combine(root, "narration-v5", "generator-validation-diagnostics.json"), diagnostics);
            DocumentaryNarrativeLifecycleIntegrationService.AssessGeneratorResult(root, true, true)
                .BlockingErrors.Should().NotBeEmpty();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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
