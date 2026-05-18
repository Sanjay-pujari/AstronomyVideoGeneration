using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class RenderingOptionsBindingTests
{
    [Fact]
    public void RenderingOptions_BindsTransitionSettingsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{RenderingOptions.SectionName}:EnableTransitions"] = "true",
                [$"{RenderingOptions.SectionName}:TransitionDurationSeconds"] = "0.5",
                [$"{RenderingOptions.SectionName}:TransitionType"] = "fade",
                [$"{RenderingOptions.SectionName}:EnableFadeInOut"] = "true",
                [$"{RenderingOptions.SectionName}:FadeDurationSeconds"] = "0.75",
                [$"{RenderingOptions.SectionName}:ShortFadeDurationSeconds"] = "0.4",
                [$"{RenderingOptions.SectionName}:EnableKenBurns"] = "true",
                [$"{RenderingOptions.SectionName}:KenBurnsZoomStart"] = "1.0",
                [$"{RenderingOptions.SectionName}:KenBurnsZoomEnd"] = "1.10",
                [$"{RenderingOptions.SectionName}:ShortKenBurnsZoomEnd"] = "1.08",
                [$"{RenderingOptions.SectionName}:KenBurnsFps"] = "30",
                [$"{RenderingOptions.SectionName}:KenBurnsUseEasing"] = "true",
                [$"{RenderingOptions.SectionName}:EnableDirectionalMotion"] = "false",
                [$"{RenderingOptions.SectionName}:FinalLongRenderTimeoutSeconds"] = "900",
                [$"{RenderingOptions.SectionName}:FinalLongTimeoutMultiplier"] = "8",
                [$"{RenderingOptions.SectionName}:FinalLongMaxTimeoutSeconds"] = "3600",
                [$"{RenderingOptions.SectionName}:RetryFinalLongRenderWithFasterProfile"] = "true",
                [$"{RenderingOptions.SectionName}:FallbackTo1080pOnFinalRenderTimeout"] = "true"
            })
            .Build();

        var options = new RenderingOptions();
        config.GetSection(RenderingOptions.SectionName).Bind(options);

        Assert.True(options.EnableTransitions);
        Assert.Equal(0.5d, options.TransitionDurationSeconds);
        Assert.Equal("fade", options.TransitionType);
        Assert.True(options.EnableFadeInOut);
        Assert.Equal(0.75d, options.FadeDurationSeconds);
        Assert.Equal(0.4d, options.ShortFadeDurationSeconds);
        Assert.True(options.EnableKenBurns);
        Assert.Equal(1.0d, options.KenBurnsZoomStart);
        Assert.Equal(1.10d, options.KenBurnsZoomEnd);
        Assert.Equal(1.08d, options.ShortKenBurnsZoomEnd);
        Assert.Equal(30, options.KenBurnsFps);
        Assert.True(options.KenBurnsUseEasing);
        Assert.False(options.EnableDirectionalMotion);
        Assert.Equal(900, options.FinalLongRenderTimeoutSeconds);
        Assert.Equal(8d, options.FinalLongTimeoutMultiplier);
        Assert.Equal(3600, options.FinalLongMaxTimeoutSeconds);
        Assert.True(options.RetryFinalLongRenderWithFasterProfile);
        Assert.True(options.FallbackTo1080pOnFinalRenderTimeout);
    }

    [Fact]
    public void VideoLengthPolicyOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{VideoLengthPolicyOptions.SectionName}:MinPrimaryObjects"] = "3",
                [$"{VideoLengthPolicyOptions.SectionName}:TargetPrimaryObjects"] = "5",
                [$"{VideoLengthPolicyOptions.SectionName}:MaxPrimaryObjects"] = "5",
                [$"{VideoLengthPolicyOptions.SectionName}:TargetFullVideoSegments"] = "7",
                [$"{VideoLengthPolicyOptions.SectionName}:MaxFullVideoSegments"] = "8",
                [$"{VideoLengthPolicyOptions.SectionName}:MinFullVideoDurationSeconds"] = "180",
                [$"{VideoLengthPolicyOptions.SectionName}:TargetFullVideoDurationSeconds"] = "240",
                [$"{VideoLengthPolicyOptions.SectionName}:MaxFullVideoDurationSeconds"] = "420"
            })
            .Build();

        var options = new VideoLengthPolicyOptions();
        config.GetSection(VideoLengthPolicyOptions.SectionName).Bind(options);

        Assert.Equal(3, options.MinPrimaryObjects);
        Assert.Equal(5, options.TargetPrimaryObjects);
        Assert.Equal(5, options.MaxPrimaryObjects);
        Assert.Equal(7, options.TargetFullVideoSegments);
        Assert.Equal(8, options.MaxFullVideoSegments);
        Assert.Equal(180, options.MinFullVideoDurationSeconds);
        Assert.Equal(240, options.TargetFullVideoDurationSeconds);
        Assert.Equal(420, options.MaxFullVideoDurationSeconds);
    }

    [Fact]
    public void RenderingOptions_BindsVideoEncodingUpscaleConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{RenderingOptions.VideoEncodingSectionName}:EnableYouTube1440pUpscale"] = "true"
            })
            .Build();

        var options = new RenderingOptions();
        config.GetSection(RenderingOptions.VideoEncodingSectionName).Bind(options);

        Assert.True(options.EnableYouTube1440pUpscale);
    }


    [Fact]
    public void RenderingOptions_BindsVideoEncodingProfilesFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{RenderingOptions.VideoEncodingSectionName}:IntermediatePreset"] = "veryfast",
                [$"{RenderingOptions.VideoEncodingSectionName}:IntermediateCrf"] = "22",
                [$"{RenderingOptions.VideoEncodingSectionName}:IntermediateScaleFlags"] = "bicubic",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongPreset"] = "veryfast",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongCrf"] = "20",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongWidth"] = "2560",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongHeight"] = "1440",
                [$"{RenderingOptions.VideoEncodingSectionName}:EnableYouTube1440pUpscale"] = "true",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongQualityMode"] = "Balanced",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongMaxRate"] = "20M",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongBufferSize"] = "40M",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongAudioBitrate"] = "128k",
                [$"{RenderingOptions.VideoEncodingSectionName}:ShortsPreset"] = "fast",
                [$"{RenderingOptions.VideoEncodingSectionName}:ShortsCrf"] = "21",
                [$"{RenderingOptions.VideoEncodingSectionName}:ShortsMaxRate"] = "12M",
                [$"{RenderingOptions.VideoEncodingSectionName}:ShortsAudioBitrate"] = "128k",
                [$"{RenderingOptions.VideoEncodingSectionName}:MetaReelPreset"] = "fast",
                [$"{RenderingOptions.VideoEncodingSectionName}:MetaReelCrf"] = "22",
                [$"{RenderingOptions.VideoEncodingSectionName}:MetaReelMaxRate"] = "10M",
                [$"{RenderingOptions.VideoEncodingSectionName}:MetaReelAudioBitrate"] = "128k"
            })
            .Build();

        var options = new RenderingOptions();
        config.GetSection(RenderingOptions.VideoEncodingSectionName).Bind(options);

        Assert.Equal("veryfast", options.IntermediatePreset);
        Assert.Equal(22, options.IntermediateCrf);
        Assert.Equal("bicubic", options.IntermediateScaleFlags);
        Assert.Equal("veryfast", options.YouTubeLongPreset);
        Assert.Equal(20, options.YouTubeLongCrf);
        Assert.Equal(2560, options.YouTubeLongWidth);
        Assert.Equal(1440, options.YouTubeLongHeight);
        Assert.True(options.EnableYouTube1440pUpscale);
        Assert.Equal("Balanced", options.YouTubeLongQualityMode);
        Assert.Equal("20M", options.YouTubeLongMaxRate);
        Assert.Equal("40M", options.YouTubeLongBufferSize);
        Assert.Equal("128k", options.YouTubeLongAudioBitrate);
        Assert.Equal("fast", options.ShortsPreset);
        Assert.Equal(21, options.ShortsCrf);
        Assert.Equal("12M", options.ShortsMaxRate);
        Assert.Equal("128k", options.ShortsAudioBitrate);
        Assert.Equal("fast", options.MetaReelPreset);
        Assert.Equal(22, options.MetaReelCrf);
        Assert.Equal("10M", options.MetaReelMaxRate);
        Assert.Equal("128k", options.MetaReelAudioBitrate);
    }

    [Fact]
    public void ThumbnailCinematicAIOptions_BindsPhase3Configuration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ThumbnailCinematicAIOptions.SectionName}:Enabled"] = "true",
                [$"{ThumbnailCinematicAIOptions.SectionName}:EnableSmartCropping"] = "true",
                [$"{ThumbnailCinematicAIOptions.SectionName}:EnableObjectFocusEnhancement"] = "true",
                [$"{ThumbnailCinematicAIOptions.SectionName}:EnableColorMoodGrading"] = "true",
                [$"{ThumbnailCinematicAIOptions.SectionName}:EnableVisualHierarchyOptimization"] = "true",
                [$"{ThumbnailCinematicAIOptions.SectionName}:EnablePlanetMoonEnhancement"] = "true",
                [$"{ThumbnailCinematicAIOptions.SectionName}:EnableConjunctionFraming"] = "true",
                [$"{ThumbnailCinematicAIOptions.SectionName}:EnablePortraitSafeCropping"] = "true",
                [$"{ThumbnailCinematicAIOptions.SectionName}:AllowedMoodProfiles:0"] = "dramatic",
                [$"{ThumbnailCinematicAIOptions.SectionName}:AllowedMoodProfiles:1"] = "warmGlow",
                [$"{ThumbnailCinematicAIOptions.SectionName}:PreventFakeAstronomy"] = "true",
                [$"{ThumbnailCinematicAIOptions.SectionName}:MaximumObjectScaleBoost"] = "1.35",
                [$"{ThumbnailCinematicAIOptions.SectionName}:OutputFileName"] = "thumbnail-cinematic-ai-report.json"
            })
            .Build();

        var options = new ThumbnailCinematicAIOptions();
        config.GetSection(ThumbnailCinematicAIOptions.SectionName).Bind(options);

        Assert.True(options.Enabled);
        Assert.True(options.EnablePortraitSafeCropping);
        Assert.True(options.PreventFakeAstronomy);
        Assert.Equal(1.35d, options.MaximumObjectScaleBoost);
        Assert.Contains("warmGlow", options.AllowedMoodProfiles);
        Assert.Equal("thumbnail-cinematic-ai-report.json", options.OutputFileName);
    }

    [Fact]
    public void ThumbnailOptions_DefaultsToPremiumDocumentaryPreset()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ThumbnailOptions.SectionName}:VisualPreset"] = "Premium Documentary"
            })
            .Build();

        var options = new ThumbnailOptions();
        config.GetSection(ThumbnailOptions.SectionName).Bind(options);

        Assert.Equal("Premium Documentary", options.VisualPreset);
    }

}
