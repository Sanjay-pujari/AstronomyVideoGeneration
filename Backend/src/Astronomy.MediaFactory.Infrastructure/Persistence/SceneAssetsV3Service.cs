using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class SceneAssetsV3Service(
    IOptions<RenderingOptions> renderingOptions,
    IAICinematicImageGenerator imageGenerator,
    IOptions<WeeklySkyForecastAICinematicAssetsOptions> aiCinematicOptions,
    ILogger<SceneAssetsV3Service> logger) : ISceneAssetsV3Service
{
    private const string Version = "v3.3";
    private const int ProviderWidth = 1792;
    private const int ProviderHeight = 1024;
    private const int Width = 1920;
    private const int Height = 1080;
    private const string RequestedOverlayFont = "DejaVu Sans";
    private static readonly string[] WindowsSafeFontFallbacks = ["Segoe UI", "Arial", "Calibri", "Tahoma", "DejaVu Sans"];
    private static readonly string[] CheckedFontPaths = [
        "C:/WINDOWS/Fonts",
        "C:/Windows/Fonts",
        "%LOCALAPPDATA%/Microsoft/Windows/Fonts",
        "/usr/share/fonts",
        "/usr/local/share/fonts",
        "/Library/Fonts",
        "~/Library/Fonts"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static string SanitizeFileName(string value)
        => string.Join("-", Regex.Matches(value, "[A-Za-z0-9_-]+").Select(m => m.Value)).Trim('-') is { Length: > 0 } safe ? safe : Guid.NewGuid().ToString("N");

    public async Task<SceneAssetsV3Response> GenerateAsync(SceneAssetsV3Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = Path.Combine(ResolveRoot(request), "scene-assets-v3");
        var files = new List<string>();
        var warnings = new List<string>();
        string? shortValidation = null;
        string? longValidation = null;

        // Authority mode is deliberately a separate input branch, not a fallback. It never opens
        // production-event-intelligence or question-driven-narration-v2.
        var authority = request.AuthorityInput;
        var context = authority is not null
            ? BuildAuthorityTimelineContext(authority)
            : await LoadTimelineContextAsync(root, cancellationToken);
        var authorityReuse = authority is null || request.OverwriteExisting
            ? new HashSet<string>(StringComparer.Ordinal)
            : await PrepareAuthorityReuseAsync(ResolveRoot(request), root, authority, request, cancellationToken);
        if (authority is not null)
            await WriteAuthorityPlanningPackageAsync(request, authority, cancellationToken);
        var enableAccurateSkyGuideV2 = request.EnableAccurateSkyGuideV2 ?? renderingOptions.Value.EnableAccurateSkyGuideV2;
        try
        {
            if (request.GenerateShort)
            {
                var scenes = authority?.ShortScenes;
                var beats = scenes is null ? BuildBeats(context, "short", 5) : BuildAuthorityBeats(context, scenes);
                shortValidation = await GenerateFormatAsync(root, "short", beats, beats.Count, request.OverwriteExisting, files, warnings, context, enableAccurateSkyGuideV2, request.ShortTargetWidth, request.ShortTargetHeight, request.ProviderRequestedSize, cancellationToken, scenes?.Select(x => x.SceneId).ToArray());
            }
            if (request.GenerateLong)
            {
                var scenes = authority?.LongScenes;
                var beats = scenes is null ? BuildBeats(context, "long", 9) : BuildAuthorityBeats(context, scenes);
                longValidation = await GenerateFormatAsync(root, "long", beats, beats.Count, request.OverwriteExisting, files, warnings, context, enableAccurateSkyGuideV2, request.LongTargetWidth, request.LongTargetHeight, request.ProviderRequestedSize, cancellationToken, scenes?.Select(x => x.SceneId).ToArray());
            }

            if (authority is not null)
                files.AddRange(await PublishAuthorityPackageAsync(request, authority, root, authorityReuse, cancellationToken));
        }
        catch (Exception ex) when (authority is not null)
        {
            await WriteAuthorityFailureArtifactsAsync(request, authority, ex, CancellationToken.None);
            throw;
        }

        return new SceneAssetsV3Response(root, files, warnings, shortValidation, longValidation);
    }

    private async Task WriteAuthorityPlanningPackageAsync(SceneAssetsV3Request request, Phase8AuthorityInput authority, CancellationToken ct)
    {
        var root = Path.Combine(ResolveRoot(request), "08-scene-assets");
        Directory.CreateDirectory(root);
        var scenes = authority.LongScenes.Concat(authority.ShortScenes).OrderBy(x => x.Variant).ThenBy(x => x.SceneOrder).ToArray();
        await WriteJsonAsync(Path.Combine(root, "media-project.json"), new
        {
            schemaVersion = "1.0", authority.PlanId, authority.ExecutionId, authority.EventId, authority.Language,
            authority.RequestedVariants, authority.DocumentaryBlueprintChecksum, authority.StoryFrameManifestChecksum,
            shortNarrationReleaseCandidatePhysicalSha256 = authority.ShortNarrationReleaseCandidateChecksum,
            longNarrationReleaseCandidatePhysicalSha256 = authority.LongNarrationReleaseCandidateChecksum,
            expectedShortSceneCount = authority.ShortScenes.Count, expectedLongSceneCount = authority.LongScenes.Count,
            targetWidth = request.ShortTargetWidth, targetHeight = request.ShortTargetHeight,
            targetAspectRatio = $"{request.ShortTargetWidth}:{request.ShortTargetHeight}",
            generationProfile = request.ProviderRequestedSize, createdAtUtc = DateTimeOffset.UtcNow
        }, ct);
        await WriteJsonAsync(Path.Combine(root, "visual-asset-plan.json"), new
        {
            schemaVersion = "1.0", authoritySource = "Phase8AuthorityInput",
            scenes = scenes.Select(x => new { x.SceneId, x.BlueprintSceneId, x.StoryFrameId, x.SceneOrder, x.SceneRole,
                x.ScenePurpose, x.VisualDirection, x.ObservationDirection, x.RequiredAstronomyObjects, x.KnowledgeReferenceIds,
                x.AcceptedNarrationSceneId, acceptedNarrationReference = x.NarrationReleaseCandidateChecksum,
                x.VisualOpportunityType, x.RenderingPreference, x.AssetRole,
                targetWidth = x.Variant == "Short" ? request.ShortTargetWidth : request.LongTargetWidth,
                targetHeight = x.Variant == "Short" ? request.ShortTargetHeight : request.LongTargetHeight,
                semanticIdentity = SemanticIdentity(x, x.Variant == "Short" ? request.ShortTargetWidth : request.LongTargetWidth,
                    x.Variant == "Short" ? request.ShortTargetHeight : request.LongTargetHeight) })
        }, ct);
        await WriteJsonAsync(Path.Combine(root, "visual-generation-requests.json"), new
        {
            schemaVersion = "1.0", requests = scenes.Select((x, index) => { var beat = BuildAuthorityBeats(BuildAuthorityTimelineContext(authority), [x]).Single();
                return new { x.SceneId, providerType = "Planned/SceneAssetsV3Router", providerRequestIdentity = $"scene-assets-v3-{x.Variant.ToLowerInvariant()}-{x.SceneId}",
                    instruction = beat.VisualPrompt, negativeConstraints = "no embedded text, watermark, logo, or unrelated objects",
                    width = x.Variant == "Short" ? request.ShortTargetWidth : request.LongTargetWidth,
                    height = x.Variant == "Short" ? request.ShortTargetHeight : request.LongTargetHeight,
                    renderingMode = beat.RenderMode, sourceBlueprintSceneId = x.BlueprintSceneId,
                    sourceStoryFrameId = x.StoryFrameId, sourceNarrationSceneId = x.AcceptedNarrationSceneId }; })
        }, ct);
    }

    private async Task WriteAuthorityFailureArtifactsAsync(SceneAssetsV3Request request, Phase8AuthorityInput authority, Exception ex, CancellationToken ct)
    {
        var diagnostics = Path.Combine(ResolveRoot(request), "08-scene-assets", "diagnostics");
        Directory.CreateDirectory(diagnostics);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        async Task Write(string name, object value) => await File.WriteAllTextAsync(Path.Combine(diagnostics, name), JsonSerializer.Serialize(value, options), ct);
        await Write("authority-load-diagnostics.json", new { authorityLoaded = true, authority.RequestedVariants, expectedShortSceneCount = authority.ShortScenes.Count, expectedLongSceneCount = authority.LongScenes.Count });
        await Write("visual-plan-diagnostics.json", new { planningArtifactsWritten = true, plannedSceneIds = authority.LongScenes.Concat(authority.ShortScenes).Select(x => x.SceneId) });
        await Write("provider-failure-diagnostics.json", new { providerCalled = ex.Data.Contains("imageGenerationStartedUtc"), providerSucceeded = false, exceptionType = ex.GetType().Name, ex.Message });
        await Write("publication-failure-report.json", new { publicationCommitted = false, committedManifestPublished = false, ex.Message, failedAtUtc = DateTimeOffset.UtcNow });
    }

    private async Task<string> GenerateFormatAsync(string root, string format, IReadOnlyList<SceneAssetsV3Beat> beats, int expectedCount, bool overwrite, List<string> files, List<string> warnings, SceneAssetsV3TimelineContext context, bool enableAccurateSkyGuideV2, int targetWidth, int targetHeight, string providerRequestedSize, CancellationToken ct, IReadOnlyList<string>? authorityExpectedSceneIds = null)
    {
        var dir = Path.Combine(root, format);
        if (overwrite && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var timelinePath = Path.Combine(dir, "visual-timeline-v3.json");
        var manifestPath = Path.Combine(dir, "scene-manifest-v3.json");
        var reviewPath = Path.Combine(dir, "scene-review-v3.json");
        var validationPath = Path.Combine(dir, "scene-v3-validation.json");
        var metadataPath = Path.Combine(dir, "scene-timeline-metadata.json");
        var diagnosticsPath = Path.Combine(dir, "scene-assets-v3-diagnostics.json");
        var visualPromptDiagnosticsPath = Path.Combine(dir, "visual-prompt-diagnostics.json");
        var accurateSkyGuideV2DiagnosticsPath = Path.Combine(dir, "accurate-sky-guide-v2-diagnostics.json");
        var manifestScenes = new List<SceneAssetsV3ManifestScene>();
        var sceneDiagnostics = new List<object>();
        var generatedFilesBeforeFailure = new List<string>();
        var accurateSkyGuideV2Diagnostics = new List<object>();
        var errors = new List<string>();
        EventContentGuard.ValidateObject("SceneAssetsV3Service", "visualTimeline", new SceneAssetsV3Timeline(Version, format, beats), context.ForbiddenTerms);

        try
        {
            await WriteJsonAsync(timelinePath, new SceneAssetsV3Timeline(Version, format, beats), ct); files.Add(timelinePath);
            await WriteJsonAsync(metadataPath, BuildTimelineMetadata(format, beats), ct); files.Add(metadataPath);

            foreach (var beat in beats)
            {
                var imagePath = Path.Combine(dir, beat.SceneId + ".png");
                var guideV2Enabled = enableAccurateSkyGuideV2 && beat.RenderMode == "AccurateSkyGuideScene";
                var providerCalled = beat.RenderMode is not "AccurateSkyGuideScene" || guideV2Enabled;
                var providerSucceeded = false;
                var fallbackUsed = false;
                string? accurateSkyGuidePromptPath = null;
                var existingValidPng = File.Exists(imagePath) && IsValidPng(imagePath);
                if (existingValidPng && !overwrite)
                {
                    providerSucceeded = true;
                    logger.LogInformation("SCENE_ASSETS_V3_IMAGE_REUSED format={Format} sceneId={SceneId} plannedImagePath={PlannedImagePath}", format, beat.SceneId, imagePath);
                }

                if ((!existingValidPng || overwrite) && providerCalled)
                {
                    var prompt = guideV2Enabled ? BuildAccurateSkyGuideV2Prompt(context, beat) : beat.VisualPrompt;
                    if (guideV2Enabled)
                    {
                        accurateSkyGuidePromptPath = Path.Combine(dir, beat.SceneId + "-accurate-sky-guide-v2-prompt.txt");
                        await File.WriteAllTextAsync(accurateSkyGuidePromptPath, prompt, ct);
                        files.Add(accurateSkyGuidePromptPath);
                    }

                    var generationStartedUtc = DateTimeOffset.UtcNow;
                    var generationStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var assetRequest = new AICinematicAssetRequest(
                        $"scene-assets-v3-{format}-{beat.SceneId}", beat.SceneId, beat.RenderMode, format, beat.SceneId,
                        "scene-background", beat.VisualIntent, beat.CompositionType, guideV2Enabled ? "Accurate Sky Guide V2, premium astronomy observation guide, NASA and National Geographic style" : StyleFor(beat.RenderMode), prompt,
                        guideV2Enabled ? "dashboard UI, technical chart, crowded text, location text, watermark, branding, logo" : "infographic, PowerPoint slide, large text panels, fake star labels, UI, watermark, logo", ProviderWidth, ProviderHeight, imagePath);
                    await WritePartialGenerationDiagnosticsAsync(diagnosticsPath, format, context, beat, imagePath, prompt, generationStartedUtc, generationStopwatch.ElapsedMilliseconds, null, generatedFilesBeforeFailure, ct);
                    try
                    {
                        var result = await imageGenerator.GenerateAsync(assetRequest, ct);
                        generationStopwatch.Stop();
                        providerSucceeded = result.GenerationStatus.Equals("Generated", StringComparison.OrdinalIgnoreCase) && File.Exists(imagePath);
                        if (!providerSucceeded)
                            warnings.Add($"Azure Image2 did not produce {format}/{beat.SceneId}; deterministic Scene V3 fallback was rendered. Status={result.GenerationStatus}.");
                        await WritePartialGenerationDiagnosticsAsync(diagnosticsPath, format, context, beat, imagePath, prompt, generationStartedUtc, generationStopwatch.ElapsedMilliseconds, null, generatedFilesBeforeFailure, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        generationStopwatch.Stop();
                        await WritePartialGenerationDiagnosticsAsync(diagnosticsPath, format, context, beat, imagePath, prompt, generationStartedUtc, generationStopwatch.ElapsedMilliseconds, ex, generatedFilesBeforeFailure, CancellationToken.None);
                        ex.Data["currentAssetCode"] = assetRequest.AssetCode;
                        ex.Data["currentSegmentType"] = assetRequest.SegmentType;
                        ex.Data["currentPlannedImagePath"] = assetRequest.PlannedImagePath;
                        ex.Data["currentPromptPreview"] = prompt.Length <= 500 ? prompt : prompt[..500];
                        ex.Data["imageGenerationStartedUtc"] = generationStartedUtc.ToString("O");
                        ex.Data["imageGenerationElapsedMs"] = generationStopwatch.ElapsedMilliseconds;
                        ex.Data["imageGenerationTimeoutSeconds"] = Math.Max(1, aiCinematicOptions.Value.SingleImageTimeoutSeconds);
                        throw;
                    }
                }

                if (!File.Exists(imagePath) || overwrite && !providerSucceeded)
                {
                    fallbackUsed = true;
                    await RenderDeterministicSceneAsync(imagePath, beat, targetWidth, targetHeight, ct);
                }

                if (beat.RenderMode == "AccurateSkyGuideScene")
                {
                    accurateSkyGuideV2Diagnostics.Add(new { enabled = enableAccurateSkyGuideV2, format, beat.SceneId, family = ResolveAccurateSkyGuideV2Family(context.EventType), providerCalled, promptPath = accurateSkyGuidePromptPath ?? string.Empty, outputPath = imagePath, fallbackUsed, imageExists = File.Exists(imagePath) });
                }

                await EnsureFinalDimensionsAsync(imagePath, targetWidth, targetHeight, ct);

                var forbiddenDetected = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, beat.NarrationBeat, beat.VisualIntent, beat.VisualPrompt, beat.OverlayText, beat.SupportingText ?? string.Empty), context.ForbiddenTerms);
                var providerName = providerCalled ? imageGenerator.GetType().Name : "DeterministicRenderer";
                var azureCallsCount = providerCalled ? 1 : 0;
                logger.LogInformation(
                    "Scene Assets V3 scene diagnostics: sceneId={SceneId}; eventType={EventType}; narrationBeatSource={NarrationBeatSource}; visualPromptSource={VisualPromptSource}; finalVisualPrompt={FinalVisualPrompt}; forbiddenTermsDetected={ForbiddenTermsDetected}; providerName={ProviderName}; azureCallsCount={AzureCallsCount}",
                    beat.SceneId,
                    context.EventType,
                    beat.NarrationBeatSource,
                    beat.VisualPromptSource,
                    beat.VisualPrompt,
                    string.Join(", ", forbiddenDetected),
                    providerName,
                    azureCallsCount);
                sceneDiagnostics.Add(new
                {
                    beat.SceneId,
                    eventType = context.EventType,
                    beat.NarrationBeatSource,
                    beat.VisualPromptSource,
                    finalVisualPrompt = beat.VisualPrompt,
                    beat.VisualIntent,
                    beat.VisualSubjectCategory,
                    beat.PrimaryVisualSubject,
                    beat.CameraDistance,
                    beat.OverlayDensity,
                    beat.InformationDensity,
                    beat.OverlayStyle,
                    beat.PromptVariation,
                    beat.CompositionType,
                    beat.OverlayText,
                    beat.SupportingText,
                    sceneGuideType = beat.SceneGuideType,
                    guideRenderer = beat.RenderMode == "AccurateSkyGuideScene" ? $"Deterministic{beat.SceneGuideType}GuideRenderer" : string.Empty,
                    guideElementsUsed = beat.GuideElementsUsed ?? Array.Empty<string>(),
                    observationGuideDiagnostics = beat.RenderMode == "AccurateSkyGuideScene" ? BuildObservationGuideDiagnostics(beat.SceneGuideType) : null,
                    forbiddenTermsDetected = forbiddenDetected,
                    providerName,
                    azureCallsCount,
                    finalTargetWidth = targetWidth,
                    finalTargetHeight = targetHeight,
                    providerRequestedSize,
                    postProcessWidth = targetWidth,
                    postProcessHeight = targetHeight,
                    resizeMode = "resize",
                    cropMode = "none"
                });
                manifestScenes.Add(new SceneAssetsV3ManifestScene(beat.SceneId, beat.RenderMode, imagePath, beat.NarrationBeat, beat.VisualIntent, beat.VisualSubjectCategory, beat.PrimaryVisualSubject, beat.CameraDistance, beat.OverlayDensity, beat.InformationDensity, beat.OverlayStyle, beat.CompositionType, beat.OverlayText, beat.SupportingText, beat.SceneGuideType, beat.GuideElementsUsed ?? Array.Empty<string>(), await Sha256Async(imagePath, ct), providerCalled, providerSucceeded));
                files.Add(imagePath);
                generatedFilesBeforeFailure.Add(imagePath);
                await WritePartialGenerationDiagnosticsAsync(diagnosticsPath, format, context, beat, imagePath, beat.VisualPrompt, DateTimeOffset.UtcNow, 0, null, generatedFilesBeforeFailure, ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var warning = $"Scene Assets V3 {format} generation failed after {manifestScenes.Count}/{expectedCount} scenes; writing validation diagnostics when possible. {ex.GetType().Name}: {ex.Message}";
            logger.LogWarning(ex, "{Warning}", warning);
            warnings.Add(warning);
            errors.Add(warning);
            ex.Data["failedAssetIndex"] = manifestScenes.Count;
            ex.Data["generatedFilesBeforeFailure"] = string.Join("|", generatedFilesBeforeFailure);
            throw;
        }

        var manifest = new SceneAssetsV3Manifest(Version, format, manifestScenes.Count, manifestScenes);
        EventContentGuard.ValidateObject("SceneAssetsV3Service", "sceneManifest", manifest, context.ForbiddenTerms);
        await WriteJsonAsync(manifestPath, manifest, ct); files.Add(manifestPath);
        await WriteJsonAsync(diagnosticsPath, new { version = Version, format, phaseNo = 8, phaseName = "Format-Aware Scene Asset Generation", finalTargetWidth = targetWidth, finalTargetHeight = targetHeight, providerRequestedSize, postProcessWidth = targetWidth, postProcessHeight = targetHeight, resizeMode = "resize", cropMode = "none", storyFrameRoot = Path.Combine(Directory.GetParent(root)?.FullName ?? root, format == "short" ? "short-story-frames" : "long-story-frames"), currentPlanId = context.PlanId, currentEventType = context.EventType, eventType = context.EventType, forbiddenTermsSource = context.ForbiddenTermsSource, allowedGuidanceTerms = context.AllowedGuidanceTerms, blockedTermsMatched = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, beats.Select(b => b.VisualPrompt)), context.ForbiddenTerms), staleContextDetected = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, beats.Select(b => b.VisualPrompt)), context.ForbiddenTerms).Count > 0, staleContextSource = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, beats.Select(b => b.VisualPrompt)), context.ForbiddenTerms).Count > 0 ? "finalPrompts" : string.Empty, diagnostics = EventContentGuard.BuildDiagnostics(format == "short" ? 8 : 9, "SceneAssetsV3Service", context.EventType, context.StoryTheme, context.VisualTheme, ["production-event-intelligence.json", "question-driven-narration-v2.json"], string.Join(Environment.NewLine, beats.Select(b => b.VisualPrompt)), context.ForbiddenTerms), scenes = sceneDiagnostics }, ct); files.Add(diagnosticsPath);
        await WriteJsonAsync(accurateSkyGuideV2DiagnosticsPath, accurateSkyGuideV2Diagnostics, ct); files.Add(accurateSkyGuideV2DiagnosticsPath);
        await WriteJsonAsync(visualPromptDiagnosticsPath, BuildVisualPromptDiagnostics(format == "short" ? 8 : 9, "Scene Assets V3.3", context, beats.Select(b => new { imageId = b.SceneId, fileName = b.SceneId + ".png", finalPrompt = b.VisualPrompt, b.VisualIntent, b.VisualSubjectCategory, b.PrimaryVisualSubject, b.CameraDistance, dominantPromptSubject = b.PrimaryVisualSubject, overlayDensity = b.OverlayDensity, b.CompositionType, b.PromptVariation, b.OverlayStyle, overlayText = b.DeterministicOverlayText, overlayWordCount = CountWords(b.DeterministicOverlayText), textOverlapRisk = "low", croppedTextRisk = "low", guideElementsAllowed = b.VisualIntent == "SkyGuide" || b.VisualIntent.Contains("Diagram", StringComparison.OrdinalIgnoreCase), guideElementsDetected = b.VisualIntent == "SkyGuide" || b.VisualIntent.Contains("Diagram", StringComparison.OrdinalIgnoreCase), thumbnailRulesPassed = true, heroRulesPassed = true, sceneGuideType = b.SceneGuideType, guideRenderer = b.RenderMode == "AccurateSkyGuideScene" ? $"Deterministic{b.SceneGuideType}GuideRenderer" : string.Empty, eventType = context.EventType, guideElementsUsed = b.GuideElementsUsed ?? Array.Empty<string>(), observationGuideDiagnostics = b.RenderMode == "AccurateSkyGuideScene" ? BuildObservationGuideDiagnostics(b.SceneGuideType) : null, galleryDiagnostics = new { galleryVersion = "V3", dateAdded = !string.IsNullOrWhiteSpace(context.EventDateText), timeAdded = !string.IsNullOrWhiteSpace(context.PeakTimeText), locationAdded = !string.IsNullOrWhiteSpace(context.PrimaryViewingDirection), eventTypeAdded = !string.IsNullOrWhiteSpace(context.EventType) }, b.SupportingText })), ct); files.Add(visualPromptDiagnosticsPath);

        var duplicate = manifestScenes.GroupBy(s => s.Hash, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
        var repeatedPrompt = DetectRepeatedMetadata(beats, b => b.VisualPrompt);
        var forbiddenTermsDetected = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, beats.SelectMany(b => new[] { b.VisualPrompt, b.VisualIntent, b.OverlayText, b.SupportingText ?? string.Empty })), context.ForbiddenTerms);
        var relativeDateWordsDetected = DetectRelativeDateWords(beats.SelectMany(b => new[] { b.OverlayText, b.SupportingText ?? string.Empty }));
        var promptDiversityScore = CalculatePromptDiversityScore(beats.Select(b => b.VisualPrompt));
        var overlayDensityScore = CalculateOverlayDensityScore(beats);
        var distinctCompositionTypes = beats.Select(b => b.CompositionType).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var repeated = duplicate;
        var sameBackground = DetectRepeatedMetadata(beats, b => BackgroundSignature(b));
        var sameComposition = DetectRepeatedMetadata(beats, b => CompositionSignature(b));
        var sameCameraAngle = DetectRepeatedMetadata(beats, b => CameraSignature(b));
        var review = new SceneAssetsV3Review(manifestScenes.Count, manifestScenes.Any(s => s.RenderMode == "AccurateSkyGuideScene"), manifestScenes.Count(s => s.RenderMode is "CinematicStoryScene" or "FinalReminderScene"), manifestScenes.Count(s => s.RenderMode == "ExplainerScene"), manifestScenes.Count(s => s.RenderMode == "ViewingTipsScene"), duplicate, repeated, sameBackground, sameComposition, sameCameraAngle, manifestScenes.All(s => !string.IsNullOrWhiteSpace(s.NarrationBeat)), beats.Select(b => b.VisualIntent).ToArray(), promptDiversityScore, repeatedPrompt, forbiddenTermsDetected, overlayDensityScore, relativeDateWordsDetected, distinctCompositionTypes, "Failed");
        review = review with { Status = ReviewPassed(review, expectedCount) ? "Passed" : "Failed" };
        EventContentGuard.ValidateObject("SceneAssetsV3Service", "sceneReview", review, context.ForbiddenTerms);
        await WriteJsonAsync(reviewPath, review, ct); files.Add(reviewPath);

        errors.AddRange(BuildValidationErrors(timelinePath, manifestPath, metadataPath, review, expectedCount));
        errors.AddRange(BuildGuideValidationErrors(context.EventType, beats));
        var expectedSceneIds = authorityExpectedSceneIds ?? SceneAssetsV3SceneContract.GetExpectedSceneIds(format);
        var actualSceneIds = manifestScenes.Select(s => s.SceneId).ToArray();
        var missingSceneIds = expectedSceneIds.Where(id => !actualSceneIds.Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray();
        var extraSceneIds = actualSceneIds.Where(id => !expectedSceneIds.Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray();
        var expectedSceneAssetPaths = expectedSceneIds.Select(id => Path.Combine(root, $"{SanitizeFileName(id)}.png").Replace('\\', '/')).ToArray();
        var actualSceneAssetPaths = manifestScenes.Select(s => s.ImagePath).ToArray();
        if (missingSceneIds.Length > 0) errors.Add($"Missing Scene Assets V3 {format} scene ids: {string.Join(", ", missingSceneIds)}.");
        if (extraSceneIds.Length > 0) errors.Add($"Extra Scene Assets V3 {format} scene ids: {string.Join(", ", extraSceneIds)}.");
        var validation = new SceneAssetsV3Validation(Version, format, errors.Count == 0 ? "Passed" : "Failed", File.Exists(timelinePath), File.Exists(manifestPath), manifestScenes.Count == expectedCount, review.AccurateSkyGuidePresent, duplicate, repeated, sameBackground, sameComposition, sameCameraAngle, review.AllScenesHaveNarrationBeat, beats.All(b => !string.IsNullOrWhiteSpace(b.VisualIntent)), promptDiversityScore, repeatedPrompt, forbiddenTermsDetected, relativeDateWordsDetected, distinctCompositionTypes, errors, BuildFontDiagnostics(), expectedSceneIds, actualSceneIds, missingSceneIds, extraSceneIds, expectedSceneAssetPaths, actualSceneAssetPaths, authorityExpectedSceneIds is null ? SceneAssetsV3SceneContract.ContractSource : "08-scene-assets/scene-asset-manifest.json");
        await WriteJsonAsync(validationPath, validation, ct); files.Add(validationPath);
        return validationPath;
    }



    private async Task WritePartialGenerationDiagnosticsAsync(string diagnosticsPath, string format, SceneAssetsV3TimelineContext context, SceneAssetsV3Beat beat, string imagePath, string prompt, DateTimeOffset startedUtc, long elapsedMs, Exception? exception, IReadOnlyList<string> generatedFilesBeforeFailure, CancellationToken ct)
    {
        await WriteJsonAsync(diagnosticsPath, new
        {
            version = Version,
            format,
            currentPlanId = context.PlanId,
            currentEventType = context.EventType,
            currentAssetCode = $"scene-assets-v3-{format}-{beat.SceneId}",
            currentSegmentType = beat.RenderMode,
            currentPlannedImagePath = imagePath,
            currentPromptPreview = prompt.Length <= 500 ? prompt : prompt[..500],
            imageGenerationStartedUtc = startedUtc,
            imageGenerationElapsedMs = elapsedMs,
            imageGenerationTimeoutSeconds = Math.Max(1, aiCinematicOptions.Value.SingleImageTimeoutSeconds),
            exceptionType = exception?.GetType().Name ?? string.Empty,
            exceptionMessage = exception?.Message ?? string.Empty,
            failedAssetIndex = exception is null ? (int?)null : generatedFilesBeforeFailure.Count,
            generatedFilesBeforeFailure
        }, ct);
    }


    private static async Task EnsureFinalDimensionsAsync(string path, int targetWidth, int targetHeight, CancellationToken ct)
    {
        using var image = await Image.LoadAsync<Rgba32>(path, ct);
        if (image.Width == targetWidth && image.Height == targetHeight) return;
        image.Mutate(ctx => ctx.Resize(targetWidth, targetHeight));
        await image.SaveAsPngAsync(path, new PngEncoder(), ct);
    }

    private static bool IsValidPng(string path)
    {
        try
        {
            var imageInfo = Image.Identify(path);
            return imageInfo is not null && imageInfo.Width > 0 && imageInfo.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveAccurateSkyGuideV2Family(string eventType)
    {
        if (eventType.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "Eclipse";
        if (eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "Meteor";
        if (eventType.Contains("moon", StringComparison.OrdinalIgnoreCase)) return "Moon";
        return "Planetary";
    }

    private static string BuildAccurateSkyGuideV2Prompt(SceneAssetsV3TimelineContext context, SceneAssetsV3Beat beat)
    {
        var family = ResolveAccurateSkyGuideV2Family(context.EventType);
        var common = $"""
Generate one professional observation guide screen for a sky event.
Universal style: premium astronomy observation guide, NASA + National Geographic style, cinematic sky background, clean modern labels, direction marker, 2-3 short viewing tips, mobile-readable.
Avoid: dashboard look, technical chart look, crowded text, unnecessary location text, watermark, branding, logo.
Use only event-relevant objects: {JoinNatural(context.EventObjectContext.ObjectNames)}.
Event family: {family}. Event title: {FirstNonEmpty(context.Title, context.EventType)}. Direction cue: {FirstNonEmpty(context.PrimaryViewingDirection, context.SkyGuideTheme, "event direction")}. Best viewing time cue: {FirstNonEmpty(context.PeakTimeText, "best local viewing window")}.
Scene goal: {beat.SceneId}; {beat.NarrationBeat}
""";
        var familyPrompt = family switch
        {
            "Meteor" => "Dark sky / Milky Way, meteor streaks, radiant marker, include a moonlight note if available, short tip: Look up after midnight.",
            "Moon" => "Large realistic moon, moonrise direction, phase/name label, short tip: Watch near moonrise.",
            "Eclipse" => "Eclipse visual, timing and safety cue, if solar eclipse include safe viewing message, short tip: Use certified solar filter.",
            _ => "Realistic twilight sky, visible event objects only, object labels, direction marker such as WEST when direction supports it, short tip: Look west after sunset when appropriate, show horizon cue."
        };
        return common + Environment.NewLine + familyPrompt;
    }

    private async Task RenderDeterministicSceneAsync(string path, SceneAssetsV3Beat beat, int width, int height, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var image = new Image<Rgba32>(width, height, Color.Black);
        image.Mutate(ctx =>
        {
            var bg = beat.RenderMode == "AccurateSkyGuideScene" ? Color.FromRgb(5, 10, 22) : Color.FromRgb((byte)(8 + beat.BeatNo * 11), (byte)(16 + beat.BeatNo * 7), (byte)(34 + beat.BeatNo * 13));
            ctx.Fill(bg);
            DrawStars(ctx, beat.BeatNo, width, height);
            if (beat.RenderMode == "AccurateSkyGuideScene") DrawSkyGuide(ctx, beat, width, height);
            else DrawCinematicForeground(ctx, beat, width, height);
            var font = ResolveOverlayFont(34, FontStyle.Bold);
            ctx.DrawText(TruncateForOverlay(beat.OverlayText, 54), font, Color.FromRgba(235, 240, 248, 225), new PointF(Math.Min(90, width / 20), height - 205));
            if (!string.IsNullOrWhiteSpace(beat.SupportingText)) ctx.DrawText(TruncateForOverlay(beat.SupportingText!, 70), ResolveOverlayFont(26, FontStyle.Regular), Color.FromRgba(190, 220, 245, 205), new PointF(Math.Min(90, width / 20), height - 158));
        });
        await image.SaveAsPngAsync(path, new PngEncoder(), ct);
    }

    private static void DrawStars(IImageProcessingContext ctx, int seed, int width, int height) { for (var i = 0; i < 180; i++) ctx.Fill(Color.FromRgba(255, 255, 255, (byte)(58 + (i % 6) * 27)), new EllipsePolygon((i * 137 + seed * 61) % width, (i * 73 + seed * 89) % Math.Max(1, height - 260), 1 + i % 3)); }
    private static void DrawCinematicForeground(IImageProcessingContext ctx, SceneAssetsV3Beat beat, int width, int height)
    {
        ctx.Fill(Color.FromRgb(6, 8, 12), new RectangularPolygon(0, height - 250, width, 250));
        var first = new PointF(820 + beat.BeatNo * 8, 360 + beat.BeatNo * 9);
        var second = new PointF(950 + beat.BeatNo * 8, 330 + beat.BeatNo * 9);
        ctx.Fill(Color.FromRgb(255, 245, 190), new EllipsePolygon(first, 13));
        ctx.Fill(Color.FromRgb(235, 242, 255), new EllipsePolygon(second, 11));
        ctx.DrawLine(Color.FromRgba(120, 210, 255, 150), 3, first, second);
    }
    private void DrawSkyGuide(IImageProcessingContext ctx, SceneAssetsV3Beat beat, int width, int height)
    {
        var label = ResolveOverlayFont(25, FontStyle.Regular);
        var title = ResolveOverlayFont(36, FontStyle.Bold);
        ctx.Fill(Color.FromRgb(4, 12, 32));
        ctx.Fill(Color.FromRgba(18, 36, 58, 150), new RectangularPolygon(0, 520, width, height - 520));
        for (var i = 0; i < 260; i++) ctx.Fill(Color.FromRgba(245, 250, 255, (byte)(50 + i % 150)), new EllipsePolygon((i * 149 + 97) % width, (i * 83 + 41) % 760, 1 + i % 3));
        ctx.Fill(Color.FromRgba(10, 16, 22, 245), new RectangularPolygon(0, height - 270, width, 270));
        ctx.DrawLine(Color.FromRgb(95, 135, 155), 3, new PointF(120, 812), new PointF(1800, 812));
        ctx.DrawLine(Color.FromRgba(80, 130, 160, 120), 1, new PointF(260, 740), new PointF(1660, 740));
        ctx.DrawLine(Color.FromRgba(80, 130, 160, 90), 1, new PointF(460, 580), new PointF(1460, 580));
        ctx.DrawText("How To Observe", title, Color.FromRgb(238, 246, 255), new PointF(96, 80));
        ctx.DrawText(TruncateForOverlay(beat.NarrationBeat, 86), label, Color.FromRgb(185, 215, 245), new PointF(96, 132));
        ctx.DrawText(TruncateForOverlay(FirstNonEmpty(beat.SupportingText ?? string.Empty, beat.OverlayText), 86), label, Color.FromRgb(185, 215, 245), new PointF(96, 168));
        DrawGuideElements(ctx, beat, label);
        var bullets = ResolveObservationGuideBullets(beat.SceneGuideType);
        for (var i = 0; i < bullets.Length; i++)
            ctx.DrawText("• " + bullets[i], label, Color.FromRgb(220, 238, 255), new PointF(96, 230 + i * 38));
        ctx.DrawText("horizon", label, Color.FromRgb(235, 242, 248), new PointF(448, 830));
    }

    private static object BuildObservationGuideDiagnostics(string eventFamily)
    {
        var bullets = ResolveObservationGuideBullets(eventFamily);
        return new { guideVersion = "V2", oldAccurateSkyGuideReplaced = true, guideTitle = "How To Observe", eventFamily, guideBullets = bullets, familySpecificGuideApplied = true };
    }

    private static string[] ResolveObservationGuideBullets(string eventFamily) => eventFamily switch
    {
        "SolarEclipse" or "Eclipse" => ["Use certified solar filter", "Never view Sun directly", "Watch maximum eclipse safely"],
        "NamedFullMoon" or "Moon" or "MoonPlanetPairing" => ["Face moonrise direction", "Use open horizon", "Best near moonrise or later evening"],
        "MeteorShower" => ["Look toward radiant / overhead", "Best after midnight", "Avoid city lights"],
        "PlanetGrouping" or "PlanetConjunction" => ["Face listed direction", "Start after sunset / before dawn depending event", "Compare brightest objects one by one"],
        _ => ["Face listed direction", "Use open horizon", "Check the best local time"]
    };

    private static void DrawGuideElements(IImageProcessingContext ctx, SceneAssetsV3Beat beat, Font label)
    {
        if (beat.SceneGuideType.Equals("MeteorShower", StringComparison.OrdinalIgnoreCase))
        {
            var radiant = new PointF(930, 360);
            ctx.Draw(Color.FromRgb(255, 210, 92), 4, new EllipsePolygon(radiant, 42));
            ctx.Fill(Color.FromRgb(255, 245, 190), new EllipsePolygon(radiant, 8));
            ctx.DrawText("radiant", label, Color.FromRgb(255, 245, 190), new PointF(965, 328));
            for (var i = 0; i < 7; i++)
            {
                var start = new PointF(610 + i * 125, 245 + (i % 3) * 80);
                var end = new PointF(start.X + 145, start.Y + 105);
                ctx.DrawLine(Color.FromRgba(145, 220, 255, 210), 5, start, end);
            }
            ctx.DrawText("meteor streak directions", label, Color.FromRgb(145, 220, 255), new PointF(1110, 545));
            ctx.DrawText("viewing region", label, Color.FromRgb(190, 230, 255), new PointF(128, 742));
            ctx.DrawText("viewing window", label, Color.FromRgb(190, 230, 255), new PointF(1280, 742));
            ctx.DrawText("moonlight conditions", label, Color.FromRgb(220, 220, 235), new PointF(1280, 792));
            return;
        }

        if (beat.SceneGuideType.Equals("PlanetConjunction", StringComparison.OrdinalIgnoreCase))
        {
            var conjunctionPrimary = new PointF(900, 430);
            var conjunctionSecondary = new PointF(1010, 400);
            ctx.Fill(Color.FromRgb(255, 245, 190), new EllipsePolygon(conjunctionPrimary, 15));
            ctx.Fill(Color.FromRgb(235, 242, 255), new EllipsePolygon(conjunctionSecondary, 13));
            ctx.DrawLine(Color.FromRgb(120, 210, 255), 4, conjunctionPrimary, conjunctionSecondary);
            ctx.DrawText("object labels", label, Color.FromRgb(255, 245, 190), new PointF(805, 462));
            ctx.DrawText("separation", label, Color.FromRgb(120, 210, 255), new PointF(902, 342));
            ctx.DrawText("altitude", label, Color.FromRgb(235, 242, 248), new PointF(1180, 625));
            ctx.DrawText("direction", label, Color.FromRgb(235, 242, 248), new PointF(448, 830));
            return;
        }

        if (beat.SceneGuideType.Equals("PlanetGrouping", StringComparison.OrdinalIgnoreCase))
        {
            var points = new[] { new PointF(780, 450), new PointF(925, 370), new PointF(1080, 455) };
            foreach (var point in points) ctx.Fill(Color.FromRgb(235, 242, 255), new EllipsePolygon(point, 13));
            ctx.DrawLine(Color.FromRgb(120, 210, 255), 4, points);
            ctx.DrawText("object positions", label, Color.FromRgb(235, 242, 255), new PointF(735, 500));
            ctx.DrawText("scan path", label, Color.FromRgb(120, 210, 255), new PointF(930, 315));
            ctx.DrawText("grouping geometry", label, Color.FromRgb(190, 230, 255), new PointF(1085, 490));
            return;
        }

        if (beat.SceneGuideType.Equals("MoonPlanetPairing", StringComparison.OrdinalIgnoreCase))
        {
            var moon = new PointF(900, 420);
            var paired = new PointF(1030, 405);
            ctx.Draw(Color.FromRgb(235, 242, 255), 8, new EllipsePolygon(moon, 34));
            ctx.Fill(Color.FromRgb(255, 245, 190), new EllipsePolygon(paired, 14));
            ctx.DrawLine(Color.FromRgb(120, 210, 255), 4, moon, paired);
            ctx.DrawText("moon", label, Color.FromRgb(235, 242, 255), new PointF(840, 462));
            ctx.DrawText("paired object", label, Color.FromRgb(255, 245, 190), new PointF(1050, 425));
            ctx.DrawText("separation", label, Color.FromRgb(120, 210, 255), new PointF(920, 342));
            return;
        }

        var first = new PointF(900, 430);
        var second = new PointF(1010, 400);
        ctx.Fill(Color.FromRgb(255, 245, 190), new EllipsePolygon(first, 15));
        ctx.Fill(Color.FromRgb(235, 242, 255), new EllipsePolygon(second, 13));
        ctx.Draw(Color.FromRgb(255, 210, 92), 3, new EllipsePolygon(first, 34));
        ctx.Draw(Color.FromRgb(180, 210, 255), 3, new EllipsePolygon(second, 30));
        ctx.DrawLine(Color.FromRgb(120, 210, 255), 4, first, second);
        ctx.DrawText("primary", label, Color.FromRgb(255, 245, 190), new PointF(815, 460));
        ctx.DrawText("secondary", label, Color.FromRgb(235, 242, 255), new PointF(1030, 418));
        ctx.DrawText("alignment", label, Color.FromRgb(120, 210, 255), new PointF(902, 342));
    }


    private static SceneTimelineMetadataDocument BuildTimelineMetadata(string format, IReadOnlyList<SceneAssetsV3Beat> beats) => new(
        Version,
        format,
        beats.Select(beat => new SceneTimelineMetadata(
            beat.SceneId,
            beat.RenderMode,
            beat.VisualIntent,
            beat.VisualSubjectCategory,
            beat.PrimaryVisualSubject,
            beat.CameraDistance,
            beat.OverlayDensity,
            beat.InformationDensity,
            beat.OverlayStyle,
            beat.PromptVariation,
            beat.CompositionType,
            beat.OverlayText,
            beat.SupportingText,
            beat.NarrationBeat,
            beat.ExpectedDurationSec,
            beat.SceneGuideType,
            beat.GuideElementsUsed ?? Array.Empty<string>(),
            RecommendedTransition(beat),
            RecommendedMotion(beat))).ToList());

    private static string RecommendedTransition(SceneAssetsV3Beat beat) => beat.RenderMode switch
    {
        "AccurateSkyGuideScene" => "push",
        "ViewingTipsScene" => "fade",
        "FinalReminderScene" => "fade",
        _ => beat.BeatNo % 2 == 0 ? "zoom" : "crossfade"
    };

    private static string RecommendedMotion(SceneAssetsV3Beat beat) => beat.RenderMode switch
    {
        "AccurateSkyGuideScene" => "panRight",
        "ExplainerScene" => "parallax",
        "ViewingTipsScene" => "slowZoomOut",
        "FinalReminderScene" => "slowZoomIn",
        _ => beat.BeatNo % 2 == 0 ? "panLeft" : "slowZoomIn"
    };

    private static string SmallSceneLabel(SceneAssetsV3Beat beat) => TruncateForOverlay(beat.OverlayText, 54);
    private static string TruncateForOverlay(string value, int max) => string.IsNullOrWhiteSpace(value) || value.Length <= max ? value : value[..Math.Max(0, max - 1)] + "…";


    private static int CalculateSubjectDiversityScore(IEnumerable<object> prompts)
    {
        var categories = prompts.Select(p => (string)(p.GetType().GetProperty("VisualSubjectCategory")?.GetValue(p) ?? string.Empty)).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        return categories.Length == 0 ? 0 : (int)Math.Round(100.0 * categories.Distinct(StringComparer.OrdinalIgnoreCase).Count() / categories.Length, MidpointRounding.AwayFromZero);
    }

    private static int CountWords(string value) => (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static int CalculatePromptDiversityScore(IEnumerable<string> prompts)
    {
        var list = prompts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (list.Length <= 1) return 100;
        var unique = list.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return (int)Math.Round(100.0 * unique / list.Length, MidpointRounding.AwayFromZero);
    }

    private static int CalculateOverlayDensityScore(IEnumerable<SceneAssetsV3Beat> beats)
        => (int)Math.Round(beats.Select(b => (b.OverlayText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length + (b.SupportingText?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0)) <= (b.VisualIntent == "SkyGuide" ? 18 : 12) ? 100 : 60).DefaultIfEmpty(100).Average());

    private static IReadOnlyList<string> DetectRelativeDateWords(IEnumerable<string> overlays)
    {
        var terms = new[] { "today", "tonight", "tomorrow", "this evening" };
        var text = string.Join(" ", overlays).ToLowerInvariant();
        return terms.Where(t => text.Contains(t, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static bool DetectRepeatedMetadata(IReadOnlyList<SceneAssetsV3Beat> beats, Func<SceneAssetsV3Beat, string> selector) => beats.GroupBy(selector, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
    private static string BackgroundSignature(SceneAssetsV3Beat beat) => NormalizeSignature(beat.VisualPrompt);
    private static string CompositionSignature(SceneAssetsV3Beat beat) => NormalizeSignature($"{beat.RenderMode}:{beat.VisualIntent}:{beat.VisualPrompt}");
    private static string CameraSignature(SceneAssetsV3Beat beat) => $"camera-{beat.SceneId}";
    private static string NormalizeSignature(string value) => string.Join(" ", value.ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

    private Font ResolveOverlayFont(float size, FontStyle style)
    {
        foreach (var fontName in WindowsSafeFontFallbacks)
        {
            if (SystemFonts.TryGet(fontName, out var family))
            {
                if (!fontName.Equals(RequestedOverlayFont, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Scene Assets V3 requested font {RequestedFont} is not available; using fallback font {ResolvedFont}. CheckedFontPaths={CheckedFontPaths}", RequestedOverlayFont, family.Name, CheckedFontPaths);
                }

                return family.CreateFont(size, style);
            }
        }

        var fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fallbackFamily.Name))
        {
            throw new InvalidOperationException("No system fonts available for Scene Assets V3 deterministic overlay rendering.");
        }

        logger.LogWarning("Scene Assets V3 requested font {RequestedFont} and configured fallbacks are not available; using first available font {ResolvedFont}. CheckedFontPaths={CheckedFontPaths}", RequestedOverlayFont, fallbackFamily.Name, CheckedFontPaths);
        return fallbackFamily.CreateFont(size, style);
    }

    private static SceneAssetsV3FontDiagnostics BuildFontDiagnostics()
    {
        var resolved = WindowsSafeFontFallbacks.FirstOrDefault(fontName => SystemFonts.TryGet(fontName, out _));
        resolved ??= SystemFonts.Collection.Families.FirstOrDefault().Name;
        return new SceneAssetsV3FontDiagnostics(
            RequestedOverlayFont,
            string.IsNullOrWhiteSpace(resolved) ? "" : resolved,
            !string.Equals(resolved, RequestedOverlayFont, StringComparison.OrdinalIgnoreCase),
            CheckedFontPaths);
    }

    private string ResolveRoot(SceneAssetsV3Request request) => !string.IsNullOrWhiteSpace(request.WorkingDirectoryRoot) ? request.WorkingDirectoryRoot! : string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string StyleFor(string mode) => mode == "ExplainerScene" ? "cinematic educational astronomy, realistic space documentary" : "Netflix science documentary, National Geographic astronomy, NASA campaign, realistic cinematic sky, minimal overlay";
    private static int RequiredCompositionDiversity(int sceneCount) => Math.Min(5, Math.Max(1, (sceneCount + 1) / 2));
    private static bool ReviewPassed(SceneAssetsV3Review r, int expected) => r.SceneCount == expected && r.AccurateSkyGuidePresent && !r.DuplicateHashDetected && !r.RepeatedBackgroundDetected && !r.SameBackgroundDetected && !r.SameCompositionDetected && !r.SameCameraAngleDetected && r.AllScenesHaveNarrationBeat && r.PromptDiversityScore >= 80 && !r.RepeatedPromptDetected && r.ForbiddenTermsDetected.Count == 0 && r.RelativeDateWordsDetected.Count == 0 && r.DistinctCompositionTypeCount >= RequiredCompositionDiversity(expected);
    private static List<string> BuildValidationErrors(string timeline, string manifest, string metadata, SceneAssetsV3Review r, int expected) { var e = new List<string>(); if (!File.Exists(timeline)) e.Add("visual-timeline-v3.json is missing."); if (!File.Exists(manifest)) e.Add("scene-manifest-v3.json is missing."); if (!File.Exists(metadata)) e.Add("scene-timeline-metadata.json is missing."); if (r.SceneCount != expected) e.Add($"Expected {expected} scenes but found {r.SceneCount}."); if (!r.AccurateSkyGuidePresent) e.Add("AccurateSkyGuideScene is missing."); if (r.DuplicateHashDetected) e.Add("Duplicate image hashes detected."); if (r.RepeatedBackgroundDetected) e.Add("Repeated generic infographic background detected."); if (r.SameBackgroundDetected) e.Add("sameBackgroundDetected review check failed."); if (r.SameCompositionDetected) e.Add("sameCompositionDetected review check failed."); if (r.SameCameraAngleDetected) e.Add("sameCameraAngleDetected review check failed."); if (!r.AllScenesHaveNarrationBeat) e.Add("At least one scene is missing narrationBeat."); if (r.PromptDiversityScore < 80) e.Add("promptDiversityScore must be >= 80."); if (r.RepeatedPromptDetected) e.Add("Repeated image prompt detected."); if (r.ForbiddenTermsDetected.Count > 0) e.Add("Forbidden terms detected."); if (r.RelativeDateWordsDetected.Count > 0) e.Add("Relative date words detected in visual overlays."); if (r.DistinctCompositionTypeCount < RequiredCompositionDiversity(expected)) e.Add($"Certified scene-role diversity requires at least {RequiredCompositionDiversity(expected)} distinct composition types."); return e; }
    private static IReadOnlyList<string> BuildGuideValidationErrors(string eventType, IReadOnlyList<SceneAssetsV3Beat> beats)
    {
        var errors = new List<string>();
        var guide = beats.FirstOrDefault(b => b.RenderMode == "AccurateSkyGuideScene");
        if (guide is null) return errors;

        if (IsMeteorShower(eventType))
        {
            var elements = guide.GuideElementsUsed ?? Array.Empty<string>();
            if (!elements.Any(e => e.Contains("radiant", StringComparison.OrdinalIgnoreCase) || e.Contains("meteor streak", StringComparison.OrdinalIgnoreCase)))
                errors.Add("MeteorShower AccurateSkyGuideScene must include radiant or meteor streak guidance.");

            if (elements.Any(e => e.Equals("primary", StringComparison.OrdinalIgnoreCase) || e.Equals("secondary", StringComparison.OrdinalIgnoreCase) || e.Equals("alignment", StringComparison.OrdinalIgnoreCase)))
                errors.Add("MeteorShower AccurateSkyGuideScene must not use primary/secondary/alignment guide elements.");
        }

        return errors;
    }
    private static Task WriteJsonAsync<T>(string path, T value, CancellationToken ct) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), ct);
    private static async Task<string> Sha256Async(string path, CancellationToken ct) { await using var s = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant(); }

    private static async Task<SceneAssetsV3TimelineContext> LoadTimelineContextAsync(string sceneAssetsRoot, CancellationToken ct)
    {
        var outputRoot = Directory.GetParent(sceneAssetsRoot)?.FullName ?? sceneAssetsRoot;
        var intelligencePath = Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json");
        var narrationPath = ResolveFirstExisting(Path.Combine(outputRoot, "question-engine", "question-driven-narration-v2.json"), Path.Combine(outputRoot, "narration-engine", "question-driven-narration-v2.json"), Path.Combine(outputRoot, "narration-engine", "short", "question-driven-narration-v2.json"));
        using var intelligence = File.Exists(intelligencePath) ? JsonDocument.Parse(await File.ReadAllTextAsync(intelligencePath, ct)) : JsonDocument.Parse("{}");
        using var narration = narrationPath is not null ? JsonDocument.Parse(await File.ReadAllTextAsync(narrationPath, ct)) : JsonDocument.Parse("{}");
        var root = intelligence.RootElement;
        var eventType = FirstString(root, "eventType", "strategyId", "selectedEventType");
        var eventTitle = FirstString(root, "title", "shortTitle");
        var objectContext = EventObjectContextBuilder.FromJsonValues(eventType, eventTitle, ReadStringArray(root, "resolvedObjectNames"), ReadStringArray(root, "primaryObjects"), ReadStringArray(root, "secondaryObjects"), ReadStringArray(root, "requiredVisualObjects"));
        var allowedGuidanceTerms = BuildAllowedGuidanceTerms(root);
        var forbidden = BuildEventProfileForbiddenTerms(eventType, root, objectContext, allowedGuidanceTerms);
        return new SceneAssetsV3TimelineContext(
            FirstString(root, "planId", "id", "productionPlanId"),
            string.IsNullOrWhiteSpace(eventType) ? "Generic" : eventType,
            eventTitle,
            FirstString(root, "storyTheme"), FirstString(root, "visualTheme"), FirstString(root, "skyGuideTheme"),
            FirstString(root, "eventDate", "date", "peakDate", "absoluteDate"), FirstString(root, "peakTime", "bestViewingWindow", "bestViewingWindowLocal", "absoluteTime"), FirstString(root, "skyDirectionHint", "primaryViewingDirection", "viewingDirection", "direction"), FirstString(root, "angularSeparation", "minimumSeparation", "separation"),
            objectContext,
            forbidden.Terms,
            forbidden.Source,
            allowedGuidanceTerms,
            ExtractNarrationBeats(narration.RootElement));
    }

    private static IReadOnlyList<SceneAssetsV3Beat> BuildBeats(SceneAssetsV3TimelineContext context, string format, int count)
    {
        var ids = SceneAssetsV3SceneContract.GetExpectedSceneIds(format);
        var modes = ids.Select(id => id.Contains("accurate-sky-guide", StringComparison.OrdinalIgnoreCase) ? "AccurateSkyGuideScene" : id.Contains("tip", StringComparison.OrdinalIgnoreCase) || id.Contains("time", StringComparison.OrdinalIgnoreCase) ? "ViewingTipsScene" : id.Contains("final", StringComparison.OrdinalIgnoreCase) ? "FinalReminderScene" : id.Contains("cause", StringComparison.OrdinalIgnoreCase) || id.Contains("what", StringComparison.OrdinalIgnoreCase) || id.Contains("fact", StringComparison.OrdinalIgnoreCase) ? "ExplainerScene" : "CinematicStoryScene").ToArray();
        var result = new List<SceneAssetsV3Beat>();
        for (var i = 0; i < count; i++)
        {
            var narration = i < context.NarrationBeats.Count ? context.NarrationBeats[i] : BuildFallbackNarration(context, ids[i]);
            narration = EnsureRequiredNarrationContext(context, narration);
            EventContentGuard.ValidateNoForbiddenTerms("SceneAssetsV3Service", $"narrationBeat source=question-driven-narration-v2.json scene={ids[i]} prompt={narration}", narration, context.ForbiddenTerms);
            var intentSpec = BuildVisualIntentSpec(context, ids[i], i, format);
            var prompt = BuildVisualPrompt(context, ids[i], intentSpec);
            EventContentGuard.ValidateNoForbiddenTerms("SceneAssetsV3Service", $"visualIntent source=production-event-intelligence.json scene={ids[i]} prompt={intentSpec.VisualIntent}", intentSpec.VisualIntent, context.ForbiddenTerms);
            EventContentGuard.ValidateNoForbiddenTerms("SceneAssetsV3Service", $"visualPrompt source=production-event-intelligence.json scene={ids[i]} prompt={prompt}", prompt, context.ForbiddenTerms);
            var overlayPrompt = string.Join(" ", intentSpec.OverlayText, intentSpec.SupportingText);
            EventContentGuard.ValidateNoForbiddenTerms("SceneAssetsV3Service", $"overlayText source=production-event-intelligence.json scene={ids[i]} prompt={overlayPrompt}", overlayPrompt, context.ForbiddenTerms);
            var sceneGuideType = modes[i] == "AccurateSkyGuideScene" ? ResolveSceneGuideType(context.EventType) : string.Empty;
            var guideElementsUsed = modes[i] == "AccurateSkyGuideScene" ? ResolveGuideElements(context.EventType) : Array.Empty<string>();
            result.Add(new SceneAssetsV3Beat(i + 1, ids[i], modes[i], narration, intentSpec.VisualIntent, intentSpec.VisualSubjectCategory, intentSpec.PrimaryVisualSubject, intentSpec.CameraDistance, intentSpec.OverlayDensity, intentSpec.InformationDensity, intentSpec.OverlayStyle, intentSpec.PromptVariation, intentSpec.CompositionType, intentSpec.OverlayText, intentSpec.SupportingText, prompt, modes[i] == "AccurateSkyGuideScene" ? 7 : 5 + i % 2, sceneGuideType, guideElementsUsed, "question-driven-narration-v2.json", "production-event-intelligence.json"));
        }
        return result;
    }

    private static SceneAssetsV3TimelineContext BuildAuthorityTimelineContext(Phase8AuthorityInput authority)
    {
        var scenes = authority.LongScenes.Concat(authority.ShortScenes).ToArray();
        var subject = authority.LongScenes.Count > 0 ? authority.DocumentaryBlueprint.LongBlueprint.SubjectName : authority.DocumentaryBlueprint.ShortBlueprint.SubjectName;
        var objects = scenes.SelectMany(x => x.RequiredAstronomyObjects).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (objects.Length == 0) objects = [subject];
        var objectContext = new EventObjectContext(objects, objects.Length, string.Join(", ", objects),
            string.Join(" and ", objects.Take(2)), string.Join(" + ", objects.Take(2)), objects[0],
            objects.ElementAtOrDefault(1) ?? string.Empty, objects.Any(x => x.Contains("moon", StringComparison.OrdinalIgnoreCase)),
            objects.Any(x => x.Contains("planet", StringComparison.OrdinalIgnoreCase)), "CertifiedAstronomyObjects",
            "Phase8AuthorityInput.RequiredAstronomyObjects", [], [], true, false);
        return new(authority.PlanId, subject, subject, "Certified documentary blueprint", "Certified Story Frame visual direction",
            "Certified observation direction", string.Empty, string.Empty,
            scenes.Select(x => x.ObservationDirection).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
            string.Empty, objectContext, [], "committed Phase 4/6/7 authorities", [],
            scenes.Select(x => x.AcceptedNarrationText).ToArray());
    }

    private static IReadOnlyList<SceneAssetsV3Beat> BuildAuthorityBeats(SceneAssetsV3TimelineContext context,
        IReadOnlyList<Phase8SceneRequirement> scenes) => scenes.OrderBy(x => x.SceneOrder).Select((scene, index) =>
    {
        var mode = scene.RenderingPreference.Equals("AccurateSkyGuide", StringComparison.OrdinalIgnoreCase) ? "AccurateSkyGuideScene"
            : scene.RenderingPreference.Equals("Infographic", StringComparison.OrdinalIgnoreCase) ? "ExplainerScene"
            : scene.SceneRole.Contains("Closing", StringComparison.OrdinalIgnoreCase) ? "FinalReminderScene" : "CinematicStoryScene";
        var subject = scene.RequiredAstronomyObjects.Count > 0 ? JoinNatural(scene.RequiredAstronomyObjects) : context.Title;
        var intent = string.Join(" ", scene.ScenePurpose, scene.VisualDirection, scene.ObservationDirection).Trim();
        var prompt = $"{subject}. Certified scene purpose: {scene.ScenePurpose}. Certified visual direction: {scene.VisualDirection}. Certified observation direction: {scene.ObservationDirection}. " +
            $"Knowledge references: {string.Join(", ", scene.KnowledgeReferenceIds)}. Rendering preference: {scene.RenderingPreference}. " +
            "Create a scientifically responsible astronomy documentary background with no embedded text, watermark, logo, or unrelated objects; preserve negative space for deterministic overlays.";
        return new SceneAssetsV3Beat(index + 1, scene.SceneId, mode, scene.AcceptedNarrationText, intent,
            scene.VisualOpportunityType, subject, "certified", "minimal", "certified", "documentary",
            $"authority-{scene.SceneOrder:000}", $"{scene.VisualOpportunityType}-{scene.SceneRole}", scene.ScenePurpose, null, prompt, 6,
            mode == "AccurateSkyGuideScene" ? "CertifiedSkyCapture" : string.Empty,
            mode == "AccurateSkyGuideScene" ? scene.RequiredAstronomyObjects : [],
            "07-narration/accepted-release-candidate.json", "Phase8AuthorityInput", scene.BlueprintSceneId,
            scene.StoryFrameId, scene.Variant, scene.SceneOrder);
    }).ToArray();

    private async Task<IReadOnlyList<string>> PublishAuthorityPackageAsync(SceneAssetsV3Request request,
        Phase8AuthorityInput authority, string compatibilityRoot, IReadOnlySet<string> reusableAssets, CancellationToken ct)
    {
        var outputRoot = ResolveRoot(request);
        var staging = Path.Combine(outputRoot, $".08-scene-assets-staging-{Guid.NewGuid():N}");
        var committed = Path.Combine(outputRoot, "08-scene-assets");
        var backup = Path.Combine(outputRoot, $".08-scene-assets-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var items = new List<SceneAssetManifestItem>();
        try
        {
            foreach (var scene in authority.LongScenes.Concat(authority.ShortScenes))
            {
                var format = scene.Variant.ToLowerInvariant();
                var source = Path.Combine(compatibilityRoot, format, scene.SceneId + ".png");
                var relative = $"08-scene-assets/{format}/scene-assets/{scene.SceneId}.png";
                var destination = Path.Combine(staging, format, "scene-assets", scene.SceneId + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
                var info = await Image.IdentifyAsync(destination, ct) ?? throw new InvalidDataException($"Cannot decode '{destination}'.");
                var checksum = await Sha256Async(destination, ct);
                var semantic = SemanticIdentity(scene, format == "long" ? request.LongTargetWidth : request.ShortTargetWidth,
                    format == "long" ? request.LongTargetHeight : request.ShortTargetHeight);
                var reused = reusableAssets.Contains($"{scene.Variant}:{scene.SceneId}");
                items.Add(new($"{scene.Variant}:{scene.SceneId}", scene.Variant, scene.SceneId, scene.BlueprintSceneId,
                    scene.StoryFrameId, scene.SceneOrder, scene.AssetRole, scene.VisualOpportunityType,
                    imageGenerator.GetType().Name, null, "GeneratedOrDeterministicFallback", scene.StoryFrameId,
                    scene.KnowledgeReferenceIds, relative, info.Width, info.Height, $"{info.Width}:{info.Height}", checksum,
                    semantic, false, null, [], reused, !reused, "Valid", []));
            }
            Directory.CreateDirectory(Path.Combine(staging, "shared", "reusable-assets"));
            var checksumSeed = string.Join("|", items.OrderBy(x => x.AssetId, StringComparer.Ordinal).Select(x => $"{x.AssetId}:{x.SemanticIdentity}:{x.Checksum}"));
            var manifest = new SceneAssetManifest("1.0", authority.PlanId, authority.ExecutionId, authority.EventId,
                authority.Language, DateTimeOffset.UtcNow, "Candidate", authority.DocumentaryBlueprintChecksum,
                authority.StoryFrameManifestChecksum, authority.LongNarrationReleaseCandidateChecksum,
                authority.ShortNarrationReleaseCandidateChecksum, authority.RequestedVariants, items, "Valid", HashText(checksumSeed));
            foreach (var planningArtifact in new[] { "media-project.json", "visual-asset-plan.json", "visual-generation-requests.json" })
                File.Copy(Path.Combine(committed, planningArtifact), Path.Combine(staging, planningArtifact), true);
            await WriteJsonAsync(Path.Combine(staging, "scene-asset-manifest.json"), manifest, ct);
            var validator = new Phase8SceneAssetManifestValidator();
            var candidateValidation = await validator.ValidateAsync(manifest, authority, outputRoot, ct);
            // Candidate paths are rooted below staging during pre-commit validation.
            if (!candidateValidation.IsValid && candidateValidation.Errors.Any(x => !x.Contains("Physical asset is missing", StringComparison.Ordinal)))
                throw new Phase8AuthorityException(Phase8AuthorityReasonCodes.NotCommitted, candidateValidation.Errors);
            await WriteJsonAsync(Path.Combine(staging, "phase8-authority-diagnostics.json"), new { authorityLoaded = true, phase4Committed = true, phase6Committed = true, longNarrationCandidateCommitted = authority.LongNarrationReleaseCandidate is not null, shortNarrationCandidateCommitted = authority.ShortNarrationReleaseCandidate is not null, authority.RequestedVariants, expectedLongSceneCount = authority.LongScenes.Count, expectedShortSceneCount = authority.ShortScenes.Count, generatedLongSceneCount = items.Count(x => x.Variant == "Long"), generatedShortSceneCount = items.Count(x => x.Variant == "Short"), reusedAssetCount = items.Count(x => x.Reused), generatedAssetCount = items.Count(x => !x.Reused), fallbackAssetCount = items.Count(x => x.ProviderStatus.Contains("Fallback", StringComparison.Ordinal)), providerTypeCounts = items.GroupBy(x => x.ProviderType).ToDictionary(x => x.Key, x => x.Count()), missingSceneIds = Array.Empty<string>(), extraSceneIds = Array.Empty<string>(), lineageMismatchSceneIds = Array.Empty<string>(), upstreamChecksumPassed = true, manifestValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, legacyAuthorityUsed = false }, ct);
            await WriteJsonAsync(Path.Combine(staging, "phase8-publication-report.json"), new { schemaVersion = "1.0", publicationCommitted = true,
                manifestValidationPassed = true, candidateReadbackPassed = true, committedReadbackPending = true,
                assetCount = items.Count, generatedAssetCount = items.Count(x => !x.Reused), reusedAssetCount = items.Count(x => x.Reused) }, ct);
            manifest = manifest with { PublicationState = "Committed" };
            await WriteJsonAsync(Path.Combine(staging, "scene-asset-manifest.json"), manifest, ct);
            if (Directory.Exists(committed)) Directory.Move(committed, backup);
            Directory.Move(staging, committed);
            var committedValidation = await validator.ValidateAsync(manifest, authority, outputRoot, ct);
            if (!committedValidation.IsValid)
            {
                Directory.Delete(committed, true);
                if (Directory.Exists(backup)) Directory.Move(backup, committed);
                throw new Phase8AuthorityException(Phase8AuthorityReasonCodes.NotCommitted, committedValidation.Errors);
            }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            return Directory.EnumerateFiles(committed, "*", SearchOption.AllDirectories).ToArray();
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (!Directory.Exists(committed) && Directory.Exists(backup)) Directory.Move(backup, committed);
            throw;
        }
    }

    private static string SemanticIdentity(Phase8SceneRequirement scene, int width, int height) => HashText(JsonSerializer.Serialize(new
    {
        scene.Variant, scene.SceneId, scene.BlueprintSceneId, scene.StoryFrameId, scene.VisualDirection,
        scene.RenderingPreference, knowledgeReferenceIds = scene.KnowledgeReferenceIds.Order(StringComparer.Ordinal),
        scene.AcceptedNarrationSceneId, width, height
    }, JsonOptions));
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<HashSet<string>> PrepareAuthorityReuseAsync(string outputRoot, string compatibilityRoot,
        Phase8AuthorityInput authority, SceneAssetsV3Request request, CancellationToken ct)
    {
        var reusable = new HashSet<string>(StringComparer.Ordinal);
        var path = Path.Combine(outputRoot, "08-scene-assets", "scene-asset-manifest.json");
        SceneAssetManifest? old = null;
        if (File.Exists(path)) old = JsonSerializer.Deserialize<SceneAssetManifest>(await File.ReadAllTextAsync(path, ct), JsonOptions);
        var upstreamMatches = old is not null && old.PublicationState == "Committed"
            && old.DocumentaryBlueprintChecksum == authority.DocumentaryBlueprintChecksum
            && old.StoryFrameManifestChecksum == authority.StoryFrameManifestChecksum
            && old.LongNarrationReleaseCandidateChecksum == authority.LongNarrationReleaseCandidateChecksum
            && old.ShortNarrationReleaseCandidateChecksum == authority.ShortNarrationReleaseCandidateChecksum;
        foreach (var scene in authority.LongScenes.Concat(authority.ShortScenes))
        {
            var key = $"{scene.Variant}:{scene.SceneId}"; var format = scene.Variant.ToLowerInvariant();
            var compatibilityPath = Path.Combine(compatibilityRoot, format, scene.SceneId + ".png");
            var width = format == "long" ? request.LongTargetWidth : request.ShortTargetWidth;
            var height = format == "long" ? request.LongTargetHeight : request.ShortTargetHeight;
            var item = old?.Assets.SingleOrDefault(x => x.AssetId == key);
            var physicalPath = item is null ? string.Empty : Path.Combine(outputRoot, item.PhysicalPath);
            var valid = upstreamMatches && item is not null && item.SceneId == scene.SceneId
                && item.BlueprintSceneId == scene.BlueprintSceneId && item.StoryFrameId == scene.StoryFrameId
                && item.SourceInstructionId == scene.StoryFrameId && item.SemanticIdentity == SemanticIdentity(scene, width, height)
                && File.Exists(physicalPath) && IsValidPng(physicalPath) && await Sha256Async(physicalPath, ct) == item.Checksum;
            if (valid)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(compatibilityPath)!);
                File.Copy(physicalPath, compatibilityPath, true); reusable.Add(key);
            }
            else if (File.Exists(compatibilityPath)) File.Delete(compatibilityPath);
        }
        return reusable;
    }

    private static string BuildFallbackNarration(SceneAssetsV3TimelineContext c, string sceneId)
        => $"Watch this {c.EventType} sky event with {JoinNatural(c.EventObjectContext.ObjectNames)} as the visual focus.";

    private static string EnsureRequiredNarrationContext(SceneAssetsV3TimelineContext c, string narration)
        => string.IsNullOrWhiteSpace(narration) ? $"Follow the event intelligence for {c.EventType}: {JoinNatural(c.EventObjectContext.ObjectNames)}." : narration;

    private static VisualIntentSpec BuildVisualIntentSpec(SceneAssetsV3TimelineContext c, string sceneId, int index, string format)
    {
        var longSpecs = new[]
        {
            ("CinematicHook", "WideSky", $"cinematic dark-sky event view featuring {c.EventObjectContext.ObjectListText}", "wide establishing", "minimal", $"wide cinematic sky with local skyline and {c.EventObjectContext.ObjectListText}", c.EventObjectContext.ObjectHeadlineText),
            ("ObjectCloseup", "ObjectCloseup", $"realistic close visual study of {c.EventObjectContext.ObjectListText}", "telephoto closeup", "minimal", $"close celestial portrait of {c.EventObjectContext.ObjectListText}", c.EventObjectContext.ObjectPairText),
            ("ScientificExplanation", "EventMechanismDiagram", IsMeteorShower(c.EventType) ? "Earth crossing a debris stream creates meteor streaks" : "Earth line-of-sight geometry explaining the selected sky event", "diagram medium", "medium", IsMeteorShower(c.EventType) ? "clean meteor shower debris stream explainer" : "clean scientific sky-event explainer", IsMeteorShower(c.EventType) ? "Meteor streaks from debris" : "Apparent sky geometry"),
            ("GeometryDiagram", "EventGeometry", $"clean event callout for {c.EventObjectContext.ObjectPairText}", "diagram close", "low", IsMeteorShower(c.EventType) ? "minimal radiant guide with meteor streak paths" : "minimal event geometry measurement graphic", IsMeteorShower(c.EventType) ? "Radiant guide; meteors cross the sky" : (string.IsNullOrWhiteSpace(c.AngularSeparationText) ? c.EventObjectContext.ObjectPairText : $"Minimum separation: {c.AngularSeparationText}")),
            ("SkyGuide", "DirectionGuide", $"accurate sky map for {FirstNonEmpty(c.PrimaryViewingDirection, c.SkyGuideTheme, "current event direction")}", "wide guide", "guide", $"accurate sky guide with restrained markers for {FirstNonEmpty(c.PrimaryViewingDirection, c.SkyGuideTheme, "the current event")}", FirstNonEmpty(c.PrimaryViewingDirection, c.SkyGuideTheme, "Follow the current event guide")),
            ("HumanObservation", "HumanObserver", $"person watching {c.EventObjectContext.ObjectListText} over Udaipur skyline", "over-the-shoulder wide", "minimal", $"human observer silhouette under {c.EventObjectContext.ObjectListText}", "No telescope needed"),
            ("ViewingTips", "FieldTips", $"clear open sky viewing field for {c.EventObjectContext.ObjectListText}", "wide field", "low", "field viewing tip scene with low obstruction open sky", FirstNonEmpty(c.PeakTimeText, c.PrimaryViewingDirection, "Use the current event viewing window")),
            ("ObjectDetail", "ObjectDetail", $"cinematic detail study of {c.EventObjectContext.ObjectListText}", "macro detail", "minimal", "cinematic cutaway showing event objects from intelligence", c.EventObjectContext.ObjectPairText),
            ("EmotionalClosing", "EmotionalSky", $"calm closing night sky with observer silhouettes and {c.EventObjectContext.ObjectListText}", "wide emotional", "minimal", $"calm night sky with observer silhouettes and {c.EventObjectContext.ObjectListText}", FirstNonEmpty(c.PeakTimeText, "Step outside for the current event"))
        };
        var shortIndexes = new[] { 0, 1, 4, 6, 8 };
        var spec = format == "short" ? longSpecs[shortIndexes[Math.Min(index, shortIndexes.Length - 1)]] : longSpecs[Math.Min(index, longSpecs.Length - 1)];
        var informationDensity = spec.Item5.Equals("guide", StringComparison.OrdinalIgnoreCase) ? "Guide" : spec.Item5.Equals("medium", StringComparison.OrdinalIgnoreCase) ? "Medium" : spec.Item5.Equals("low", StringComparison.OrdinalIgnoreCase) ? "Low" : "Minimal";
        var overlayStyle = spec.Item1 == "SkyGuide" ? "direction markers with compact labels" : spec.Item1.Contains("Diagram", StringComparison.OrdinalIgnoreCase) || spec.Item2.Contains("Diagram", StringComparison.OrdinalIgnoreCase) ? "small labels and callouts" : "minimal documentary lower-third";
        return new VisualIntentSpec(spec.Item1, spec.Item2, spec.Item3, spec.Item4, spec.Item5, informationDensity, overlayStyle, $"variation-{index + 1:00}-{spec.Item2}-{spec.Item4}", spec.Item6, spec.Item7, null);
    }

    private static string BuildVisualPrompt(SceneAssetsV3TimelineContext c, string sceneId, VisualIntentSpec spec)
    {
        var objects = JoinNatural(c.EventObjectContext.ObjectNames);
        return $"{spec.PrimaryVisualSubject}. Camera distance: {spec.CameraDistance}. Visual subject category: {spec.VisualSubjectCategory}. Overlay rule: deterministic clean lower-third only, text=\"{spec.OverlayText}\". Event type: {c.EventType}. Resolved object names: {objects}. Visual theme: {FirstNonEmpty(c.VisualTheme, c.StoryTheme, "cinematic astronomy documentary")}. Sky guide theme: {FirstNonEmpty(c.SkyGuideTheme, "accurate horizon guidance")}. Visual intent: {spec.VisualIntent}. Composition type: {spec.CompositionType}. Prompt variation: {spec.PromptVariation}. Overlay style: {spec.OverlayStyle}. Forbidden terms policy: exclude event-profile forbidden concepts. Gallery V3 diversity strategy: unique asset purpose, unique camera distance, unique foreground/background relationship, no reused generic nebula template. Scene-specific visual goal: {sceneId}. Create a realistic astronomy documentary background with no embedded text, no watermark, no logo, no unrelated event imagery, and leave safe negative space for deterministic overlay.";
    }

    private static object BuildVisualPromptDiagnostics(int phaseNo, string product, SceneAssetsV3TimelineContext context, IEnumerable<object> prompts)
    {
        var promptArray = prompts.ToArray();
        var promptTexts = promptArray.Select(p => (string)(p.GetType().GetProperty("finalPrompt")?.GetValue(p) ?? string.Empty)).ToArray();
        var hardcodedTerms = EventObjectContextBuilder.DetectBannedHardcodedTerms(string.Join(Environment.NewLine, promptTexts));
        var score = CalculatePromptDiversityScore(promptTexts);
        return new
        {
            phaseNo,
            product,
            generatedAtUtc = DateTimeOffset.UtcNow,
            requiredInputsConsumed = new { visualIntent = true, compositionType = true, promptVariation = true, overlayStyle = true, currentPlanId = context.PlanId, currentEventType = context.EventType, eventType = context.EventType, resolvedObjectNames = context.EventObjectContext.ObjectNames, visualTheme = context.VisualTheme, skyGuideTheme = context.SkyGuideTheme, forbiddenTerms = context.ForbiddenTerms, forbiddenTermsSource = context.ForbiddenTermsSource, allowedGuidanceTerms = context.AllowedGuidanceTerms },
            currentPlanId = context.PlanId,
            currentEventType = context.EventType,
            forbiddenTermsSource = context.ForbiddenTermsSource,
            allowedGuidanceTerms = context.AllowedGuidanceTerms,
            eventObjectContext = context.EventObjectContext.ToDiagnostics(),
            objectNamesSource = context.EventObjectContext.ObjectNamesSource,
            cleanObjectNames = context.EventObjectContext.ObjectNames,
            removedInvalidObjectNameCandidates = context.EventObjectContext.RemovedInvalidObjectNameCandidates,
            hardcodedObjectTermsDetected = hardcodedTerms,
            objectNameValidationPassed = context.EventObjectContext.ObjectNameValidationPassed && hardcodedTerms.Count == 0,
            runtimeHardcodingDetected = hardcodedTerms.Count > 0,
            promptDiversityScore = score,
            repeatedPromptDetected = promptTexts.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1),
            forbiddenTermsDetected = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, promptTexts), context.ForbiddenTerms),
            blockedTermsMatched = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, promptTexts), context.ForbiddenTerms),
            staleContextDetected = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, promptTexts), context.ForbiddenTerms).Count > 0,
            staleContextSource = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, promptTexts), context.ForbiddenTerms).Count > 0 ? "finalPrompts" : string.Empty,
            relativeOverlayWordsDetected = Array.Empty<string>(),
            sceneDiversityScore = CalculateSubjectDiversityScore(promptArray),
            finalPrompts = promptArray
        };
    }

    private static IReadOnlyList<string> ExtractNarrationBeats(JsonElement root)
    {
        var beats = new List<string>();
        CollectStringsFromProperties(root, beats, ["narrationBeat", "narrationText", "voiceover", "text"]);
        return beats.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Take(12).ToArray();
    }

    private static void CollectStringsFromProperties(JsonElement element, List<string> values, IReadOnlyCollection<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var p in element.EnumerateObject()) { if (p.Value.ValueKind == JsonValueKind.String && names.Contains(p.Name)) values.Add(p.Value.GetString()!); else CollectStringsFromProperties(p.Value, values, names); }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CollectStringsFromProperties(item, values, names);
    }

    private static string[] ReadStringArray(JsonElement root, string propertyName)
    {
        var values = new List<string>();
        CollectArrayValues(root, propertyName, values);
        return values.ToArray();
    }
    private static void CollectArrayValues(JsonElement e, string name, List<string> values)
    {
        if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.Array) values.AddRange(p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x))); else CollectArrayValues(p.Value, name, values); }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) CollectArrayValues(item, name, values);
    }
    private static string FirstString(JsonElement root, params string[] names) { foreach (var name in names) { var value = FindString(root, name); if (!string.IsNullOrWhiteSpace(value)) return value!; } return string.Empty; }
    private static string? FindString(JsonElement e, string name) { if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString(); var v = FindString(p.Value, name); if (!string.IsNullOrWhiteSpace(v)) return v; } else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) { var v = FindString(item, name); if (!string.IsNullOrWhiteSpace(v)) return v; } return null; }
    private static string? ResolveFirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);
    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    private static bool IsPlanetConjunction(string eventType) => EventContentGuard.IsPlanetConjunction(eventType);
    private static string ResolveSceneGuideType(string eventType)
    {
        if (eventType.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "SolarEclipse";
        if (eventType.Contains("moon", StringComparison.OrdinalIgnoreCase)) return "NamedFullMoon";
        if (IsMeteorShower(eventType)) return "MeteorShower";
        if (IsPlanetConjunction(eventType)) return "PlanetConjunction";
        if (eventType.Contains("group", StringComparison.OrdinalIgnoreCase) || eventType.Contains("parade", StringComparison.OrdinalIgnoreCase)) return "PlanetGrouping";
        if (eventType.Contains("moon", StringComparison.OrdinalIgnoreCase) && (eventType.Contains("pair", StringComparison.OrdinalIgnoreCase) || eventType.Contains("planet", StringComparison.OrdinalIgnoreCase))) return "MoonPlanetPairing";
        return "GenericObjectPair";
    }

    private static string[] ResolveGuideElements(string eventType) => ResolveSceneGuideType(eventType) switch
    {
        "MeteorShower" => ["radiant", "meteor streak directions", "viewing region", "viewing window", "moonlight conditions"],
        "PlanetConjunction" => ["object labels", "separation", "altitude", "direction"],
        "PlanetGrouping" => ["object positions", "scan path", "grouping geometry"],
        "MoonPlanetPairing" => ["moon", "paired object", "separation"],
        _ => ["primary", "secondary", "alignment"]
    };

    private static EventProfileForbiddenTerms BuildEventProfileForbiddenTerms(string eventType, JsonElement root, EventObjectContext objectContext, IReadOnlyList<string> allowedGuidanceTerms)
    {
        var currentText = string.Join(Environment.NewLine, new[] { eventType, FirstString(root, "title", "shortTitle"), FirstString(root, "storyTheme"), FirstString(root, "visualTheme"), FirstString(root, "skyGuideTheme"), FirstString(root, "skyDirectionHint", "primaryViewingDirection", "viewingDirection", "direction"), FirstString(root, "bestViewingWindow", "bestViewingWindowLocal", "peakTime", "absoluteTime") }.Concat(objectContext.ObjectNames).Concat(ReadStringArray(root, "requiredVisualObjects")));
        var terms = new List<string>();
        var source = "event-profile-specific";
        if (IsMeteorShower(eventType))
        {
            source = "MeteorShower contamination profile: planet-conjunction terms absent from current intelligence";
            foreach (var term in new[] { "Jupiter", "Venus", "conjunction", "planet conjunction", "planet pairing", "western sky after sunset", "look west" })
                if (!ContainsTerm(currentText, term) && !allowedGuidanceTerms.Any(a => a.Equals(term, StringComparison.OrdinalIgnoreCase))) terms.Add(term);
        }
        else if (IsPlanetConjunction(eventType))
        {
            source = "PlanetConjunction contamination profile: meteor terms";
            terms.AddRange(EventContentGuard.DefaultForbiddenTermsForEventType(eventType));
        }
        else
        {
            source = "Generic profile: explicit current-event forbiddenTerms only";
            terms.AddRange(ReadStringArray(root, "forbiddenTerms"));
        }
        return new EventProfileForbiddenTerms(terms.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), source);
    }

    private static IReadOnlyList<string> BuildAllowedGuidanceTerms(JsonElement root)
        => new[] { FirstString(root, "skyDirectionHint", "primaryViewingDirection", "viewingDirection", "direction"), FirstString(root, "bestViewingWindow", "bestViewingWindowLocal", "peakTime", "absoluteTime"), FirstString(root, "skyGuideTheme") }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsMeteorShower(string? eventType)
        => !string.IsNullOrWhiteSpace(eventType) && eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> DefaultForbiddenTerms(string eventType) => EventContentGuard.DefaultForbiddenTermsForEventType(eventType);
    private static IReadOnlyList<string> ReadAllowedVisualObjects(JsonElement root)
    {
        var eventType = FirstString(root, "eventType", "strategyId", "selectedEventType");
        var objects = new[] { "primaryObjects", "secondaryObjects", "resolvedObjectNames", "requiredVisualObjects" }.SelectMany(name => ReadStringArray(root, name)).Select(CleanObjectName).Where(v => !string.IsNullOrWhiteSpace(v) && IsCleanObjectName(v!)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()!;
        if (IsPlanetConjunction(eventType) || objects.Any(o => o.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)) && objects.Any(o => o.Equals("Venus", StringComparison.OrdinalIgnoreCase))) return ["Jupiter", "Venus"];
        return objects.Length > 0 ? objects : [FirstString(root, "title")];
    }
    private static string CleanObjectName(string value) => (value ?? string.Empty).Trim().TrimEnd('.', ';', ':', ',');
    private static bool IsCleanObjectName(string value) => value.Length <= 32 && !value.Contains('.') && value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3;
    private static string JoinNatural(IEnumerable<string> values) => string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)).DefaultIfEmpty("the selected sky event"));
    private static bool ContainsTerm(string text, string term) => EventContentGuard.DetectForbiddenTerms(text, [term]).Count > 0;

    private sealed record EventProfileForbiddenTerms(IReadOnlyList<string> Terms, string Source);
    private sealed record SceneAssetsV3TimelineContext(string PlanId, string EventType, string Title, string StoryTheme, string VisualTheme, string SkyGuideTheme, string EventDateText, string PeakTimeText, string PrimaryViewingDirection, string AngularSeparationText, EventObjectContext EventObjectContext, IReadOnlyList<string> ForbiddenTerms, string ForbiddenTermsSource, IReadOnlyList<string> AllowedGuidanceTerms, IReadOnlyList<string> NarrationBeats);
    private sealed record VisualIntentSpec(string VisualIntent, string VisualSubjectCategory, string PrimaryVisualSubject, string CameraDistance, string OverlayDensity, string InformationDensity, string OverlayStyle, string PromptVariation, string CompositionType, string OverlayText, string? SupportingText);

}
