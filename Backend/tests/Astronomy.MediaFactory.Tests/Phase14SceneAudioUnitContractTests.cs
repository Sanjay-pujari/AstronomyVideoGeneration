using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase14SceneAudioUnitContractTests
{
    [Fact]
    public void Phase14SceneAudioUnitMayContainMultipleSubtitleSegments_WithoutCreatingTtsUnits()
    {
        var unitId = "sau-short-s01-en";
        var subtitles = Enumerable.Range(1, 5).Select(i => new SubtitleSegment($"sub-{i}", i, unitId, "S01",
            ["sentence-1", "sentence-2", "sentence-3"], $"segment {i}", $"hash-{i}", 900,
            $"segment {i}", null, null, null, AudioSyncBreakReason.Sentence)).ToArray();
        var unit = new SceneAudioUnit(unitId, 1, "Short", "en", "S01", "beat-1",
            ["sentence-1", "sentence-2", "sentence-3"], 0, 2,
            "First sentence. Second sentence. Third sentence.", "text-hash", 3000, 0, 250,
            AudioSyncBreakReason.Scene, subtitles, "voice-profile:en", "documentary-neutral", false,
            ["phase7#S01"], ["phase10#S01"]);

        Assert.Equal(3, unit.SentenceIds.Count);
        Assert.Equal(5, unit.SubtitleSegments.Count);
        Assert.Equal(1, Phase15SceneAudioUnitAdapter.ProductionSynthesisRequestCount([unit]));
        Assert.All(unit.SubtitleSegments, segment => Assert.Equal(unit.SceneAudioUnitId, segment.SceneAudioUnitId));
        Assert.False(unit.MayCrossSceneBoundary);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EnglishAndHindiUseSameTtsBoundaryModel(string language)
    {
        var unit = new SceneAudioUnit($"sau-{language}", 1, "Short", language, "S01", "beat-1", ["s1"], 0, 0,
            language == "hi" ? "आकाश सुंदर है।" : "The sky is beautiful.", "hash", 1000, 0, 0,
            AudioSyncBreakReason.Scene, [], $"voice-profile:{language}", "neutral", false, [], []);
        Assert.Equal(1, Phase15SceneAudioUnitAdapter.ProductionSynthesisRequestCount([unit]));
    }
}
