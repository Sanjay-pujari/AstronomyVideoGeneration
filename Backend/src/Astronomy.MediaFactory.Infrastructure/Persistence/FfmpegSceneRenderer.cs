using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class FfmpegSceneRenderer(
    IOptions<RenderingOptions> renderingOptions,
    IProcessRunner processRunner,
    ILogger<FfmpegSceneRenderer> logger) : ISceneRenderer
{
    private const string RecipeDirectoryName = "render-recipes";
    private const string CapabilityDirectoryName = "render-capabilities";
    private const string RenderedScenesDirectoryName = "rendered-scenes";
    private const string WorkingFramesDirectoryName = "render-working-frames";
    private const string PilotCategory = "RareEventAlert";
    private static readonly Guid PilotPlanId = Guid.Parse("36cb768a-4aa6-4189-ac48-f45ae5ee4f6b");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public async Task<SceneRenderingResponse> RenderScenesAsync(SceneRenderingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("RegionId is required.");
        if (request.PlanIds is null || request.PlanIds.Count == 0) throw new ArgumentException("At least one planId is required. Phase 9E.2B pilot does not render all plans.");
        if (request.MaxPlans is < 1) throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var selectedPlanIds = request.PlanIds.Take(Math.Min(request.MaxPlans ?? 1, 1)).ToArray();
        if (selectedPlanIds.Any(id => id != PilotPlanId))
            throw new ArgumentException($"Phase 9E.2B pilot is isolated to RareEventAlert plan {PilotPlanId:D}.");

        var root = ResolveWorkingDirectoryRoot();
        var warnings = new List<string>();
        var renderedFiles = new List<string>();
        var planItems = new List<SceneRenderingPlanItem>();
        var completed = 0;
        var failed = 0;

        foreach (var planId in selectedPlanIds)
        {
            var planRoot = BuildPlanRoot(root, request.RegionId!, planId.ToString("D"));
            var recipeDirectory = Path.Combine(planRoot, RecipeDirectoryName);
            var capabilityDirectory = Path.Combine(planRoot, CapabilityDirectoryName);
            var outputDirectory = Path.Combine(planRoot, RenderedScenesDirectoryName);
            var frameDirectory = Path.Combine(planRoot, WorkingFramesDirectoryName);
            var recipePaths = Directory.Exists(recipeDirectory)
                ? Directory.EnumerateFiles(recipeDirectory, "scene-*.recipe.json", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(4).ToArray()
                : [];

            if (recipePaths.Length == 0)
            {
                warnings.Add($"No render recipes found for pilot plan {planId:D}: {recipeDirectory}");
                continue;
            }
            if (recipePaths.Length != 4)
                warnings.Add($"Phase 9E.2B pilot expects 4 RareEventAlert scene recipes; found {recipePaths.Length} under {recipeDirectory}.");

            foreach (var recipePath in recipePaths)
            {
                var sceneWarnings = new List<string>();
                var recipe = await ReadJsonAsync<RenderRecipeDocument>(recipePath, cancellationToken);
                if (recipe is null)
                {
                    failed++;
                    warnings.Add($"Could not read render recipe: {recipePath}");
                    continue;
                }

                if (!string.Equals(recipe.ContentCategory, PilotCategory, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Phase 9E.2B pilot only supports {PilotCategory}. Recipe {recipePath} is {recipe.ContentCategory}.");

                var capabilityPath = Path.Combine(capabilityDirectory, $"scene-{recipe.SceneNumber:000}.capability.json");
                var capability = File.Exists(capabilityPath) ? await ReadJsonAsync<RenderCapabilityDocument>(capabilityPath, cancellationToken) : null;
                if (capability is null) sceneWarnings.Add($"Missing render capability matrix for scene {recipe.SceneNumber}: {capabilityPath}");
                else
                {
                    sceneWarnings.AddRange(capability.ExecutionPlan.Warnings);
                    sceneWarnings.AddRange(capability.ExecutionPlan.Fallbacks.Select(x => $"Fallback: {x}"));
                    if (!capability.ExecutionPlan.CanExecute)
                        sceneWarnings.AddRange(capability.ExecutionPlan.BlockingIssues.Select(x => $"Capability blocking issue: {x}"));
                }

                var audioPath = ResolveAudioPath(planRoot, recipe.SceneNumber);
                if (string.IsNullOrWhiteSpace(audioPath)) sceneWarnings.Add($"Missing narration WAV for scene {recipe.SceneNumber:000} under {planRoot}.");

                var outputPath = Path.Combine(outputDirectory, $"scene-{recipe.SceneNumber:000}.mp4");
                var visual = await ResolveVisualAsync(planRoot, frameDirectory, recipePath, recipe, request.DryRun, sceneWarnings, cancellationToken);
                var motionRenderer = ResolveMotionRenderer(recipe.Motion.Type);
                var item = new SceneRenderingPlanItem(
                    recipe.ContentGenerationPlanId,
                    recipe.RegionId,
                    recipe.SceneNumber,
                    recipe.SceneName,
                    recipe.DurationSeconds,
                    recipePath,
                    capabilityPath,
                    audioPath ?? string.Empty,
                    outputPath,
                    visual.SourcePath,
                    visual.RendererName,
                    motionRenderer,
                    !string.IsNullOrWhiteSpace(audioPath),
                    sceneWarnings);
                planItems.Add(item);
                warnings.AddRange(sceneWarnings.Select(w => $"Plan {planId:D} scene {recipe.SceneNumber:000}: {w}"));

                if (request.DryRun) continue;
                if (string.IsNullOrWhiteSpace(audioPath))
                {
                    failed++;
                    continue;
                }

                if (File.Exists(outputPath) && !request.OverwriteExisting)
                {
                    sceneWarnings.Add("Skipped existing rendered scene. Set overwriteExisting=true to replace it.");
                    renderedFiles.Add(outputPath);
                    completed++;
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(outputDirectory);
                    Directory.CreateDirectory(frameDirectory);
                    var subtitlePath = Path.Combine(frameDirectory, $"scene-{recipe.SceneNumber:000}.srt");
                    var narrationText = await ResolveNarrationTextAsync(planRoot, recipePath, recipe, cancellationToken);
                    await File.WriteAllTextAsync(subtitlePath, BuildCaptionSrt(narrationText, recipe.DurationSeconds), cancellationToken);

                    var args = BuildFfmpegArgs(visual.FramePath, audioPath, subtitlePath, outputPath, recipe, request.OverwriteExisting);
                    logger.LogInformation("Rendering Phase 9E.2B pilot scene {SceneNumber} with {FfmpegPath}", recipe.SceneNumber, renderingOptions.Value.FfmpegPath);
                    var ffmpeg = await processRunner.ExecuteAsync(renderingOptions.Value.FfmpegPath, args, cancellationToken, TimeSpan.FromSeconds(Math.Max(120, renderingOptions.Value.FfmpegSegmentTimeoutSeconds)));
                    if (ffmpeg.ExitCode != 0)
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(ffmpeg.StandardError) ? "FFmpeg scene render failed." : ffmpeg.StandardError);

                    var validation = await ValidateOutputAsync(outputPath, recipe.DurationSeconds, cancellationToken);
                    if (!validation.IsValid)
                        throw new InvalidOperationException(string.Join("; ", validation.Warnings));

                    completed++;
                    renderedFiles.Add(outputPath);
                }
                catch (Exception ex)
                {
                    failed++;
                    var message = $"Render failed for scene {recipe.SceneNumber:000}: {ex.Message}";
                    warnings.Add(message);
                    sceneWarnings.Add(message);
                }
            }

            if (!request.DryRun)
                await WriteManifestAsync(outputDirectory, planId.ToString("D"), planItems.Where(x => string.Equals(x.ContentGenerationPlanId, planId.ToString("D"), StringComparison.OrdinalIgnoreCase)).ToArray(), completed, failed, cancellationToken);
        }

        return new SceneRenderingResponse(selectedPlanIds.Length, planItems.Count, completed, failed, renderedFiles, warnings, planItems);
    }

    private async Task<ResolvedVisual> ResolveVisualAsync(string planRoot, string frameDirectory, string recipePath, RenderRecipeDocument recipe, bool dryRun, List<string> warnings, CancellationToken cancellationToken)
    {
        var imageInput = recipe.Inputs.Select(x => ResolveAssetPath(planRoot, x.AssetPath)).FirstOrDefault(IsUsableImage);
        if (!string.IsNullOrWhiteSpace(imageInput)) return new ResolvedVisual(imageInput, imageInput, "PlannedVisual");

        var cardInput = recipe.Inputs.FirstOrDefault(x => IsJsonPath(x.AssetPath) || IsCardMode(x.RenderMode) || IsCardType(x.AssetType));
        if (cardInput is not null)
        {
            var cardPath = ResolveAssetPath(planRoot, cardInput.AssetPath);
            var framePath = Path.Combine(frameDirectory, $"scene-{recipe.SceneNumber:000}-card.png");
            if (!dryRun)
            {
                Directory.CreateDirectory(frameDirectory);
                await RenderCardFrameAsync(framePath, recipe, recipePath, File.Exists(cardPath) ? cardPath : null, false, cancellationToken);
            }
            return new ResolvedVisual(framePath, cardPath, ResolveCardRenderer(cardInput));
        }

        var placeholderPath = Path.Combine(frameDirectory, $"scene-{recipe.SceneNumber:000}-placeholder.png");
        if (!dryRun)
        {
            Directory.CreateDirectory(frameDirectory);
            await RenderCardFrameAsync(placeholderPath, recipe, recipePath, null, true, cancellationToken);
        }
        warnings.Add("Generated cinematic placeholder visual because no completed generated image was found.");
        return new ResolvedVisual(placeholderPath, placeholderPath, "PlaceholderVisualRenderer");
    }

    private async Task RenderCardFrameAsync(string outputPath, RenderRecipeDocument recipe, string recipePath, string? sourceJsonPath, bool placeholder, CancellationToken cancellationToken)
    {
        var (title, subtitle, objects, eventTitle, category) = await ExtractCardTextAsync(recipePath, sourceJsonPath, recipe, cancellationToken);
        if (placeholder) category = string.IsNullOrWhiteSpace(category) ? "Planned visual" : category;
        using var image = new Image<Rgba32>(1920, 1080, Color.ParseHex("#060815"));
        var titleFont = ResolveFont(74, FontStyle.Bold);
        var subtitleFont = ResolveFont(38, FontStyle.Regular);
        var labelFont = ResolveFont(28, FontStyle.Regular);
        image.Mutate(ctx =>
        {
            DrawCinematicBackground(ctx, 1920, 1080);
            DrawStarField(ctx, 1920, 1080, recipe.SceneNumber);
            ctx.Fill(Color.ParseHex("#000000").WithAlpha(0.30f), new RectangleF(0, 0, 1920, 1080));
            ctx.DrawLine(Color.ParseHex("#F4B35F").WithAlpha(0.70f), 4, new PointF(120, 138), new PointF(620, 138));
            ctx.DrawText(new RichTextOptions(titleFont) { Origin = new PointF(120, 170), WrappingLength = 1120 }, title, Color.White);
            ctx.DrawText(new RichTextOptions(subtitleFont) { Origin = new PointF(124, 360), WrappingLength = 1040 }, subtitle, Color.ParseHex("#CBE3FF"));
            var panel = new RectangleF(122, 610, 1260, 280);
            ctx.Fill(Color.Black.WithAlpha(0.48f), panel);
            ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(0.34f), 2, panel);
            ctx.DrawText(new RichTextOptions(labelFont) { Origin = new PointF(158, 642), WrappingLength = 1180 }, $"Visual: {category}", Color.ParseHex("#F9B24E"));
            ctx.DrawText(new RichTextOptions(labelFont) { Origin = new PointF(158, 700), WrappingLength = 1180 }, $"Objects: {objects}", Color.White.WithAlpha(0.94f));
            ctx.DrawText(new RichTextOptions(labelFont) { Origin = new PointF(158, 758), WrappingLength = 1180 }, $"Event: {eventTitle}", Color.ParseHex("#D5F3FF"));
            ctx.DrawText(new RichTextOptions(ResolveFont(22)) { Origin = new PointF(128, 980), WrappingLength = 1600 }, "Astronomy Media Factory • Rare Event Alert", Color.White.WithAlpha(0.52f));
            DrawVignette(ctx, 1920, 1080);
        });
        await image.SaveAsPngAsync(outputPath, new PngEncoder(), cancellationToken);
    }

    private static string BuildFfmpegArgs(string imagePath, string audioPath, string subtitlePath, string outputPath, RenderRecipeDocument recipe, bool overwrite)
    {
        var duration = recipe.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var vf = BuildVideoFilter(recipe, subtitlePath);
        var overwriteFlag = overwrite ? "-y" : "-n";
        return $"{overwriteFlag} -loop 1 -i \"{EscapePath(imagePath)}\" -i \"{EscapePath(audioPath)}\" -t {duration} -vf \"{vf}\" -af apad -c:v libx264 -preset veryfast -crf 21 -pix_fmt yuv420p -r 30 -c:a aac -b:a 192k -movflags +faststart -shortest \"{EscapePath(outputPath)}\"";
    }

    private static string BuildVideoFilter(RenderRecipeDocument recipe, string subtitlePath)
    {
        var width = recipe.Resolution.Width <= 0 ? 1920 : recipe.Resolution.Width;
        var height = recipe.Resolution.Height <= 0 ? 1080 : recipe.Resolution.Height;
        var duration = Math.Max(0.1, recipe.DurationSeconds);
        var frames = Math.Max(1, (int)Math.Round(duration * 30));
        var zoomEnd = Math.Clamp(recipe.Motion.EndScale <= 0 ? 1.06 : recipe.Motion.EndScale, 1.0, 1.18);
        var zoomStep = (zoomEnd - 1.0) / frames;
        var motion = recipe.Motion.Type.ToLowerInvariant();
        var baseFilter = motion switch
        {
            "statichold" or "static_hold" => $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}",
            "fadehold" or "fade_hold" => $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},fade=t=in:st=0:d=0.5,fade=t=out:st={Math.Max(0, duration - 0.5).ToString("0.###", CultureInfo.InvariantCulture)}:d=0.5",
            "panhold" or "pan_hold" or "groupedobjectpan" or "grouped_object_pan" => $"scale={width + 160}:{height + 90}:force_original_aspect_ratio=increase,crop={width}:{height}:x='(iw-ow)*t/{duration.ToString("0.###", CultureInfo.InvariantCulture)}':y='(ih-oh)/2'",
            _ => $"scale={width * 2}:{height * 2}:force_original_aspect_ratio=increase,crop={width * 2}:{height * 2},zoompan=z='min(zoom+{zoomStep.ToString("0.########", CultureInfo.InvariantCulture)},{zoomEnd.ToString("0.###", CultureInfo.InvariantCulture)})':d={frames}:s={width}x{height}:fps=30"
        };
        var escapedSubtitlePath = subtitlePath.Replace("\\", "/").Replace("'", "'\\''").Replace(":", "\\:");
        return $"{baseFilter},subtitles='{escapedSubtitlePath}':force_style='FontName=Arial,FontSize=28,PrimaryColour=&H00FFFFFF,BackColour=&H99000000,BorderStyle=4,Outline=1,Shadow=0,MarginV=64,Alignment=2'";
    }

    private async Task<OutputValidation> ValidateOutputAsync(string outputPath, double recipeDurationSeconds, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        if (!File.Exists(outputPath)) warnings.Add($"Video file was not created: {outputPath}");
        else if (new FileInfo(outputPath).Length <= 100_000) warnings.Add($"Video file is too small: {new FileInfo(outputPath).Length} bytes.");

        var ffprobePath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfprobePath) ? "ffprobe" : renderingOptions.Value.FfprobePath!;
        var duration = await ProbeDoubleAsync(ffprobePath, $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{EscapePath(outputPath)}\"", cancellationToken);
        if (Math.Abs(duration - recipeDurationSeconds) > 0.5) warnings.Add($"Rendered duration {duration:0.###}s differs from recipe duration {recipeDurationSeconds:0.###}s by more than 0.5s.");
        var streams = await processRunner.ExecuteAsync(ffprobePath, $"-v error -select_streams v:0 -show_entries stream=codec_type -of csv=p=0 \"{EscapePath(outputPath)}\"", cancellationToken, TimeSpan.FromSeconds(30));
        var hasVideo = streams.ExitCode == 0 && streams.StandardOutput.Contains("video", StringComparison.OrdinalIgnoreCase);
        if (!hasVideo) warnings.Add("Video stream missing.");
        var audio = await processRunner.ExecuteAsync(ffprobePath, $"-v error -select_streams a:0 -show_entries stream=codec_type -of csv=p=0 \"{EscapePath(outputPath)}\"", cancellationToken, TimeSpan.FromSeconds(30));
        var hasAudio = audio.ExitCode == 0 && audio.StandardOutput.Contains("audio", StringComparison.OrdinalIgnoreCase);
        if (!hasAudio) warnings.Add("Audio stream missing.");
        return new OutputValidation(warnings.Count == 0, duration, hasVideo, hasAudio, new FileInfo(outputPath).Length, warnings);
    }

    private async Task<double> ProbeDoubleAsync(string fileName, string args, CancellationToken cancellationToken)
    {
        var result = await processRunner.ExecuteAsync(fileName, args, cancellationToken, TimeSpan.FromSeconds(30));
        return result.ExitCode == 0 && double.TryParse(result.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private async Task WriteManifestAsync(string outputDirectory, string planId, IReadOnlyList<SceneRenderingPlanItem> items, int completed, int failed, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputs = new List<SceneRenderManifestOutput>();
        foreach (var item in items)
        {
            var validation = File.Exists(item.OutputVideoPath) ? await ValidateOutputAsync(item.OutputVideoPath, item.DurationSeconds, cancellationToken) : new OutputValidation(false, 0, false, false, 0, ["Output missing."]);
            outputs.Add(new SceneRenderManifestOutput(item.SceneNumber, item.SceneName, item.OutputVideoPath, item.AudioPath, item.VisualSourcePath, item.DurationSeconds, validation.DurationSeconds, validation.FileSizeBytes, validation.HasAudioStream, validation.HasVideoStream, validation.IsValid ? "Completed" : "Failed", item.Warnings.Concat(validation.Warnings).ToArray()));
        }
        var manifest = new SceneRenderManifest(planId, items.Count, completed, failed, outputs, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "render-manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
    }

    private async Task<string> ResolveNarrationTextAsync(string planRoot, string recipePath, RenderRecipeDocument recipe, CancellationToken cancellationToken)
    {
        var recipeText = await File.ReadAllTextAsync(recipePath, cancellationToken);
        using var recipeDoc = JsonDocument.Parse(recipeText);
        var direct = FindStringByNames(recipeDoc.RootElement, ["narrationText", "narration", "captionText", "scriptText", "voiceoverText"]);
        if (!string.IsNullOrWhiteSpace(direct)) return direct!;

        foreach (var path in Directory.Exists(planRoot) ? Directory.EnumerateFiles(planRoot, "*.json", SearchOption.AllDirectories) : [])
        {
            if (path.Contains(RecipeDirectoryName, StringComparison.OrdinalIgnoreCase) || path.Contains(CapabilityDirectoryName, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                var text = FindSceneString(doc.RootElement, recipe.SceneNumber, ["narrationText", "finalNarration", "polishedNarration", "scriptText", "text"]);
                if (!string.IsNullOrWhiteSpace(text)) return text!;
            }
            catch { }
        }
        return recipe.SceneName;
    }

    private static async Task<(string Title, string Subtitle, string Objects, string EventTitle, string Category)> ExtractCardTextAsync(string recipePath, string? sourceJsonPath, RenderRecipeDocument recipe, CancellationToken cancellationToken)
    {
        var title = recipe.SceneName;
        var subtitle = recipe.ContentCategory;
        var objects = string.Join(", ", recipe.Inputs.SelectMany(i => Tokenize(Path.GetFileNameWithoutExtension(i.AssetPath))).Distinct(StringComparer.OrdinalIgnoreCase).Take(8));
        var eventTitle = recipe.SceneName;
        var category = recipe.Inputs.FirstOrDefault()?.AssetType ?? "Astronomy visual";
        foreach (var path in new[] { sourceJsonPath, recipePath }.Where(File.Exists))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path!, cancellationToken));
            title = FindStringByNames(doc.RootElement, ["title", "headline", "sceneName"]) ?? title;
            subtitle = FindStringByNames(doc.RootElement, ["subtitle", "summary", "description"]) ?? subtitle;
            eventTitle = FindStringByNames(doc.RootElement, ["eventTitle", "eventName", "title"]) ?? eventTitle;
            var foundObjects = FindStringArrayByNames(doc.RootElement, ["objectNames", "objects", "targets", "celestialObjects"]);
            if (foundObjects.Count > 0) objects = string.Join(", ", foundObjects.Take(8));
            category = FindStringByNames(doc.RootElement, ["visualCategory", "category", "assetType", "cardType"]) ?? category;
        }
        return (Trim(title, 90), Trim(subtitle, 180), string.IsNullOrWhiteSpace(objects) ? "Moon, planets, stars" : Trim(objects, 160), Trim(eventTitle, 120), Trim(category, 80));
    }

    private static string? ResolveAudioPath(string planRoot, int sceneNumber)
    {
        if (!Directory.Exists(planRoot)) return null;
        var candidates = new[] { $"scene-{sceneNumber:00}.wav", $"scene-{sceneNumber:000}.wav", $"scene-{sceneNumber}.wav", $"scene-{sceneNumber:00}-*.wav", $"scene-{sceneNumber:000}-*.wav" };
        foreach (var pattern in candidates)
        {
            var match = Directory.EnumerateFiles(planRoot, pattern, SearchOption.AllDirectories).OrderBy(x => x.Length).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(match)) return match;
        }
        return null;
    }

    private static string ResolveAssetPath(string planRoot, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return string.Empty;
        if (Path.IsPathRooted(assetPath)) return assetPath;
        var normalized = assetPath.Replace('/', Path.DirectorySeparatorChar);
        var rooted = Path.Combine(planRoot, normalized);
        return File.Exists(rooted) ? rooted : assetPath;
    }

    private static bool IsUsableImage(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static bool IsJsonPath(string? path) => !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);
    private static bool IsCardMode(string? mode) => !string.IsNullOrWhiteSpace(mode) && mode.Contains("card", StringComparison.OrdinalIgnoreCase);
    private static bool IsCardType(string? assetType) => !string.IsNullOrWhiteSpace(assetType) && (assetType.Contains("card", StringComparison.OrdinalIgnoreCase) || assetType.Contains("overlay", StringComparison.OrdinalIgnoreCase) || assetType.Contains("nasa", StringComparison.OrdinalIgnoreCase));
    private static string ResolveCardRenderer(RenderRecipeInput input) => input.AssetType.ToLowerInvariant() switch
    {
        var x when x.Contains("sky") => "SkyMapCardRenderer",
        var x when x.Contains("constellation") => "ConstellationGuideRenderer",
        var x when x.Contains("nasa") => "NasaAssetCardRenderer",
        _ => "TextOverlayCardRenderer"
    };

    private static string ResolveMotionRenderer(string motionType) => motionType.ToLowerInvariant() switch
    {
        "kenburns" or "ken_burns" => "KenBurnsMotionRenderer",
        "panhold" or "pan_hold" => "PanHoldMotionRenderer",
        "parallaxsoft" or "parallax_soft" => "ParallaxSoftMotionRenderer",
        "groupedobjectpan" or "grouped_object_pan" => "GroupedObjectPanRenderer",
        "weeklymontage" or "weekly_montage" => "WeeklyMontageRenderer",
        "statichold" or "static_hold" => "StaticHoldMotionRenderer",
        "fadehold" or "fade_hold" => "FadeHoldMotionRenderer",
        _ => "KenBurnsMotionRenderer"
    };

    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string BuildPlanRoot(string root, string regionId, string planId) => Path.Combine(root, "assets", regionId, "plans", planId);
    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct) where T : class => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), JsonOptions);
    private static string EscapePath(string path) => path.Replace("\\", "/");

    private static string BuildCaptionSrt(string text, double durationSeconds)
    {
        var clean = Regex.Replace(text.Trim(), "\\s+", " ");
        var chunks = SplitCaption(clean, 78).Take(6).ToArray();
        var cueDuration = durationSeconds / Math.Max(1, chunks.Length);
        var sb = new StringBuilder();
        for (var i = 0; i < chunks.Length; i++)
        {
            sb.AppendLine((i + 1).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine($"{FormatSrt(cueDuration * i)} --> {FormatSrt(Math.Min(durationSeconds, cueDuration * (i + 1)))}");
            sb.AppendLine(chunks[i]);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static IEnumerable<string> SplitCaption(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > maxLength)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    private static string FormatSrt(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
    }

    private static Font ResolveFont(float size, FontStyle style = FontStyle.Regular)
    {
        var families = SystemFonts.Collection.Families;
        var family = families.FirstOrDefault(f => f.Name.Contains("DejaVu Sans", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(family.Name))
        {
            family = families.FirstOrDefault(f => f.Name.Contains("Arial", StringComparison.OrdinalIgnoreCase));
        }
        if (string.IsNullOrWhiteSpace(family.Name))
        {
            family = families.FirstOrDefault();
        }
        if (string.IsNullOrWhiteSpace(family.Name))
        {
            throw new InvalidOperationException("No system fonts available for C# overlay rendering.");
        }

        return family.CreateFont(size, style);
    }

    private static void DrawCinematicBackground(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(Color.ParseHex("#071126"), new RectangleF(0, 0, width, height));
        ctx.Fill(Color.ParseHex("#1A2E5B").WithAlpha(0.42f), new EllipsePolygon(width * 0.72f, height * 0.30f, width * 0.46f));
        ctx.Fill(Color.ParseHex("#5B2A86").WithAlpha(0.22f), new EllipsePolygon(width * 0.30f, height * 0.28f, width * 0.34f));
        ctx.Fill(Color.ParseHex("#F2B35F").WithAlpha(0.09f), new EllipsePolygon(width * 0.80f, height * 0.78f, width * 0.40f));
    }

    private static void DrawStarField(IImageProcessingContext ctx, int width, int height, int seed)
    {
        var random = new Random(seed * 7919);
        for (var i = 0; i < 220; i++)
        {
            var x = random.NextSingle() * width;
            var y = random.NextSingle() * height;
            var r = 0.8f + random.NextSingle() * 2.2f;
            ctx.Fill(Color.White.WithAlpha(0.22f + random.NextSingle() * 0.58f), new EllipsePolygon(x, y, r));
        }
    }

    private static void DrawVignette(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Draw(Color.Black.WithAlpha(0.38f), 80, new RectangleF(-34, -34, width + 68, height + 68));
    }

    private static string? FindSceneString(JsonElement element, int sceneNumber, IReadOnlyList<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var elementScene = FindIntByNames(element, ["sceneNumber", "scene", "sceneIndex"]);
            if (elementScene == sceneNumber || elementScene == sceneNumber - 1)
            {
                var value = FindStringByNames(element, names);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            foreach (var property in element.EnumerateObject())
            {
                var found = FindSceneString(property.Value, sceneNumber, names);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindSceneString(item, sceneNumber, names);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }
        return null;
    }

    private static string? FindStringByNames(JsonElement element, IReadOnlyList<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
            }
            foreach (var property in element.EnumerateObject())
            {
                var found = FindStringByNames(property.Value, names);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindStringByNames(item, names);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }
        return null;
    }

    private static List<string> FindStringArrayByNames(JsonElement element, IReadOnlyList<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                if (property.Value.ValueKind == JsonValueKind.Array)
                    return property.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (property.Value.ValueKind == JsonValueKind.String) return [property.Value.GetString()!];
            }
            foreach (var property in element.EnumerateObject())
            {
                var found = FindStringArrayByNames(property.Value, names);
                if (found.Count > 0) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindStringArrayByNames(item, names);
                if (found.Count > 0) return found;
            }
        }
        return [];
    }

    private static int? FindIntByNames(JsonElement element, IReadOnlyList<string> names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number)) return number;
            if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        }
        return null;
    }

    private static IEnumerable<string> Tokenize(string value) => Regex.Split(value ?? string.Empty, "[-_\\s]+")
        .Where(x => x.Length > 2 && !x.StartsWith("scene", StringComparison.OrdinalIgnoreCase));
    private static string Trim(string value, int max) => string.IsNullOrWhiteSpace(value) || value.Length <= max ? value : value[..max].TrimEnd() + "…";

    private sealed record ResolvedVisual(string FramePath, string SourcePath, string RendererName);
    private sealed record OutputValidation(bool IsValid, double DurationSeconds, bool HasVideoStream, bool HasAudioStream, long FileSizeBytes, IReadOnlyList<string> Warnings);
}
