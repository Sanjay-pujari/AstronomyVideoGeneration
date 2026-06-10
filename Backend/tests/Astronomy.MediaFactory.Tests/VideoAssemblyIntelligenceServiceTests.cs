using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class VideoAssemblyIntelligenceServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private const string EventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateVideoAssemblyAsync_IntelligenceNonDryRunWritesVideoAssemblyIntelligenceOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Intelligence",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var outputPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-assembly-intelligence.json");
        Assert.True(result.VideoAssemblyIntelligenceGenerated);
        Assert.Equal("Intelligence", result.PhaseRequested);
        Assert.Equal("Intelligence", result.PhaseExecuted);
        Assert.Equal(outputPath.Replace('\\', '/'), result.VideoAssemblyIntelligencePath);
        Assert.Equal("DON'T MISS THIS TONIGHT", result.SelectedOpeningHook);
        Assert.Equal(20.0, result.RecommendedTotalDurationSeconds);
        Assert.True(result.TtsRequired);
        Assert.True(result.FinalVideoPlanned);
        Assert.Empty(result.GeneratedFiles);
        Assert.True(File.Exists(outputPath));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase));

        var saved = JsonSerializer.Deserialize<VideoAssemblyIntelligenceDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(EventId, saved!.EventId);
        Assert.Equal(RegionId, saved.RegionId);
        Assert.Equal("en", saved.Language);
        Assert.Equal("YouTubeShort", saved.Platform);
        Assert.Equal("DON'T MISS THIS TONIGHT", saved.SelectedOpeningHook);
        Assert.Equal("ShortFormAstronomyAlert", saved.VideoIntent);
        Assert.Equal("Curiosity → Clarity → Action", saved.EmotionalArc);
        Assert.Equal(new[] { "Hook", "What", "Why", "Where", "When", "Action" }, saved.RecommendedSceneOrder);
        Assert.Equal(6, saved.RecommendedSceneDurations.Count);
        Assert.Equal(20.0, saved.RecommendedTotalDurationSeconds);
        Assert.True(saved.AudioPlan.TtsRequired);
        Assert.True(saved.OutputsPlanned.Contains("final-video-short.mp4"));
        Assert.True(saved.Scores.VideoAssemblyReadinessScore >= 90);
        Assert.Empty(saved.Warnings);
    }


    [Fact]
    public async Task GenerateVideoAssemblyAsync_LongFormIntelligenceWritesLongFolderContract()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeLong",
            Phase = "LongFormIntelligence",
            ScenePresentationProfile = ScenePresentationProfile.LongForm,
            DryRun = false,
            OverwriteExisting = true,
            LongForm = new VideoAssemblyFormRequest
            {
                Enabled = true,
                Platform = "YouTubeLong",
                ScenePresentationProfile = ScenePresentationProfile.LongForm,
                TargetDurationSeconds = 180,
                BackgroundMusic = true,
                MusicMood = "WonderCuriosity",
                MusicLevelPercent = 18,
                DuckMusicUnderNarration = true
            }
        }, CancellationToken.None);

        var outputPath = Path.Combine(BuildLongVideoAssemblyRoot(workingDirectory), "video-assembly-long-intelligence.json");
        Assert.True(result.VideoAssemblyIntelligenceGenerated);
        Assert.Equal("LongFormIntelligence", result.PhaseRequested);
        Assert.Equal(outputPath.Replace('\\', '/'), result.VideoAssemblyIntelligencePath);
        Assert.True(File.Exists(outputPath));

        var saved = JsonSerializer.Deserialize<VideoAssemblyIntelligenceDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal("EducationalAstronomyGuide", saved!.VideoIntent);
        Assert.Equal(180, saved.TargetDurationSeconds);
        Assert.Equal(ScenePresentationProfile.LongForm, saved.RecommendedScenePresentationProfile);
        Assert.EndsWith("scene-approval-v3/long/", saved.RecommendedSceneDirectory);
        Assert.Equal(new[] { "Hook", "WhatIsHappening", "AboutVenus", "AboutJupiter", "WhyTheyAppearClose", "WhereToLook", "WhenToLook", "HowToObserve", "WhatYouWillSee", "InterestingFact", "ObservationTips", "Recap", "Action" }, saved.LongFormSections);
    }


    [Fact]
    public async Task GenerateVideoAssemblyAsync_LongFormScriptWritesWordCountEstimatedNarration()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeLong",
            Phase = "LongFormIntelligence",
            ScenePresentationProfile = ScenePresentationProfile.LongForm,
            DryRun = false,
            OverwriteExisting = true,
            LongForm = new VideoAssemblyFormRequest
            {
                Enabled = true,
                Platform = "YouTubeLong",
                ScenePresentationProfile = ScenePresentationProfile.LongForm,
                TargetDurationSeconds = 180,
                BackgroundMusic = true,
                MusicMood = "WonderCuriosity",
                MusicLevelPercent = 18,
                DuckMusicUnderNarration = true
            }
        }, CancellationToken.None);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeLong",
            Phase = "LongFormScript",
            ScenePresentationProfile = ScenePresentationProfile.LongForm,
            DryRun = false,
            OverwriteExisting = true,
            LongForm = new VideoAssemblyFormRequest
            {
                Enabled = true,
                Platform = "YouTubeLong",
                ScenePresentationProfile = ScenePresentationProfile.LongForm,
                TargetDurationSeconds = 180,
                BackgroundMusic = true,
                MusicMood = "WonderCuriosity",
                MusicLevelPercent = 18,
                DuckMusicUnderNarration = true
            }
        }, CancellationToken.None);

        var outputPath = Path.Combine(BuildLongVideoAssemblyRoot(workingDirectory), "video-long-narration-script.json");
        Assert.Equal("LongFormScript", result.PhaseRequested);
        Assert.Equal("LongFormScript", result.PhaseExecuted);
        Assert.True(result.VideoNarrationScriptGenerated);
        Assert.Equal(outputPath.Replace('\\', '/'), result.VideoNarrationScriptPath);
        Assert.InRange(result.TotalEstimatedDurationSeconds, 120, 180);
        Assert.True(File.Exists(outputPath));

        var saved = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal("YouTubeLong", saved!.Platform);
        Assert.Equal(new[] { "Hook", "WhatIsHappening", "AboutVenus", "AboutJupiter", "WhyTheyAppearClose", "WhereToLook", "WhenToLook", "HowToObserve", "WhatYouWillSee", "InterestingFact", "ObservationTips", "Recap", "Action" }, saved.SceneScripts.Select(scene => scene.SceneKey));
        Assert.InRange(saved.TotalEstimatedDurationSeconds, 120, 180);

        var totalWords = CountTestWords(saved.FullNarrationText);
        Assert.InRange(totalWords, 330, 430);
        Assert.Equal(Math.Round(totalWords / 150.0 * 60.0, 3, MidpointRounding.AwayFromZero), saved.TotalEstimatedDurationSeconds);
        Assert.All(saved.SceneScripts, scene =>
        {
            Assert.InRange(CountTestWords(scene.Narration), 25, 35);
            Assert.True(scene.Narration.Count(c => c == '.') >= 2);
        });
        Assert.Equal("video-long-tts-audio.mp3", saved.TtsPlan.OutputFileName);
    }



    [Fact]
    public async Task GenerateVideoAssemblyAsync_ScriptNonDryRunWritesNarrationScriptOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Intelligence",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Script",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var outputPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-narration-script.json");
        Assert.Equal("Script", result.PhaseRequested);
        Assert.Equal("Script", result.PhaseExecuted);
        Assert.True(result.VideoNarrationScriptGenerated);
        Assert.Equal(outputPath.Replace('\\', '/'), result.VideoNarrationScriptPath);
        Assert.Equal(20.0, result.TotalEstimatedDurationSeconds);
        Assert.True(result.TtsReady);
        Assert.Empty(result.GeneratedFiles);
        Assert.True(File.Exists(outputPath));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase));

        var saved = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(EventId, saved!.EventId);
        Assert.Equal(RegionId, saved.RegionId);
        Assert.Equal("en", saved.Language);
        Assert.Equal("YouTubeShort", saved.Platform);
        Assert.Equal(20.0, saved.TotalEstimatedDurationSeconds);
        Assert.Equal("Excited but clear", saved.ScriptStyle.Tone);
        Assert.Equal(new[] { "Hook", "What", "Why", "Where", "When", "Action" }, saved.SceneScripts.Select(scene => scene.SceneKey));
        Assert.Equal("Don't miss this tonight.", saved.SceneScripts[0].Narration);
        Assert.Equal("Step outside tonight and look west.", saved.SceneScripts[^1].Narration);
        Assert.Equal("Don't miss this tonight. Venus and Jupiter will shine close together after sunset. Two of the brightest worlds will share the evening sky. Look toward the western sky. The best time is shortly after sunset. Step outside tonight and look west.", saved.FullNarrationText);
        Assert.True(saved.TtsPlan.TtsRequired);
        Assert.Equal("NeutralEnergetic", saved.TtsPlan.RecommendedVoice);
        Assert.Equal("video-tts-audio.mp3", saved.TtsPlan.OutputFileName);
        Assert.True(saved.Scores.TtsReadinessScore >= 90);
        Assert.Empty(saved.Warnings);
    }

    [Fact]
    public async Task GenerateVideoAssemblyAsync_DryRunReturnsPreviewPathWithoutWriting()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Intelligence",
            DryRun = true,
            OverwriteExisting = true
        }, CancellationToken.None);

        var outputPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-assembly-intelligence.json");
        Assert.True(result.VideoAssemblyIntelligenceGenerated);
        Assert.Equal("DON'T MISS THIS TONIGHT", result.SelectedOpeningHook);
        Assert.Equal(outputPath.Replace('\\', '/'), result.VideoAssemblyIntelligencePath);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task GenerateVideoAssemblyAsync_TtsNonDryRunWritesAudioAndTimingsOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Intelligence",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Script",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Tts",
            DryRun = false,
            OverwriteExisting = true,
            AllowSyntheticSilentTts = true
        }, CancellationToken.None);

        var audioPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-tts-audio.mp3");
        var timingsPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-tts-timings.json");
        Assert.Equal("Tts", result.PhaseRequested);
        Assert.Equal("Tts", result.PhaseExecuted);
        Assert.True(result.TtsAudioGenerated);
        Assert.True(result.TtsTimingsGenerated);
        Assert.Equal(audioPath.Replace('\\', '/'), result.AudioFilePath);
        Assert.Equal(timingsPath.Replace('\\', '/'), result.TimingsFilePath);
        Assert.Equal(20.0, result.ActualDurationSeconds);
        Assert.Equal("SyntheticOfflineTtsV1", result.TtsProvider);
        Assert.True(result.IsSyntheticTts);
        Assert.True(result.IsSilentAudio);
        Assert.False(result.AudioValidationPassed);
        Assert.True(File.Exists(audioPath));
        Assert.True(File.Exists(timingsPath));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase));

        var saved = JsonSerializer.Deserialize<VideoTtsTimingsDto>(await File.ReadAllTextAsync(timingsPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(EventId, saved!.EventId);
        Assert.Equal(RegionId, saved.RegionId);
        Assert.Equal("en", saved.Language);
        Assert.Equal("YouTubeShort", saved.Platform);
        Assert.Equal(audioPath.Replace('\\', '/'), saved.AudioFilePath);
        Assert.Equal(20.0, saved.EstimatedDurationSeconds);
        Assert.Equal(20.0, saved.ActualDurationSeconds);
        Assert.Equal(new[] { "Hook", "What", "Why", "Where", "When", "Action" }, saved.SceneTimings.Select(scene => scene.SceneKey));
        Assert.Equal(0.0, saved.SceneTimings[0].StartSeconds);
        Assert.Equal(3.0, saved.SceneTimings[0].EndSeconds);
        Assert.Equal("Don't miss this tonight.", saved.SceneTimings[0].Narration);
        Assert.Equal(20.0, saved.SceneTimings[^1].EndSeconds);
        Assert.Equal("SyntheticOfflineTtsV1", saved.TtsProvider);
        Assert.Equal("NeutralEnergetic", saved.VoiceUsed);
        Assert.NotNull(saved.AudioValidation);
        Assert.True(saved.AudioValidation!.IsSilentAudio);
        Assert.False(saved.AudioValidation.AudioValidationPassed);
    }

    [Fact]
    public async Task GenerateVideoAssemblyAsync_TtsNonDryRunWithoutRealProviderFailsClearly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Intelligence",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Script",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var originalOpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
            {
                EventId = EventId,
                RegionId = RegionId,
                Language = "en",
                Platform = "YouTubeShort",
                Phase = "Tts",
                DryRun = false,
                OverwriteExisting = true
            }, CancellationToken.None));

            Assert.Equal("Real TTS provider is not configured. SyntheticOfflineTtsV1 is disabled for dryRun=false.", error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalOpenAiApiKey);
        }
    }


    [Fact]
    public async Task GenerateVideoAssemblyAsync_AssemblyNonDryRunWritesPlanOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);
        await WriteAssemblyPhaseInputsAsync(workingDirectory, service);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Assembly",
            ScenePresentationProfile = ScenePresentationProfile.ShortForm,
            BackgroundMusic = true,
            MusicMood = "WonderCuriosity",
            MusicLevelPercent = 12,
            DuckMusicUnderNarration = true,
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var planPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-assembly-plan.json");
        Assert.Equal("Assembly", result.PhaseRequested);
        Assert.Equal("Assembly", result.PhaseExecuted);
        Assert.True(result.VideoAssemblyPlanGenerated);
        Assert.Equal(planPath.Replace('\\', '/'), result.VideoAssemblyPlanPath);
        Assert.True(result.ReadyForRender);
        Assert.Equal(6, result.SegmentCount);
        Assert.True(result.RenderUsedShortScenes);
        Assert.False(result.RenderUsedLongScenes);
        Assert.True(result.SceneMappingValid);
        Assert.True(result.BackgroundMusicPlanned);
        Assert.Equal(12, result.MusicLevelPercent);
        Assert.Equal(21.456, result.TotalDurationSeconds);
        Assert.Empty(result.GeneratedFiles);
        Assert.True(File.Exists(planPath));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase));

        var saved = JsonSerializer.Deserialize<VideoAssemblyPlanDto>(await File.ReadAllTextAsync(planPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(EventId, saved!.EventId);
        Assert.Equal(RegionId, saved.RegionId);
        Assert.Equal("en", saved.Language);
        Assert.Equal("YouTubeShort", saved.Platform);
        Assert.Equal(ScenePresentationProfile.ShortForm, saved.ScenePresentationProfile);
        Assert.EndsWith("/scene-approval-v3/short/", saved.SceneImageBaseDirectory);
        Assert.Equal(6, saved.SceneCount);
        Assert.Equal(6, saved.SceneImages.Count);
        Assert.All(saved.SceneImages, path => Assert.Contains("/scene-approval-v3/short/", path));
        Assert.Equal(21.456, saved.TotalDurationSeconds);
        Assert.Equal(Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-tts-audio.mp3").Replace('\\', '/'), saved.AudioFilePath);
        Assert.Equal(Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "final-video-short.mp4").Replace('\\', '/'), saved.RenderOutputPath);
        Assert.Equal(1080, saved.RenderSettings.Width);
        Assert.Equal(1920, saved.RenderSettings.Height);
        Assert.Equal(30, saved.RenderSettings.Fps);
        Assert.Equal("mp4", saved.RenderSettings.Format);
        Assert.Equal("h264", saved.RenderSettings.Codec);
        Assert.Equal("aac", saved.RenderSettings.AudioCodec);
        Assert.Equal("CrossFade", saved.Style.TransitionStyle);
        Assert.Equal("SubtleKenBurns", saved.Style.MotionStyle);
        Assert.Equal("UseExistingSceneTextOnly", saved.Style.TextOverlayStyle);
        Assert.True(saved.BackgroundMusic);
        Assert.True(saved.Style.BackgroundMusic);
        Assert.True(saved.SceneMappingValidation.HookUsesScene001);
        Assert.True(saved.SceneMappingValidation.WhatUsesScene001);
        Assert.True(saved.SceneMappingValidation.WhyUsesScene005);
        Assert.True(saved.SceneMappingValidation.WhereUsesScene002);
        Assert.True(saved.SceneMappingValidation.WhenUsesScene003);
        Assert.True(saved.SceneMappingValidation.ActionUsesScene006);
        Assert.True(saved.SceneMappingValidation.SceneMappingValid);
        Assert.True(saved.RenderMusicPlan.BackgroundMusic);
        Assert.Equal("WonderCuriosity", saved.RenderMusicPlan.MusicMood);
        Assert.Equal(12, saved.RenderMusicPlan.MusicLevelPercent);
        Assert.True(saved.RenderMusicPlan.DuckMusicUnderNarration);
        Assert.True(saved.Validation.AudioExists);
        Assert.True(saved.Validation.AllVisualAssetsExist);
        Assert.Equal(6, saved.Validation.SegmentCount);
        Assert.True(saved.Validation.DurationMatchesAudio);
        Assert.True(saved.Validation.ReadyForRender);
        Assert.Empty(saved.Warnings);
        Assert.Equal(new[] { "Hook", "What", "Why", "Where", "When", "Action" }, saved.Segments.Select(segment => segment.SceneKey));
        Assert.Equal(0.0, saved.Segments[0].StartSeconds);
        Assert.Equal(3.218, saved.Segments[0].EndSeconds);
        Assert.Equal(3.218, saved.Segments[0].DurationSeconds);
        Assert.EndsWith("scene-001-final.png", saved.Segments[0].VisualAssetPath);
        Assert.Contains("/scene-approval-v3/short/", saved.Segments[0].VisualAssetPath);
        Assert.Equal("Don't miss this tonight.", saved.Segments[0].Narration);
        Assert.Equal("None", saved.Segments[0].TransitionIn);
        Assert.Equal("CrossFade", saved.Segments[0].TransitionOut);
        Assert.Equal("HookThumbnailZoomIn100To105", saved.Segments[0].Motion);
        Assert.EndsWith("scene-005-final.png", saved.Segments[2].VisualAssetPath);
        Assert.EndsWith("scene-001-final.png", saved.Segments[1].VisualAssetPath);
        Assert.EndsWith("scene-002-final.png", saved.Segments[3].VisualAssetPath);
        Assert.EndsWith("scene-003-final.png", saved.Segments[4].VisualAssetPath);
        Assert.Equal(18.238, saved.Segments[5].StartSeconds);
        Assert.Equal(21.456, saved.Segments[5].EndSeconds);
        Assert.Equal("None", saved.Segments[5].TransitionOut);
    }

    [Fact]
    public async Task GenerateVideoAssemblyAsync_AssemblyFailsWhenVisualAssetMissing()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);
        await WriteAssemblyPhaseInputsAsync(workingDirectory, service);
        File.Delete(Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine", "scene-approval-v3", "short", "scene-006-final.png"));

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Assembly",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None));

        Assert.Contains("Required video assembly visual asset", error.Message);
        Assert.Contains("scene-006-final.png", error.Message);
        Assert.False(File.Exists(Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-assembly-plan.json")));
    }

    [Fact]
    public async Task GenerateVideoAssemblyAsync_RenderDryRunReportsRenderPolishValidation()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);
        await WriteAssemblyPhaseInputsAsync(workingDirectory, service);
        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Assembly",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Render",
            DryRun = true,
            BackgroundMusic = true,
            MusicLevelPercent = 30,
            DuckMusicUnderNarration = false
        }, CancellationToken.None);

        Assert.Equal("Render", result.PhaseExecuted);
        Assert.True(result.RenderSucceeded);
        Assert.Equal(ScenePresentationProfile.ShortForm, result.ScenePresentationProfileUsed);
        Assert.True(result.RenderUsedShortScenes);
        Assert.Equal(6, result.ShortFormSceneCount);
        Assert.Equal(Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-render-validation.json").Replace('\\', '/'), result.VideoRenderValidationPath);
        Assert.True(result.RenderPolishScore >= 90);
        Assert.True(result.VideoFinalReadinessScore >= 95);
        Assert.Equal(30, result.RequestedMusicLevelPercent);
        Assert.Equal(30, result.EffectiveMusicLevelPercent);
        Assert.Equal(0.30, result.MusicVolumeMultiplier);
        Assert.False(result.DuckMusicUnderNarration);
        Assert.Equal("[2:a]volume=0.30[music];[1:a][music]amix=inputs=2:duration=first:normalize=0[aout]", result.FfmpegAudioFilter);
        Assert.False(result.MusicMixApplied);
        Assert.Empty(result.GeneratedFiles);
        Assert.False(File.Exists(Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-render-validation.json")));
    }

    [Fact]
    public async Task GenerateVideoAssemblyAsync_RenderFailsWhenAssemblyPlanMissing()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Render",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None));

        Assert.Contains("Required render input 'video-assembly-plan.json'", error.Message);
    }

    private static VideoAssemblyIntelligenceService CreateService(string workingDirectory)
        => new(Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }));


    private static async Task WriteAssemblyPhaseInputsAsync(string workingDirectory, VideoAssemblyIntelligenceService service)
    {
        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Intelligence",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Script",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var videoAssemblyRoot = BuildVideoAssemblyRoot(workingDirectory);
        Directory.CreateDirectory(videoAssemblyRoot);
        await File.WriteAllBytesAsync(Path.Combine(videoAssemblyRoot, "video-tts-audio.mp3"), Enumerable.Repeat((byte)1, 2048).ToArray());

        var timings = new VideoTtsTimingsDto(
            EventId,
            RegionId,
            "en",
            "YouTubeShort",
            Path.Combine(videoAssemblyRoot, "video-tts-audio.mp3").Replace('\\', '/'),
            21.456,
            21.456,
            [
                new VideoTtsSceneTimingDto("Hook", 0.000, 3.218, "Don't miss this tonight."),
                new VideoTtsSceneTimingDto("What", 3.218, 7.510, "Venus and Jupiter will shine close together after sunset."),
                new VideoTtsSceneTimingDto("Why", 7.510, 11.801, "Two of the brightest worlds will share the evening sky."),
                new VideoTtsSceneTimingDto("Where", 11.801, 15.019, "Look toward the western sky."),
                new VideoTtsSceneTimingDto("When", 15.019, 18.238, "The best time is shortly after sunset."),
                new VideoTtsSceneTimingDto("Action", 18.238, 21.456, "Step outside tonight and look west.")
            ],
            "AzureSpeechTts",
            "en-US-JennyNeural",
            DateTimeOffset.UtcNow,
            new VideoTtsAudioValidationDto(false, -3.0, -18.0));
        await File.WriteAllTextAsync(Path.Combine(videoAssemblyRoot, "video-tts-timings.json"), JsonSerializer.Serialize(timings, JsonOptions));

        await WriteThumbnailLandscapeAsync(workingDirectory);
    }

    private static async Task WriteThumbnailLandscapeAsync(string workingDirectory)
    {
        var thumbnailRoot = BuildThumbnailAssetsRoot(workingDirectory);
        Directory.CreateDirectory(thumbnailRoot);
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(Path.Combine(thumbnailRoot, "thumbnail-landscape.png"), pngBytes);
    }

    private static async Task WriteRequiredInputsAsync(string workingDirectory)
    {
        var heroAssetsRoot = BuildHeroAssetsRoot(workingDirectory);
        var thumbnailRoot = BuildThumbnailAssetsRoot(workingDirectory);
        Directory.CreateDirectory(heroAssetsRoot);
        Directory.CreateDirectory(thumbnailRoot);

        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-story.json"), JsonSerializer.Serialize(new
        {
            eventId = EventId,
            heroHook = "LOOK WEST TONIGHT",
            summary = "Venus and Jupiter appear close together after sunset."
        }, JsonOptions));

        var heroSceneManifest = new HeroSceneManifestDto(
            EventId,
            new HeroSceneManifestEntryDto(1, "What", BuildApprovedScenePath(workingDirectory, "scene-001"), "PrimaryVisual"),
            new HeroSceneManifestEntryDto(6, "Action", BuildApprovedScenePath(workingDirectory, "scene-006"), "CallToAction"),
            new HeroSceneManifestEntryDto(2, "Where", BuildApprovedScenePath(workingDirectory, "scene-002"), "DirectionCue"),
            "Use What scene as visual anchor, Action scene for CTA, and Where scene for direction cue.");

        var heroCompositionModel = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto("LOOK WEST TONIGHT"),
            new HeroCompositionSceneBlockDto("scene-001"),
            new HeroCompositionTextBlockDto("scene-001", "WEST"),
            new HeroCompositionTextBlockDto("scene-001", "AFTER SUNSET"),
            new HeroCompositionTextBlockDto("scene-001", "LOOK WEST"),
            new HeroCompositionValidationDto(true, true, true, true, true, 100));

        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-scene-manifest.json"), JsonSerializer.Serialize(heroSceneManifest, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-composition-model.json"), JsonSerializer.Serialize(heroCompositionModel, JsonOptions));

        var thumbnailIntelligence = new ThumbnailIntelligenceDto(
            EventId,
            RegionId,
            "en",
            "DON'T MISS THIS TONIGHT",
            ["VENUS AND JUPITER TONIGHT"],
            [new ThumbnailHookScoreDto("DON'T MISS THIS TONIGHT", 100, 95, 91, 87, 94)],
            "Curiosity + Wonder",
            "High",
            "A time-sensitive sky moment that feels easy to miss unless the viewer clicks now.",
            "Large Venus and Jupiter close together above twilight horizon.",
            "Bold emotional astronomy thumbnail with minimal text and twilight contrast.",
            "HeroCompositionModel + PrimaryScene",
            "scene-001",
            [],
            new ThumbnailCopyDto("DON'T MISS THIS TONIGHT", "Venus + Jupiter", "After Sunset"),
            [new ThumbnailPlatformTargetDto("YouTube", "1280x720", "Click")],
            new ThumbnailReadinessScoresDto(100, 95, 91, 87, 94),
            [],
            DateTimeOffset.UtcNow);

        var thumbnailCompositionModel = new ThumbnailCompositionModelDto(
            EventId,
            RegionId,
            "en",
            "DON'T MISS THIS TONIGHT",
            "Venus + Jupiter",
            "After Sunset",
            "Curiosity",
            "High",
            "ScrollStoppingAstronomyThumbnail",
            "Large Venus and Jupiter close together above twilight horizon.",
            new ThumbnailCompositionBlocksDto(
                new ThumbnailCompositionTextBlockDto("DON'T MISS THIS TONIGHT", 1),
                new ThumbnailCompositionVisualBlockDto("HeroCompositionModel + PrimaryScene", 2),
                new ThumbnailCompositionTextBlockDto("Venus + Jupiter", 3),
                new ThumbnailCompositionTextBlockDto("After Sunset", 4)),
            [new ThumbnailCompositionPlatformVariantDto("Landscape", "1280x720", "YouTubeThumbnail")],
            new ThumbnailCompositionValidationDto(true, true, 3, 94),
            DateTimeOffset.UtcNow);

        var thumbnailSceneManifest = new ThumbnailSceneManifestDto(
            EventId,
            new ThumbnailSceneManifestEntryDto(1, "What", BuildApprovedScenePath(workingDirectory, "scene-001"), "PrimaryVisual"),
            new ThumbnailSceneManifestEntryDto(5, "Why", BuildApprovedScenePath(workingDirectory, "scene-005"), "EmotionalSignificance"),
            new ThumbnailSceneManifestEntryDto(6, "Action", BuildApprovedScenePath(workingDirectory, "scene-006"), "UrgencyCue"),
            "Use What scene for visual focus, Why scene for emotional pull, and Action scene for urgency.");

        await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, "thumbnail-intelligence.json"), JsonSerializer.Serialize(thumbnailIntelligence, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, "thumbnail-composition-model.json"), JsonSerializer.Serialize(thumbnailCompositionModel, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, "thumbnail-scene-manifest.json"), JsonSerializer.Serialize(thumbnailSceneManifest, JsonOptions));

        await WriteApprovedSceneOutputsAsync(workingDirectory);
    }

    private static async Task WriteApprovedSceneOutputsAsync(string workingDirectory)
    {
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        foreach (var profileDirectory in new[] { "short", "long" })
        {
            var sceneApprovalRoot = Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine", "scene-approval-v3", profileDirectory);
            Directory.CreateDirectory(sceneApprovalRoot);
            foreach (var sceneId in new[] { "scene-001", "scene-002", "scene-003", "scene-004", "scene-005", "scene-006" })
                await File.WriteAllBytesAsync(Path.Combine(sceneApprovalRoot, $"{sceneId}-final.png"), pngBytes);
        }
    }

    private static string BuildApprovedScenePath(string workingDirectory, string sceneId)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine", "scene-approval-v3", "short", $"{sceneId}-final.png").Replace('\\', '/');

    private static string BuildHeroAssetsRoot(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "hero-assets");

    private static string BuildThumbnailAssetsRoot(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "thumbnail-assets");

    private static string BuildVideoAssemblyRoot(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "video-assembly", "short");

    private static string BuildLongVideoAssemblyRoot(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "video-assembly", "long");

    private static int CountTestWords(string value)
        => Regex.Matches(value, "[\\p{L}\\p{N}]+(?:['’\u2010-\u2015-][\\p{L}\\p{N}]+)?").Count;

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "video-assembly-intelligence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
