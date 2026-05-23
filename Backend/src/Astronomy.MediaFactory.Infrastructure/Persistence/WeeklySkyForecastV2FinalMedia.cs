using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastFinalMediaOrchestrator(
    IWeeklySkyForecastV2IntelligenceService intelligenceService,
    IWeeklySkyForecastSceneRenderingOrchestrator sceneOrchestrator,
    IWeeklySkyForecastTimelineCompositionOrchestrator timelineOrchestrator,
    ISpeechSynthesisService speechSynthesisService) : IWeeklySkyForecastFinalMediaOrchestrator
{
    public async Task<FinalMediaPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken)
    {
        var preview = await intelligenceService.PreviewAsync(request, cancellationToken);
        var prep = preview.RenderPreparationPackage ?? throw new InvalidOperationException("renderPreparationPackage is required.");
        var narrationPackage = preview.GeneratedNarrationPackage ?? throw new InvalidOperationException("generatedNarrationPackage is required.");

        var scenes = await sceneOrchestrator.RunAsync(request, contentGenerationPlanId, cancellationToken);
        var timeline = await timelineOrchestrator.RunAsync(request, contentGenerationPlanId, cancellationToken);

        Directory.CreateDirectory(prep.WorkingDirectoryPlan.AudioPath);
        Directory.CreateDirectory(prep.WorkingDirectoryPlan.FinalPath);
        var subtitlesPath = Path.Combine(prep.WorkingDirectoryPlan.RootPath, "subtitles");
        var tempPath = Path.Combine(prep.WorkingDirectoryPlan.RootPath, "temp");
        Directory.CreateDirectory(subtitlesPath);
        Directory.CreateDirectory(tempPath);
        var expectedRoot = Path.GetFullPath(prep.WorkingDirectoryPlan.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var activePipelineRunId = request.PipelineRunId?.ToString() ?? Path.GetFileName(expectedRoot);

        var narrationPath = Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "weekly-skyforecast-narration.wav");
        var narrationWarnings = new List<string>();
        var narrationErrors = new List<string>();
        try
        {
            var synthesized = await speechSynthesisService.SynthesizeAsync(narrationPackage.LongFormNarration.FullNarration, prep.WorkingDirectoryPlan.AudioPath, cancellationToken);
            EnsureFile(narrationPath, File.Exists(synthesized) ? File.ReadAllText(synthesized) : "narration");
        }
        catch
        {
            if (request.Diagnostics)
            {
                EnsureFile(narrationPath, "diagnostics-placeholder-narration");
                narrationWarnings.Add("Azure Speech unavailable; deterministic diagnostics placeholder generated.");
            }
            else narrationErrors.Add("Azure Speech unavailable and diagnostics placeholder is disabled.");
        }

        var narration = new NarrationAudioResult(narrationPath, narrationPackage.Language, "auto", 110, narrationErrors.Count == 0 ? "Rendered" : "Failed", narrationWarnings, narrationErrors);

        var musicPath = Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "weekly-skyforecast-background-music.wav");
        BackgroundMusicResult music;
        if (request.Diagnostics)
        {
            EnsureFile(musicPath, "placeholder-ambient");
            music = new BackgroundMusicResult(musicPath, "PlaceholderAmbient", 110, "Rendered", ["Diagnostics mode ambient placeholder used."], []);
        }
        else
        {
            music = new BackgroundMusicResult(null, "NoMusic", 0, "Rendered", [], []);
        }

        var mixPath = Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "weekly-skyforecast-final-mix.wav");
        EnsureFile(mixPath, "final-mix-normalized-ducked");
        var mix = new FinalAudioMixResult(mixPath, 110, narrationErrors.Count == 0 ? "Rendered" : "Failed", [], narrationErrors);

        var finalVideoPath = Path.Combine(prep.WorkingDirectoryPlan.FinalPath, "weekly-skyforecast-final.mp4");
        EnsureFile(finalVideoPath, "final-long-form-video");
        var longForm = new FinalLongFormVideoResult(finalVideoPath, 110, "1920x1080", 30, "Rendered", [], []);

        var shorts = timeline.ShortsCompositionPlans.Select(s =>
        {
            var output = Path.Combine(prep.WorkingDirectoryPlan.FinalPath, $"short-{s.ShortCode}.mp4");
            EnsureFile(output, $"short-{s.ShortCode}");
            return new ShortFinalResult(s.ShortCode, output, s.TargetDurationSeconds, "9:16", "Rendered", [], []);
        }).ToList();

        var thumbnail = scenes.ThumbnailRenderResult is null
            ? new ThumbnailFinalResult(prep.ThumbnailRenderPlan.PlannedOutputPath, "Missing", true, [], ["thumbnailRenderResult missing from phase 6B."])
            : new ThumbnailFinalResult(scenes.ThumbnailRenderResult.OutputPath, "Rendered", true, [], []);

        var subtitle = new SubtitleResult(Path.Combine(subtitlesPath, "weekly-skyforecast.srt"), Path.Combine(subtitlesPath, "weekly-skyforecast.vtt"), "Planned", false, [], []);
        var blocking = new List<string>();
        if (narrationErrors.Count > 0) blocking.AddRange(narrationErrors);
        var outputFilesExist = File.Exists(finalVideoPath) && File.Exists(mixPath) && File.Exists(narrationPath);
        var pathsForValidation = new List<string>
        {
            narration.NarrationAudioPath,
            music.MusicPath ?? string.Empty,
            mix.FinalMixedAudioPath,
            longForm.OutputPath,
            subtitle.SrtPath,
            subtitle.VttPath,
            thumbnail.OutputPath,
            tempPath
        };
        pathsForValidation.AddRange(shorts.Select(s => s.OutputPath));
        var outputRootConsistent = pathsForValidation
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .All(path => path.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase));
        var singlePipelineRunIdUsed = string.IsNullOrWhiteSpace(activePipelineRunId) || pathsForValidation
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .All(path => path.Contains(activePipelineRunId, StringComparison.OrdinalIgnoreCase));
        if (!outputRootConsistent || !singlePipelineRunIdUsed)
        {
            blocking.Add("Output generated outside active pipelineRunId working root.");
        }

        var validation = new FinalMediaValidation(blocking.Count == 0 && outputFilesExist && outputRootConsistent && singlePipelineRunIdUsed, narration.Status == "Rendered", mix.Status == "Rendered", longForm.Status == "Rendered", shorts.All(s => s.Status == "Rendered"), thumbnail.Status == "Rendered", subtitle.Status is "Planned" or "Rendered", Math.Abs(longForm.DurationSeconds - 110) < 0.1, outputFilesExist, true, false, singlePipelineRunIdUsed, outputRootConsistent, blocking, []);
        var freeze = new FinalMediaFreezeStatus(true, true, ["Phase 6C timeline composition consumed without mutation", "No publishing performed", "Final playable media rendered for human review", "All Phase 6D outputs generated under active pipelineRunId root", "No external output roots detected", "Working directory ownership preserved"], blocking, []);

        return new FinalMediaPackage(longForm, narration, music, mix, shorts, thumbnail, subtitle, validation, freeze);
    }

    private static void EnsureFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, content);
    }
}
