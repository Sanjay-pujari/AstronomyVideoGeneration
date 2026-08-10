using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase16DurationCalibrationAuthorityTests
{
    [Fact]
    public void PlannedVisualLongerThanAudio_IsRetained()
    {
        var result = Phase16DurationCalibrationPublisher.Calibrate(30_000, 1_000, 24_000);

        Assert.Equal(30_000, result.FinalDurationMs);
        Assert.Equal("PlannedVisualRetained", result.Reason);
        Assert.Equal("Phase6StoryFrameIndexEstimatedDuration", result.Source);
    }

    [Fact]
    public void AudioLongerThanPlanned_ExtendsVisual()
    {
        var result = Phase16DurationCalibrationPublisher.Calibrate(20_000, 1_000, 26_000);

        Assert.Equal(26_000, result.FinalDurationMs);
        Assert.Equal("AudioExtendedVisual", result.Reason);
    }

    [Fact]
    public void MissingPlannedDuration_UsesConfiguredMinimumFallback()
    {
        var result = Phase16DurationCalibrationPublisher.Calibrate(null, 1_000, 8_000);

        Assert.Equal(8_000, result.FinalDurationMs);
        Assert.Equal("ConfiguredMinimumVisualDurationMs", result.Source);
    }

    [Fact]
    public void PlannedDurationJoin_IsStableByFormatAndSceneId()
    {
        var shuffled = new Dictionary<string, long>
        {
            [Phase16DurationCalibrationPublisher.PlannedDurationKey("Long", "scene-b")] = 42_000,
            [Phase16DurationCalibrationPublisher.PlannedDurationKey("Short", "scene-a")] = 30_000
        };

        Assert.Equal(30_000, shuffled[Phase16DurationCalibrationPublisher.PlannedDurationKey("Short", "scene-a")]);
        Assert.Equal(42_000, shuffled[Phase16DurationCalibrationPublisher.PlannedDurationKey("Long", "scene-b")]);
    }

    [Fact]
    public void PlannedDurationChange_InvalidatesReuseIdentity()
    {
        var first = new Dictionary<string, long> { ["short\nscene-a"] = 30_000 };
        var changed = new Dictionary<string, long> { ["short\nscene-a"] = 31_000 };

        Assert.NotEqual(
            Phase16DurationCalibrationPublisher.PlannedDurationReuseIdentity("phase6-checksum", first),
            Phase16DurationCalibrationPublisher.PlannedDurationReuseIdentity("phase6-checksum", changed));
    }
}
