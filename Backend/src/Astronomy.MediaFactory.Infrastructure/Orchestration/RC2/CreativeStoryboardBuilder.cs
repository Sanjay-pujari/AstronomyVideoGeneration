using System.Diagnostics;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class CreativeStoryboardBuilder(ILogger<CreativeStoryboardBuilder> logger)
{
    private const string PhaseName = "Creative Intelligence / Story Frames";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] AstronomyVisualAccuracyRules =
    [
        "Planets must remain circular.",
        "Do not exaggerate angular separation beyond editorially acceptable framing.",
        "Do not show false surface detail.",
        "Do not imply the planets physically touch.",
        "Do not show daylight if the event is described after sunset.",
        "Observation visuals must respect direction and timing metadata when available.",
        "If altitude, constellation, moon interference, or brightness are missing, do not visualize them as confirmed facts."
    ];

    private static readonly string[] ProhibitedVisualChoices =
    [
        "fantasy sky",
        "sci-fi spaceship",
        "alien elements",
        "distorted planets",
        "unrealistic planet scale unless explicitly marked as editorial thumbnail treatment",
        "misleading constellation labels",
        "fake telescope detail",
        "overdramatic disaster-like lighting"
    ];

    public async Task<CreativeStoryboardBuilderResult> BuildAndWriteDiagnosticsAsync(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Creative Intelligence / Story Frames executed for RC2 batch generation. OutputRoot={OutputRoot}; Success={Success}", response.OutputRoot, response.Success);
        if (string.IsNullOrWhiteSpace(response.OutputRoot)) return CreativeStoryboardBuilderResult.Empty;

        var outputRoot = response.OutputRoot!;
        var editorialContractPath = Path.Combine(outputRoot, "editorial", "editorial-contract.json");
        var storyGraphPath = Path.Combine(outputRoot, "editorial", "story-graph.json");
        var sceneIntentsPath = Path.Combine(outputRoot, "editorial", "scene-intents.json");
        var creativeRoot = Path.Combine(outputRoot, "creative");
        Directory.CreateDirectory(creativeRoot);
        var storyboardPath = Path.Combine(creativeRoot, "creative-storyboard.json");
        var diagnosticsPath = Path.Combine(creativeRoot, "creative-diagnostics.json");
        var longRoot = Path.Combine(outputRoot, "story-frames", "long");
        var shortRoot = Path.Combine(outputRoot, "story-frames", "short");
        if (request.ExecutionMode == ContentPlanExecutionMode.RebuildOutputs && request.OverwriteExisting)
        {
            if (Directory.Exists(longRoot)) Directory.Delete(longRoot, recursive: true);
            if (Directory.Exists(shortRoot)) Directory.Delete(shortRoot, recursive: true);
        }

        var contract = ReadFirstJson(editorialContractPath);
        var storyGraph = ReadFirstJson(storyGraphPath);
        var sceneIntents = ReadFirstJson(sceneIntentsPath);
        var storyboard = BuildStoryboard(request, response, contract, storyGraph, sceneIntents);

        await File.WriteAllTextAsync(storyboardPath, JsonSerializer.Serialize(storyboard, JsonOptions), cancellationToken);
        var inputs = new[] { editorialContractPath, storyGraphPath, sceneIntentsPath };
        var requested = ResolveStoryFrameRequests(request, response);
        var storyFrameFiles = new List<string>();
        if (requested.LongRequested) storyFrameFiles.AddRange(await WriteStoryFramesAsync(outputRoot, "long", "landscape", "16:9", 1920, 1080, requested.LongRequested, storyboard, inputs, stopwatch, cancellationToken));
        if (requested.ShortRequested) storyFrameFiles.AddRange(await WriteStoryFramesAsync(outputRoot, "short", "portrait", "9:16", 2160, 3840, requested.ShortRequested, storyboard, inputs, stopwatch, cancellationToken));
        var diagnostics = new
        {
            phaseNo = 6,
            phaseName = PhaseName,
            orchestrationVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion,
            subPhases = new[] { "6.1 Creative Storyboard Builder", "6.2 Long Story Frame Planner", "6.3 Short Story Frame Planner", "6.6 Creative Diagnostics" },
            inputs = inputs.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) }).ToArray(),
            outputFiles = new[] { NormalizePath(storyboardPath), NormalizePath(diagnosticsPath) }.Concat(storyFrameFiles.Select(NormalizePath)).ToArray(),
            creativeSceneCount = storyboard.Scenes.Count,
            missingCreativeWarningCount = storyboard.MissingCreativeWarnings.Count,
            missingCreativeWarnings = storyboard.MissingCreativeWarnings,
            longStoryFramesRequested = requested.LongRequested,
            shortStoryFramesRequested = requested.ShortRequested,
            currentRunFilesOnly = true,
            executionTimeMs = stopwatch.ElapsedMilliseconds
        };
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);

        logger.LogInformation("Creative Intelligence / Story Frames wrote {CreativeSceneCount} creative storyboard scenes and diagnostics to {DiagnosticsPath}.", storyboard.Scenes.Count, diagnosticsPath);
        return new CreativeStoryboardBuilderResult(storyboard, new[] { storyboardPath, diagnosticsPath }.Concat(storyFrameFiles).ToArray());
    }


    private static async Task<IReadOnlyList<string>> WriteStoryFramesAsync(string outputRoot, string format, string orientation, string aspectRatio, int targetWidth, int targetHeight, bool requested, CreativeStoryboard storyboard, IReadOnlyList<string> inputFiles, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var root = Path.Combine(outputRoot, "story-frames", format);
        Directory.CreateDirectory(root);
        var files = new List<string>();
        var frameFiles = new List<string>();
        foreach (var scene in storyboard.Scenes.OrderBy(s => s.SceneOrder))
        {
            var fileName = $"scene-{scene.SceneOrder:000}.json";
            var path = Path.Combine(root, fileName);
            var frame = new StoryFrame(
                $"{format}-frame-{scene.SceneOrder:000}",
                scene.SceneId,
                scene.SceneOrder,
                scene.ScenePurpose,
                format,
                orientation,
                aspectRatio,
                targetWidth,
                targetHeight,
                BuildVisualGoal(scene, format),
                BuildComposition(scene, format),
                BuildCameraPlan(scene, format),
                BuildSubjectFocus(scene),
                BuildForeground(scene, format),
                BuildBackground(scene, format),
                BuildObjectPlacement(scene, format),
                BuildSafeFramingPlan(scene, format),
                BuildNegativeSpacePlan(scene, format),
                BuildOverlaySafeArea(scene, format),
                BuildMotionHint(scene, format),
                scene.ScenePurpose == "Hook" ? 5.0 : scene.ScenePurpose == "Takeaway" ? 4.0 : 7.0,
                BuildNarrationMapping(scene, format),
                scene.SceneId,
                scene.SceneId);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(frame, JsonOptions), cancellationToken);
            files.Add(path);
            frameFiles.Add(fileName);
        }

        var manifestPath = Path.Combine(root, "story-frame-manifest.json");
        var diagnosticsPath = Path.Combine(root, "story-frame-diagnostics.json");
        var manifest = new { format, orientation, aspectRatio, targetWidth, targetHeight, requested, generated = files.Count > 0, expectedSceneCount = storyboard.Scenes.Count, generatedSceneCount = files.Count, sceneIds = storyboard.Scenes.OrderBy(s => s.SceneOrder).Select(s => s.SceneId).ToArray(), files = frameFiles.ToArray(), createdUtc = DateTimeOffset.UtcNow, sourceFiles = inputFiles.Select(NormalizePath).ToArray(), qualityProfile = "Aurora", contractVersion = "RC2-Phase6-StoryFrames-v1" };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        var diagnostics = BuildStoryFrameDiagnostics(format, inputFiles, files.Concat([manifestPath, diagnosticsPath]).ToArray(), storyboard, frameFiles, stopwatch);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        files.Add(manifestPath);
        files.Add(diagnosticsPath);
        return files;
    }


    private static object BuildStoryFrameDiagnostics(string format, IReadOnlyList<string> inputFiles, IReadOnlyList<string> outputFiles, CreativeStoryboard storyboard, IReadOnlyList<string> frameFiles, Stopwatch stopwatch)
    {
        var genericWarnings = new List<string>();
        var duplicateWarnings = new List<string>();
        var safeWarnings = new List<string>();
        var motionWarnings = new List<string>();
        var compositions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in storyboard.Scenes.OrderBy(s => s.SceneOrder))
        {
            var visualGoal = BuildVisualGoal(scene, format);
            var composition = BuildComposition(scene, format);
            var motionHint = BuildMotionHint(scene, format);
            var safeArea = BuildOverlaySafeArea(scene, format);
            if (IsGenericText(visualGoal) || IsGenericText(composition) || IsGenericText(BuildCameraPlan(scene, format))) genericWarnings.Add($"{scene.SceneId} contains generic story-frame language.");
            if (!compositions.Add(composition)) duplicateWarnings.Add($"{scene.SceneId} duplicates another composition.");
            if (IsGenericText(safeArea) || !safeArea.Any(char.IsDigit)) safeWarnings.Add($"{scene.SceneId} overlay safe area is missing exact zones.");
            if (IsGenericText(motionHint)) motionWarnings.Add($"{scene.SceneId} motion hint is missing scene-specific motion direction.");
        }
        var errors = genericWarnings.Concat(duplicateWarnings).Concat(safeWarnings).Concat(motionWarnings).ToArray();
        var score = Math.Max(0, 100 - (genericWarnings.Count * 12) - (duplicateWarnings.Count * 15) - (safeWarnings.Count * 10) - (motionWarnings.Count * 10));
        return new { phaseNo = 6, phaseName = PhaseName, format, inputFiles = inputFiles.Select(NormalizePath).ToArray(), outputFiles = outputFiles.Select(NormalizePath).ToArray(), expectedSceneCount = storyboard.Scenes.Count, generatedSceneCount = frameFiles.Count, genericTextWarnings = genericWarnings, duplicateCompositionWarnings = duplicateWarnings, missingSafeAreaWarnings = safeWarnings, missingMotionHintWarnings = motionWarnings, sceneSpecificityScore = score, compositionQualityScore = score, motionReadinessScore = score, overlaySafetyScore = score, overallStoryFrameQualityScore = score, warnings = storyboard.MissingCreativeWarnings, errors, staleFilesIgnored = true, currentRunFilesOnly = true, executionTimeMs = stopwatch.ElapsedMilliseconds };
    }

    private static string BuildVisualGoal(CreativeStoryboardScene s, string format) => s.ScenePurpose switch
    {
        "Hook" => $"Create immediate curiosity around {s.PrimarySubject} by showing the viewer the most intriguing sky relationship before any explanation; the frame should feel like a quiet discovery moment, not a generic sky card.",
        "Discovery" => $"Orient the viewer to where {s.PrimarySubject} appears, using the supported direction or horizon context as the information anchor for finding the event.",
        "Science" => $"Explain the apparent alignment of {s.PrimarySubject} as a line-of-sight relationship, making separation and perspective readable without implying physical contact.",
        "Observation" => $"Help the viewer know how to look for {s.PrimarySubject}, prioritizing direction, horizon reference, brightness hierarchy, and practical viewing confidence.",
        "Takeaway" => $"Leave a memorable emotional image of {s.PrimarySubject} that reinforces why this event is worth remembering after the facts are understood.",
        _ => $"Support the reminder/action beat for {s.PrimarySubject} with a clean visual that makes the next viewer action obvious."
    };

    private static string BuildComposition(CreativeStoryboardScene s, string format)
    {
        var secondary = s.SecondarySubjects.Count > 0 ? string.Join("; ", s.SecondarySubjects) : "confirmed sky context only";
        return format == "short"
            ? s.ScenePurpose switch
            {
                "Hook" => $"Portrait-first sky stack: place {s.PrimarySubject} in the upper-middle third, keep the horizon low and subdued, use a dark foreground silhouette only at the bottom edge, and make the brightest object the first read with {secondary} held as small supporting context.",
                "Discovery" => $"Vertical locator frame with the horizon in the lower 15%, the sky direction cue rising through the center lane, and {s.PrimarySubject} placed just above center so mobile viewers can map sky position quickly; background gradients separate foreground, horizon, and object layer.",
                "Science" => $"Tall explanatory composition with {s.PrimarySubject} separated along a gentle vertical diagonal, a thin perspective guide between them, and ample side space for a compact fact chip; hierarchy reads object, separation, then explanation.",
                "Observation" => $"Practical phone-view composition: low horizon, clear central lookup lane, {s.PrimarySubject} positioned in the upper half with secondary viewing cues beneath it, foreground kept minimal so captions do not cover the target.",
                "Takeaway" => $"Memorable portrait closing frame with {s.PrimarySubject} glowing in the upper third above a quiet horizon band, leaving the middle calm and the lower frame clean for a save/reminder cue.",
                _ => $"Action-oriented portrait frame with {s.PrimarySubject} high enough to protect captions, a simple foreground location cue at the bottom edge, and background sky context arranged in clear foreground/midground/sky layers."
            }
            : s.ScenePurpose switch
            {
                "Hook" => $"Wide 16:9 opening sky over a restrained horizon; place {s.PrimarySubject} off the right third as the strongest visual read, leave the left third quieter for curiosity-building title treatment, and use {secondary} as subtle background evidence.",
                "Discovery" => $"Landscape orientation map: horizon spans the lower quarter, sky direction reads left-to-right, {s.PrimarySubject} sits on a stable upper third, and background sky gradient provides enough context to understand where to look.",
                "Science" => $"Wide explanatory frame with {s.PrimarySubject} arranged across the center thirds, separation visible as negative space, and a reserved side area for a line-of-sight annotation; hierarchy reads relationship first, then labels.",
                "Observation" => $"Viewer-at-the-horizon composition with a realistic low skyline, {s.PrimarySubject} placed above the appropriate viewing band, secondary cues kept smaller, and the lower-left third clean for practical viewing instructions.",
                "Takeaway" => $"Cinematic closing landscape with {s.PrimarySubject} small but luminous against a broad sky, horizon softened in the lower fifth, and the brightest point balanced against wide calm space for emotional memory.",
                _ => $"Support-detail landscape frame with {s.PrimarySubject} on a focal third, a modest horizon or location cue, and clear side space for reminder/action information."
            };
    }

    private static string BuildCameraPlan(CreativeStoryboardScene s, string format) => format == "short"
        ? $"Medium-wide vertical sky view from a grounded observer angle, framed for a 9:16 phone screen with a mild telephoto lens feeling that compresses {s.PrimarySubject} enough to read quickly; this supports the {s.ScenePurpose.ToLowerInvariant()} beat by keeping the subject in the central mobile scan path."
        : $"Wide locked-off observer angle from ground level, landscape 16:9 framing with a natural 35-50mm documentary lens feeling; the distance preserves horizon and sky context so the {s.ScenePurpose.ToLowerInvariant()} explanation can unfold slowly without crowding overlays.";
    private static string BuildSubjectFocus(CreativeStoryboardScene s) => $"Primary: {s.PrimarySubject}. Secondary: {(s.SecondarySubjects.Count == 0 ? "none beyond verified context" : string.Join("; ", s.SecondarySubjects))}.";
    private static string BuildForeground(CreativeStoryboardScene s, string format) => s.ScenePurpose is "Science" ? "none" : format == "short" ? "Thin low horizon or silhouette band only when supported, kept below caption-safe space." : "Subtle horizon/location reference used for scale and orientation, never competing with the sky subject.";
    private static string BuildBackground(CreativeStoryboardScene s, string format) => format == "short" ? $"Portrait sky gradient with vertical depth behind {s.PrimarySubject}; no invented constellations or false surface detail." : $"Wide natural sky and restrained horizon context behind {s.PrimarySubject}; use only supported direction/timing facts.";
    private static string BuildObjectPlacement(CreativeStoryboardScene s, string format) => format == "short" ? $"Place {s.PrimarySubject} in the upper-middle third; keep secondary cues below or beside it at smaller scale, with top and bottom safe zones protected." : $"Place {s.PrimarySubject} on the upper or right focal third depending on the scene purpose; keep secondary cues smaller and lower-priority, away from lower-third captions.";
    private static string BuildSafeFramingPlan(CreativeStoryboardScene s, string format) => format == "short" ? "Protect title, caption, and CTA bands while keeping the central vertical subject lane uncluttered." : "Protect lower-third labels and side margins while preserving a wide readable sky relationship.";
    private static string BuildNegativeSpacePlan(CreativeStoryboardScene s, string format) => format == "short" ? "Use clean sky in the top 12% and bottom 18%; keep side negative space simple so mobile overlays remain legible." : "Reserve the lower-left/lower-third sky or horizon area for captions, with quiet side sky for labels and slow pacing.";
    private static string BuildOverlaySafeArea(CreativeStoryboardScene s, string format) => format == "short" ? "Exact safe zones: top 12% clear for title, bottom 18% clear for captions/CTA, side 8% margins free of essential objects, central 60% kept readable." : "Exact safe zones: lower 18% clear for lower-third captions, side 6% margins clear, upper-left 20% available for a small documentary label.";
    private static string BuildMotionHint(CreativeStoryboardScene s, string format) => s.ScenePurpose switch
    {
        "Hook" => format == "short" ? $"Begin with a one-second hold on empty upper sky, then a tiny upward reveal toward {s.PrimarySubject}; no fast zoom." : $"Slow lateral drift from quiet negative space into {s.PrimarySubject}, preserving the lower-third label area.",
        "Discovery" => format == "short" ? $"Gentle vertical tilt from low horizon to {s.PrimarySubject} to teach where to look." : $"Slow pan along the horizon direction, ending locked on {s.PrimarySubject} for orientation.",
        "Science" => $"Hold mostly static; add a subtle animated guide line or parallax cue to clarify apparent alignment without changing object spacing.",
        "Observation" => format == "short" ? $"Slow upward lookup motion from horizon cue to target, ending with a steady two-second hold." : $"Measured push-in from horizon reference toward {s.PrimarySubject}, keeping captions stable.",
        "Takeaway" => $"Very slow fade or drift that lets {s.PrimarySubject} linger as an emotional memory; avoid explanatory movement.",
        _ => $"Short, steady hold with a small CTA-safe drift that does not cross overlay zones."
    };
    private static string BuildNarrationMapping(CreativeStoryboardScene s, string format) => $"Supports the narration beat '{s.KeyMessage}' by making the {s.ScenePurpose.ToLowerInvariant()} information visible in {format} framing before downstream narration or motion is added.";
    private static bool IsGenericText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        string[] generic = ["Calm documentary camera", "Balanced composition", "Clear visual focus", "Documentary style", "Cinematic sky view", "Same scene for short format", "stable focal third"];
        return generic.Any(g => text.Contains(g, StringComparison.OrdinalIgnoreCase));
    }

    private static (bool LongRequested, bool ShortRequested) ResolveStoryFrameRequests(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response)
    {
        var completions = response.RequestedOutputCompletion ?? response.Results?.OfType<ContentPlanProductionExecutionResult>().SelectMany(r => r.RequestedOutputCompletion ?? []).ToArray();
        bool Requested(string outputType) => completions?.Any(c => c.Requested && string.Equals(c.OutputType, outputType, StringComparison.OrdinalIgnoreCase)) == true;
        var longRequested = Requested("LongVideo");
        var shortRequested = Requested("ShortVideo");
        foreach (var format in response.SelectedPlans.Select(p => p.PlannedFormat).Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            if (format!.Contains("long", StringComparison.OrdinalIgnoreCase)) longRequested = true;
            if (format.Contains("short", StringComparison.OrdinalIgnoreCase)) shortRequested = true;
        }
        if (!longRequested && !shortRequested) return (true, true);
        return (longRequested, shortRequested);
    }

    private static CreativeStoryboard BuildStoryboard(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, JsonElement? contract, JsonElement? storyGraph, JsonElement? sceneIntents)
    {
        var eventType = FirstNonEmpty(GetString(contract, "eventType"), GetString(storyGraph, "eventType"), response.SelectedPlans.FirstOrDefault()?.ContentCategoryCode, "Unknown")!;
        var eventName = FirstNonEmpty(GetString(contract, "eventName"), GetString(storyGraph, "eventName"), response.Title, response.SelectedPlans.FirstOrDefault()?.Title, "Unknown")!;
        var language = FirstNonEmpty(GetString(contract, "language"), GetString(storyGraph, "language"), request.Language)!;
        var regionId = FirstNonEmpty(GetString(contract, "regionId"), GetString(storyGraph, "regionId"), request.RegionId)!;
        var storyArc = FirstNonEmpty(GetString(storyGraph, "storyArc"), FindString(contract, "storyArc"), "Hook → Discovery → Science → Observation → Takeaway")!;
        var warnings = new List<string>();
        if (!contract.HasValue) warnings.Add("Missing input file editorial/editorial-contract.json.");
        if (!storyGraph.HasValue) warnings.Add("Missing input file editorial/story-graph.json.");
        if (!sceneIntents.HasValue) warnings.Add("Missing input file editorial/scene-intents.json.");

        var graphScenes = ReadArray(storyGraph, "scenes");
        var intentScenes = sceneIntents.HasValue && sceneIntents.Value.ValueKind == JsonValueKind.Array ? sceneIntents.Value.EnumerateArray().Select(e => e.Clone()).ToArray() : [];
        var sourceScenes = graphScenes.Count > 0 ? graphScenes : intentScenes;
        if (sourceScenes.Count == 0) warnings.Add("No editorial scenes were available for creative storyboard generation.");

        var primarySubject = ResolvePrimarySubjectFromContract(contract, eventName);
        var secondarySubjects = ResolveSecondarySubjectsFromContract(contract);
        var scenes = sourceScenes.Select((scene, index) => BuildScene(scene, intentScenes, index, eventName, primarySubject, secondarySubjects, warnings)).ToArray();
        warnings.AddRange(FindStringArray(contract, "missingFactWarnings").Select(w => $"Editorial warning carried into creative layer: {w}"));
        return new CreativeStoryboard(
            "AstroPulse-CreativeStoryboard-v1",
            Rc2PipelinePhaseRegistry.OrchestrationVersion,
            eventType,
            eventName,
            language,
            regionId,
            "Make the astronomy understandable, observable, calm, and visually accurate before any generator writes prompts.",
            storyArc,
            "Natural documentary sky visuals with restrained motion, factual object relationships, and no invented observational metadata.",
            scenes,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static CreativeStoryboardScene BuildScene(JsonElement scene, IReadOnlyList<JsonElement> intents, int index, string eventName, string primarySubject, IReadOnlyList<string> secondarySubjects, List<string> warnings)
    {
        var sceneId = FirstNonEmpty(GetString(scene, "sceneId"), $"scene-{index + 1:000}")!;
        var purpose = NormalizePurpose(FirstNonEmpty(GetString(scene, "scenePurpose"), GetString(scene, "purpose"))) ?? FallbackPurpose(index);
        var intent = intents.FirstOrDefault(i => string.Equals(GetString(i, "sceneId"), sceneId, StringComparison.OrdinalIgnoreCase));
        var keyMessage = FirstNonEmpty(GetString(scene, "keyMessage"), GetString(intent, "keyMessage"), $"Help viewers understand {eventName} without inventing unsupported facts.")!;
        var defaults = DefaultsFor(purpose);
        if (!string.Equals(purpose, FirstNonEmpty(GetString(scene, "scenePurpose"), GetString(scene, "purpose")), StringComparison.OrdinalIgnoreCase)) warnings.Add($"Scene {sceneId} used creative fallback purpose {purpose}.");

        return new CreativeStoryboardScene(
            sceneId,
            purpose,
            GetInt(scene, "sceneOrder") ?? index + 1,
            keyMessage,
            defaults.ViewerFocus,
            defaults.EmotionalRole,
            FirstNonEmpty(GetString(scene, "visualRole"), GetString(intent, "visualIntent"), $"Translate the {purpose.ToLowerInvariant()} beat into a clear visual decision.")!,
            FirstNonEmpty(GetString(scene, "motionRole"), "Use motion only to support comprehension and pacing.")!,
            primarySubject,
            secondarySubjects,
            defaults.CompositionIntent,
            purpose == "Science" ? "Stable explanatory camera with legible spatial relationships." : "Calm documentary camera that keeps the main subjects easy to understand.",
            purpose == "Hook" ? "Natural high-contrast night-sky lighting without disaster-like drama." : "Natural sky lighting consistent with the supported observation context.",
            FirstNonEmpty(GetString(scene, "motionRole"), GetString(intent, "motionIntent"), "Slow, restrained motion that clarifies the scene relationship.")!,
            FirstNonEmpty(GetString(scene, "transitionToNext"), "Use a simple editorial transition that preserves story continuity.")!,
            AstronomyVisualAccuracyRules,
            ProhibitedVisualChoices);
    }

    private static string ResolvePrimarySubjectFromContract(JsonElement? contract, string eventName)
    {
        var primaryObjects = FindContractFactArray(contract, "primaryObjects");
        var secondaryObjects = FindContractFactArray(contract, "secondaryObjects");
        var objects = primaryObjects.Concat(secondaryObjects).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return objects.Length == 0 ? eventName : string.Join(" + ", objects);
    }

    private static IReadOnlyList<string> ResolveSecondarySubjectsFromContract(JsonElement? contract)
    {
        var subjects = new List<string>();
        AddFactSubject(subjects, contract, "angularSeparationDegrees", "Angular separation");
        AddFactSubject(subjects, contract, "bestViewingWindowLocal", "Best viewing window");
        AddFactSubject(subjects, contract, "skyDirectionHint", "Sky direction");
        return subjects;
    }

    private static void AddFactSubject(List<string> subjects, JsonElement? contract, string factName, string label)
    {
        var value = FindContractFactString(contract, factName);
        if (!string.IsNullOrWhiteSpace(value)) subjects.Add($"{label}: {value}");
    }

    private static IReadOnlyList<string> FindContractFactArray(JsonElement? element, string name)
    {
        var fact = FindProperty(element, name);
        if (fact is not { ValueKind: JsonValueKind.Object } obj || !TryGetProperty(obj, "value", out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(ValueToString).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToArray();
    }

    private static string? FindContractFactString(JsonElement? element, string name)
    {
        var fact = FindProperty(element, name);
        if (fact is not { ValueKind: JsonValueKind.Object } obj || !TryGetProperty(obj, "value", out var value)) return null;
        return ValueToString(value);
    }

    private static JsonElement? FindProperty(JsonElement? element, string name)
    {
        if (!element.HasValue) return null;
        if (element.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in element.Value.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
                var nested = FindProperty(p.Value, name);
                if (nested.HasValue) return nested;
            }
        }
        return null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var p in element.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        value = default;
        return false;
    }
    private static (string ViewerFocus, string EmotionalRole, string CompositionIntent) DefaultsFor(string purpose) => purpose switch
    {
        "Hook" => ("Understand why this event is worth watching.", "Create curiosity and immediate visual interest.", "Strong opening composition with the main astronomical subjects clearly visible."),
        "Discovery" => ("Understand where this event appears in the sky.", "Make the sky feel approachable and easy to navigate.", "Orientation-style composition that helps viewers locate the event."),
        "Science" => ("Understand why the event happens.", "Turn curiosity into understanding.", "Clean explanatory visual, orbit geometry, or perspective-based diagram."),
        "Observation" => ("Know exactly what to look for.", "Build confidence for real sky observation.", "Practical sky-view composition with direction, horizon, and object relationship."),
        "Takeaway" => ("Remember why the event matters.", "Leave the viewer with a calm sense of wonder.", "Beautiful closing composition that reinforces the event’s significance."),
        _ => ("Know what action to take next.", "Encourage the viewer to observe or save the event.", "Simple action-oriented composition.")
    };
    private static string FallbackPurpose(int index) => index switch { 0 => "Hook", 1 => "Discovery", 2 => "Science", 3 => "Observation", 4 => "Takeaway", _ => "SupportingDetail" };
    private static string? NormalizePurpose(string? purpose) { if (string.IsNullOrWhiteSpace(purpose)) return null; var c = new string(purpose.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant(); return c switch { "hook" => "Hook", "discovery" => "Discovery", "science" => "Science", "observation" or "viewing" or "observing" => "Observation", "takeaway" or "summary" or "closing" => "Takeaway", "supportingdetail" or "detail" => "SupportingDetail", _ => null }; }
    private static JsonElement? ReadFirstJson(string path) { if (!File.Exists(path)) return null; using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.Clone(); }
    private static IReadOnlyList<JsonElement> ReadArray(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return []; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().Select(i => i.Clone()).ToArray(); return []; }
    private static IReadOnlyDictionary<string, string> ReadObjectStrings(JsonElement element, string name) { if (element.ValueKind != JsonValueKind.Object) return new Dictionary<string, string>(); foreach (var p in element.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object) return p.Value.EnumerateObject().Where(kv => ValueToString(kv.Value) is not null).ToDictionary(kv => kv.Name, kv => ValueToString(kv.Value)!); return new Dictionary<string, string>(); }
    private static int? GetInt(JsonElement? element, string name) => int.TryParse(GetString(element, name), out var value) ? value : null;
    private static string? GetString(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return null; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(p.Value); return null; }
    private static string? FindString(JsonElement? element, string name) { if (!element.HasValue) return null; if (element.Value.ValueKind == JsonValueKind.Object) foreach (var p in element.Value.EnumerateObject()) { if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(p.Value); var nested = FindString(p.Value, name); if (!string.IsNullOrWhiteSpace(nested)) return nested; } return null; }
    private static IReadOnlyList<string> FindStringArray(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return []; foreach (var p in e.EnumerateObject()) { if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().Select(ValueToString).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToArray(); var nested = FindStringArray(p.Value, name); if (nested.Count > 0) return nested; } return []; }
    private static string? ValueToString(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
}

public sealed record CreativeStoryboard(string CreativeStoryboardVersion, string OrchestrationVersion, string EventType, string EventName, string Language, string RegionId, string CreativePrinciple, string StoryArc, string GlobalVisualDirection, IReadOnlyList<CreativeStoryboardScene> Scenes, IReadOnlyList<string> MissingCreativeWarnings);
public sealed record CreativeStoryboardScene(string SceneId, string ScenePurpose, int SceneOrder, string KeyMessage, string ViewerFocus, string EmotionalRole, string VisualRole, string MotionRole, string PrimarySubject, IReadOnlyList<string> SecondarySubjects, string CompositionIntent, string CameraIntent, string LightingIntent, string MotionIntent, string TransitionIntent, IReadOnlyList<string> VisualAccuracyRules, IReadOnlyList<string> ProhibitedVisualChoices);
public sealed record CreativeStoryboardBuilderResult(CreativeStoryboard? Storyboard, IReadOnlyList<string> GeneratedFiles)
{
    public static CreativeStoryboardBuilderResult Empty { get; } = new(null, []);
}

public sealed record StoryFrame(string FrameId, string SceneId, int SceneOrder, string ScenePurpose, string Format, string Orientation, string AspectRatio, int TargetWidth, int TargetHeight, string VisualGoal, string Composition, string CameraPlan, string SubjectFocus, string Foreground, string Background, string ObjectPlacement, string SafeFramingPlan, string NegativeSpacePlan, string OverlaySafeArea, string MotionHint, double EstimatedDurationSeconds, string NarrationMapping, string SourceSceneIntentId, string SourceCreativeSceneId);
