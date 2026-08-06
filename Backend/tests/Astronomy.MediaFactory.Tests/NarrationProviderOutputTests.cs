using System.Reflection;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationProviderOutputTests
{
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
}
