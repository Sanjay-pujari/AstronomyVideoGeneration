using Xunit;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class PrePublishValidationServiceTests
{
    [Fact] public async Task Missing_final_video_fails() => Assert.False((await CreateService().ValidateAsync(CreateRequest(videoExists:false), default)).Passed);
    [Fact] public async Task Video_without_audio_fails() => Assert.Contains("Audio stream is missing.", (await CreateService(audio:false).ValidateAsync(CreateRequest(), default)).Errors);
    [Fact] public async Task Placeholder_file_fails() => Assert.Contains("Placeholder visuals were used.", (await CreateService().ValidateAsync(CreateRequest(placeholder:true), default)).Errors);
    [Fact] public async Task Short_sequence_mismatch_fails() => Assert.Contains("short-sequence-map.json is missing for short video.", (await CreateService().ValidateAsync(CreateRequest(isShort:true, createShortMap:false), default)).Errors);
    [Fact] public async Task Valid_long_video_passes() => Assert.True((await CreateService().ValidateAsync(CreateRequest(), default)).Passed);
    [Fact] public async Task Valid_short_video_passes() => Assert.True((await CreateService().ValidateAsync(CreateRequest(isShort:true, createShortMap:true), default)).Passed);
    [Fact] public async Task Publish_false_still_writes_report() { var req = CreateRequest(); await CreateService().ValidateAsync(req, default); Assert.True(File.Exists(Path.Combine(req.OutputDirectory, "pre-publish-validation-report.json"))); }

    private static PrePublishValidationService CreateService(bool audio=true)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var probe = Path.Combine(root, "ffprobe");
        var json = $"{{\"streams\":[{{\"codec_type\":\"video\"}}{(audio?",{\"codec_type\":\"audio\"}":"")}],\"format\":{{\"duration\":\"120\"}}}}";
        File.WriteAllText(probe, $"#!/usr/bin/env bash\necho '{json}'\n");
        System.Diagnostics.Process.Start("chmod", $"+x {probe}")!.WaitForExit();
        return new PrePublishValidationService(Options.Create(new RenderingOptions { FfprobePath = probe }), Options.Create(new PublishingValidationOptions()), NullLogger<PrePublishValidationService>.Instance);
    }

    private static PrePublishValidationRequest CreateRequest(bool videoExists=true,bool placeholder=false,bool isShort=false,bool createShortMap=false)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var video = Path.Combine(dir, "final-video.mp4"); if (videoExists) File.WriteAllText(video, "x");
        File.WriteAllText(Path.Combine(dir, "ffmpeg.log"), "ok");
        var visual = placeholder ? Path.Combine(dir,"scene-001.placeholder.txt") : Path.Combine(dir,"scene-001.png");
        File.WriteAllText(visual, "x");
        if (isShort && createShortMap) File.WriteAllText(Path.Combine(dir, "short-sequence-map.json"), "{\"sceneId\":\"scene-1\"}");
        return new PrePublishValidationRequest{ PipelineRunId = Guid.NewGuid(), ContentType = ContentType.DailySkyGuide, IsShort=isShort, OutputDirectory=dir, FinalVideoPath=video, VisualPaths=[visual], Context=new AstronomyContext{SceneObservationContexts=[new SceneObservationContext{SceneId="scene-1",ObjectName="Moon",UtcObservationTime=DateTime.UtcNow}]}, Script=new ScriptResult{SceneScriptSections=new SceneScriptSections{SectionsBySceneId=new Dictionary<string,string>{{"scene-1","text"}}}}};
    }
}
