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
                [$"{RenderingOptions.SectionName}:EnableDirectionalMotion"] = "false"
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
                [$"{RenderingOptions.VideoEncodingSectionName}:IntermediatePreset"] = "fast",
                [$"{RenderingOptions.VideoEncodingSectionName}:IntermediateCrf"] = "21",
                [$"{RenderingOptions.VideoEncodingSectionName}:IntermediateScaleFlags"] = "bicubic",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongPreset"] = "medium",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongCrf"] = "18",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongWidth"] = "2560",
                [$"{RenderingOptions.VideoEncodingSectionName}:YouTubeLongHeight"] = "1440",
                [$"{RenderingOptions.VideoEncodingSectionName}:EnableYouTube1440pUpscale"] = "true",
                [$"{RenderingOptions.VideoEncodingSectionName}:ShortsPreset"] = "medium",
                [$"{RenderingOptions.VideoEncodingSectionName}:ShortsCrf"] = "20",
                [$"{RenderingOptions.VideoEncodingSectionName}:ShortsMaxRate"] = "12M",
                [$"{RenderingOptions.VideoEncodingSectionName}:MetaReelPreset"] = "medium",
                [$"{RenderingOptions.VideoEncodingSectionName}:MetaReelCrf"] = "21",
                [$"{RenderingOptions.VideoEncodingSectionName}:MetaReelMaxRate"] = "10M"
            })
            .Build();

        var options = new RenderingOptions();
        config.GetSection(RenderingOptions.VideoEncodingSectionName).Bind(options);

        Assert.Equal("fast", options.IntermediatePreset);
        Assert.Equal(21, options.IntermediateCrf);
        Assert.Equal("bicubic", options.IntermediateScaleFlags);
        Assert.Equal("medium", options.YouTubeLongPreset);
        Assert.Equal(18, options.YouTubeLongCrf);
        Assert.Equal(2560, options.YouTubeLongWidth);
        Assert.Equal(1440, options.YouTubeLongHeight);
        Assert.True(options.EnableYouTube1440pUpscale);
        Assert.Equal("medium", options.ShortsPreset);
        Assert.Equal(20, options.ShortsCrf);
        Assert.Equal("12M", options.ShortsMaxRate);
        Assert.Equal("medium", options.MetaReelPreset);
        Assert.Equal(21, options.MetaReelCrf);
        Assert.Equal("10M", options.MetaReelMaxRate);
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
