using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationProviderOutputTests
{
    [Fact]
    public async Task ProviderCounterIncrementsOnlyAtConcreteAdapter()
    {
        var concrete = new CountingPerformer();
        INarrationPerformer performer = concrete;
        var request = new NarrationProviderCall("call-1", "long-1", "long", "system", "facts", "checksum");

        // Creating request/output-shaped data is deliberately inert.
        _ = JsonSerializer.Serialize(request);
        _ = "{\"scenes\":[]}";
        Assert.Equal(0, concrete.InvocationCount);

        var result = await performer.InvokeAsync(request, default);

        Assert.Equal(1, concrete.InvocationCount);
        Assert.Equal("call-1", result.ProviderCallId);
        Assert.NotEmpty(result.ResponseChecksum);
    }

    [Fact]
    public void RealizedPrompt_ProjectsFactsWithoutMachineIdentity()
    {
        var beat = new NarrationContextBeat("Advance01", "Help viewers recognize Orion by its Belt.", "producer: use an overlay", [], [], null, "internal-transition-01", "calm", "measured", [], "final narration remains owned by Phase 7", "SCENE-SECRET-01", 1, "long", 45, null);
        var context = new NarrationContextDocument("v", "test", [new NarrationFormatContext("long", [beat])]);
        var realization = new NarrationRealizationResult("long", "SCENE-SECRET-01", "Advance01", "authority-secret", "constellation", "Advance01",
            "Help viewers recognize Orion quickly in the night sky.",
            [new("ClaimId=CLAIM-SECRET", "named-stars", "Orion's Belt stars", "Alnitak, Alnilam, and Mintaka form a conspicuous visual line.")],
            ["The three stars form an apparent pattern and are not physically close."], [],
            new("basic recognition", "Orion's major stars", "internal-transition-01"), "calm", "measured", 100, null, null, [], "Open with wonder");

        var prompt = new NarrationPromptComposer().Compose(new NarrationPromptComposerInput(context, [], "/tmp/prompt.md", "/tmp/prompt.json", Realizations: [realization])).PromptPreviewMarkdown;

        Assert.Contains("Alnitak, Alnilam, and Mintaka", prompt);
        Assert.Contains("Help viewers recognize Orion quickly", prompt);
        Assert.DoesNotContain("SCENE-SECRET-01", prompt);
        Assert.DoesNotContain("CLAIM-SECRET", prompt);
        Assert.DoesNotContain("authority-secret", prompt);
        Assert.DoesNotContain("internal-transition-01", prompt);
        Assert.DoesNotContain("producer: use an overlay", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderResponse_MapsCompleteSceneNumbersAndPreservesText()
    {
        var parsed = Parse("""{"variant":"Long","scenes":[{"sceneNumber":1,"narrationText":"Orion rises with three Belt stars forming its unmistakable center."},{"sceneNumber":2,"narrationText":"Betelgeuse marks a warm-colored shoulder while blue-white Rigel anchors a foot."}]}""", "long", 2);

        Assert.Equal("Orion rises with three Belt stars forming its unmistakable center.", parsed[1]);
        Assert.Equal("Betelgeuse marks a warm-colored shoulder while blue-white Rigel anchors a foot.", parsed[2]);
    }

    [Fact]
    public void StructurallyEmptyProviderResponse_IsRejectedDuringParsing()
        => Assert.Throws<TargetInvocationException>(() => Parse("{\"variant\":\"Long\",\"scenes\":[]}", "long", 1));

    [Fact]
    public void ProviderResponse_RejectsExplanatoryFields()
        => Assert.Throws<TargetInvocationException>(() => Parse("""{"variant":"Long","scenes":[{"sceneNumber":1,"narrationText":"Natural prose.","producerAdvice":"Use a dramatic pause."}]}""", "long", 1));

    [Fact]
    public void ProviderResponse_CleansOnlyHarmlessWrappers()
    {
        var parsed = Parse(
            """
            ```json
            {"variant":"Long","scenes":[{"sceneNumber":1,"narrationText":"Narration: \"Orion's Belt points across the winter sky.\""}]}
            ```
            """,
            "long",
            1);
        Assert.Equal("Orion's Belt points across the winter sky.", parsed[1]);
    }

    [Theory]
    [InlineData("Advance01 introduces the next idea.")]
    [InlineData("final narration remains owned by Phase 7")]
    public void ProviderNarrationLeakage_IsParsedThenRejectedByPostProviderValidation(string narrationText)
    {
        var response = $$"""{"variant":"Long","scenes":[{"sceneNumber":1,"narrationText":"{{narrationText}}"}]}""";
        Assert.Equal(narrationText, Parse(response, "long", 1)[1]);
        Assert.Contains(GeneratedNarrationValidator.Validate(narrationText), failure =>
            failure.DetectedIssue == "ProviderInternalIdentifierOrPlaceholder");
    }

    [Fact]
    public void RepairAttempt_ChangesPromptChecksumMaterial()
    {
        var first = BuildPrompt([]);
        var repaired = BuildPrompt(["Remove all IDs and internal labels."]);

        Assert.NotEqual(first, repaired);
        Assert.Contains("Remove all IDs", repaired);
        Assert.Contains("Write Short independently from Long", repaired);
    }

    [Fact]
    public void ObjectNamesAndFragmentaryTokens_AreNotGroundedStatements()
    {
        var realization = new NarrationRealizationResult("long", "internal", "Outcome04", "authority", "ScientificExplanation", "Advance04",
            "Orion Gold scene 04 outcome Outcome04.",
            [new("name", "object", "Betelgeuse", null), new("science", "statement", "Betelgeuse is a red supergiant", null),
             new("token", "fragment", "Ori", null), new("token", "fragment", "Orionis", null), new("token", "fragment", "Constellation", null)],
            [], [], new("Advance04", "Outcome04", "Advance04"), "calm", "measured", 100, null, null, [], "");

        var projection = ProviderSemanticProjection.Project(realization);

        Assert.Contains("Betelgeuse is a red supergiant.", projection.FactualStatements);
        Assert.Contains("Betelgeuse", projection.ObjectVocabulary);
        Assert.DoesNotContain("Ori", projection.FactualStatements);
        Assert.DoesNotContain("Orionis", projection.FactualStatements);
        Assert.DoesNotContain("Constellation", projection.FactualStatements);
        Assert.DoesNotContain("Advance04", projection.Purpose);
        Assert.DoesNotContain("Outcome04", projection.Transition);
    }

    [Fact]
    public void ObservationDetails_AreProjectedFromTheirValuesAndUnits()
    {
        var realization = new NarrationRealizationResult("long", "internal", "Observation", "authority", "ScientificExplanation", "Observation",
            "Explain how to observe Orion.", [], [],
            [new("observation", "direction", "Orion rises in the eastern sky", "after sunset")],
            null, "calm", "measured", 100, null, null, [], "");

        var projection = ProviderSemanticProjection.Project(realization);

        Assert.Equal(["Orion rises in the eastern sky after sunset."], projection.ObservationStatements);
    }

    [Fact]
    public void RealizedProviderPrompt_HasNaturalSectionsAndNoInternalFactLabels()
    {
        var realization = new NarrationRealizationResult("long", "SCENE-ID", "Outcome04", "authority", "ScientificExplanation", "Advance04",
            "Explain why Orion's apparent pattern has physical depth.",
            [new("scene Fact1", "science", "Betelgeuse is a red supergiant", null)], [], [],
            new("recognizing Orion's pattern", "understanding its physical depth", "Advance04"), "calm", "measured", 100, null, null, [], "");
        var context = new NarrationContextDocument("v", "test", [new NarrationFormatContext("long", [])]);

        var prompt = new NarrationPromptComposer().Compose(new NarrationPromptComposerInput(context, [], "/tmp/prompt.md", "/tmp/prompt.json", Realizations: [realization])).PromptPreviewMarkdown;

        Assert.Contains("Grounded astronomy", prompt);
        Assert.Contains("Betelgeuse is a red supergiant", prompt);
        Assert.DoesNotContain("scene Fact1", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Advance04", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Outcome04", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SCENE-ID", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<int, string> Parse(string response, string format, int count)
        => (IReadOnlyDictionary<int, string>)typeof(NarrationGeneratorV5)
            .GetMethod("ParseProviderNarration", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [response, format, count])!;

    private static string BuildPrompt(IReadOnlyList<string> guidance)
    {
        var beat = new NarrationContextBeat("recognition", "Recognize Orion by the three Belt stars", "", [new("Belt stars", "Three aligned stars", null)], [], null, "continue", "calm", "varied", [], null, "internal-scene", 1, "short", 20, null);
        return (string)typeof(NarrationGeneratorV5).GetMethod("BuildStructuredVariantPrompt", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, ["short", new[] { beat }, "Orion's Belt contains three conspicuous aligned stars.", guidance])!;
    }

    private sealed class CountingPerformer : INarrationPerformer
    {
        public int InvocationCount { get; private set; }
        public string ProviderName => "test-provider";
        public string ModelOrDeployment => "test-model";
        public Task<string> PerformAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult("{\"variant\":\"long\",\"scenes\":[{\"sceneNumber\":1,\"narrationText\":\"Orion's three Belt stars form a conspicuous line.\"}]}");
        }
    }
}
