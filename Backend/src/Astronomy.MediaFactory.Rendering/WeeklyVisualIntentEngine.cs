using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed partial class WeeklyVisualIntentEngine : IWeeklyVisualIntentEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] RequiredInputFileNames =
    [
        "longform-narration.json",
        "shortform-narration.json",
        "audio-driven-final-render-timeline.json",
        "audio-driven-resolved-render-shot-plan.json",
        "weekly-production-asset-manifest.json",
        "narration-asset-map.json",
        "narration-timeline-map.json"
    ];

    private static readonly string[] InternalCelestialLookupKeys =
    [
        "Moon",
        "Saturn",
        "Venus",
        "Jupiter",
        "Mars",
        "Mercury",
        "DeepSkyObject"
    ];

    private readonly IPipelineRepository _repository;

    public WeeklyVisualIntentEngine(IPipelineRepository repository)
    {
        _repository = repository;
    }

    public async Task<WeeklyVisualIntentBuildResult> BuildAsync(Guid pipelineRunId, CancellationToken cancellationToken)
    {
        var run = await _repository.GetAsync(pipelineRunId, cancellationToken)
            ?? throw new KeyNotFoundException($"Pipeline run {pipelineRunId} was not found.");

        if (string.IsNullOrWhiteSpace(run.OutputFolder) || !Directory.Exists(run.OutputFolder))
            throw new DirectoryNotFoundException($"Pipeline run {pipelineRunId} output folder was not found: {run.OutputFolder ?? "<empty>"}.");

        var outputDirectory = run.OutputFolder!;
        var renderDirectory = Path.Combine(outputDirectory, "render");
        Directory.CreateDirectory(renderDirectory);

        var inputs = await LoadInputsAsync(outputDirectory, renderDirectory, cancellationToken);
        var assetIndex = BuildAssetIndex(inputs);
        var sourceSegments = ExtractSourceSegments(inputs);
        var beats = BuildBeats(sourceSegments, assetIndex).ToList();
        var shots = beats.Select(ToShot).ToList();
        var report = Validate(beats, inputs.MissingInputFileNames);

        var plan = new WeeklyVisualIntentPlan
        {
            PipelineRunId = pipelineRunId,
            Inputs = inputs.LoadedInputFileNames,
            Beats = beats
        };

        var shotPlan = new WeeklyVisualIntentShotPlan
        {
            PipelineRunId = pipelineRunId,
            Shots = shots
        };

        var planPath = Path.Combine(renderDirectory, "visual-intent-plan.json");
        var shotPlanPath = Path.Combine(renderDirectory, "visual-intent-shot-plan.json");
        var reportPath = Path.Combine(renderDirectory, "visual-intent-validation-report.json");

        await WriteJsonAsync(planPath, plan, cancellationToken);
        await WriteJsonAsync(shotPlanPath, shotPlan, cancellationToken);
        await WriteJsonAsync(reportPath, report, cancellationToken);

        return new WeeklyVisualIntentBuildResult(pipelineRunId, outputDirectory, planPath, shotPlanPath, reportPath, report);
    }

    private static async Task<WeeklyVisualIntentInputs> LoadInputsAsync(string outputDirectory, string renderDirectory, CancellationToken cancellationToken)
    {
        var documents = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        var loaded = new List<string>();
        var missing = new List<string>();

        foreach (var fileName in RequiredInputFileNames)
        {
            var path = ResolveInputPath(outputDirectory, renderDirectory, fileName);
            if (path is null)
            {
                missing.Add(fileName);
                continue;
            }

            try
            {
                documents[fileName] = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                loaded.Add(fileName);
            }
            catch (JsonException)
            {
                missing.Add(fileName);
            }
        }

        return new WeeklyVisualIntentInputs(documents, loaded, missing);
    }

    private static string? ResolveInputPath(string outputDirectory, string renderDirectory, string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(outputDirectory, fileName),
            Path.Combine(renderDirectory, fileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static WeeklyAssetIndex BuildAssetIndex(WeeklyVisualIntentInputs inputs)
    {
        var assets = new List<IndexedAsset>();
        if (inputs.Documents.TryGetValue("weekly-production-asset-manifest.json", out var manifest) && manifest is not null)
            CollectAssets(manifest, assets, null);
        if (inputs.Documents.TryGetValue("narration-asset-map.json", out var assetMap) && assetMap is not null)
            CollectAssets(assetMap, assets, null);
        if (inputs.Documents.TryGetValue("audio-driven-resolved-render-shot-plan.json", out var shotPlan) && shotPlan is not null)
            CollectAssets(shotPlan, assets, null);

        return new WeeklyAssetIndex(assets);
    }

    private static IReadOnlyCollection<SourceSegment> ExtractSourceSegments(WeeklyVisualIntentInputs inputs)
    {
        var segments = new List<SourceSegment>();
        AddSegments(inputs, "longform-narration.json", "longform", segments);
        AddSegments(inputs, "shortform-narration.json", "shortform", segments);

        if (segments.Count == 0)
        {
            AddSegments(inputs, "narration-timeline-map.json", "longform", segments);
            AddSegments(inputs, "audio-driven-final-render-timeline.json", "longform", segments);
        }

        return segments
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .GroupBy(x => x.StableKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Text.Length).First())
            .OrderBy(x => x.Form.Equals("shortform", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(x => x.Sequence)
            .ToList();
    }

    private static void AddSegments(WeeklyVisualIntentInputs inputs, string fileName, string form, List<SourceSegment> segments)
    {
        if (!inputs.Documents.TryGetValue(fileName, out var root) || root is null)
            return;

        var before = segments.Count;
        CollectTextSegments(root, form, segments, 0);
        for (var i = before; i < segments.Count; i++)
            segments[i] = segments[i] with { Sequence = i - before };
    }

    private static void CollectTextSegments(JsonNode? node, string form, List<SourceSegment> segments, int depth)
    {
        if (node is null || depth > 16)
            return;

        if (node is JsonObject obj)
        {
            var text = FirstString(obj, "text", "narration", "script", "voiceover", "line", "content", "sentence");
            if (!string.IsNullOrWhiteSpace(text) && IsNarrationLike(text))
            {
                segments.Add(new SourceSegment(
                    FormFromObject(obj, form),
                    segments.Count,
                    FirstString(obj, "id", "beatId", "segmentId", "sceneId") ?? $"beat-{segments.Count + 1}",
                    text!,
                    FirstDouble(obj, "startSeconds", "start", "startTime", "audioStartSeconds"),
                    FirstDouble(obj, "endSeconds", "end", "endTime", "audioEndSeconds"),
                    FirstDouble(obj, "durationSeconds", "duration", "audioDurationSeconds")));
                return;
            }

            foreach (var child in obj)
                CollectTextSegments(child.Value, form, segments, depth + 1);
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var child in array)
                CollectTextSegments(child, form, segments, depth + 1);
        }
    }

    private static IEnumerable<WeeklyVisualIntentBeat> BuildBeats(IReadOnlyCollection<SourceSegment> segments, WeeklyAssetIndex assetIndex)
    {
        var previousFamilies = new Queue<string>();
        var sequence = 0;

        foreach (var segment in segments)
        {
            var intent = ClassifyIntent(segment.Text, segment.Sequence, segment.Form);
            var objects = DetectObjects(segment.Text);
            var plan = SelectAssets(intent, objects, segment.Text, assetIndex, previousFamilies);
            var warnings = new List<string>();
            if (plan.Primary.Availability.Equals("requestedButUnavailable", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"No available {plan.Primary.AssetFamily} asset was found; prepared fallback request for the next provider phase.");

            yield return new WeeklyVisualIntentBeat
            {
                BeatId = string.IsNullOrWhiteSpace(segment.Id) ? $"{segment.Form}-{sequence + 1:000}" : segment.Id,
                Form = segment.Form,
                Sequence = sequence++,
                StartSeconds = segment.StartSeconds,
                EndSeconds = segment.EndSeconds,
                DurationSeconds = segment.DurationSeconds ?? (segment.EndSeconds - segment.StartSeconds),
                NarrationText = segment.Text,
                VisualIntent = intent,
                MentionedObjects = objects,
                Primary = plan.Primary,
                Secondary = plan.Secondary,
                Overlay = plan.Overlay,
                EditorialRationale = BuildRationale(intent, objects, plan.Primary, plan.Overlay),
                InternalCelestialRequests = plan.InternalRequests,
                Warnings = warnings
            };

            previousFamilies.Enqueue(plan.Primary.AssetFamily);
            while (previousFamilies.Count > 2)
                previousFamilies.Dequeue();
        }
    }

    private static WeeklyVisualIntentShot ToShot(WeeklyVisualIntentBeat beat)
        => new()
        {
            ShotId = $"visual-intent-shot-{beat.Sequence + 1:000}",
            BeatId = beat.BeatId,
            Form = beat.Form,
            Sequence = beat.Sequence,
            StartSeconds = beat.StartSeconds,
            EndSeconds = beat.EndSeconds,
            VisualIntent = beat.VisualIntent.ToString(),
            Primary = beat.Primary,
            Overlay = beat.Overlay,
            RendererShouldTreatMotionGraphicAsOverlay = beat.Overlay?.AssetFamily.Equals("MotionGraphics", StringComparison.OrdinalIgnoreCase) == true,
            RendererShouldTreatEducationalGraphicAsOverlay = beat.Overlay?.AssetFamily.Equals("EducationalOverlay", StringComparison.OrdinalIgnoreCase) == true,
            Notes = "Phase 6.6A planning only: do not render yet; future renderer should composite overlays over the primary visual."
        };

    private static WeeklyAssetSelection SelectAssets(WeeklyVisualIntentType intent, IReadOnlyCollection<string> objects, string text, WeeklyAssetIndex assetIndex, Queue<string> previousFamilies)
    {
        var detailRequested = DetailRegex().IsMatch(text);
        var primaryFamilies = PrimaryFamilies(intent, objects, detailRequested).ToList();
        if (previousFamilies.Count == 2 && previousFamilies.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            primaryFamilies.Remove(previousFamilies.Peek());
        if (primaryFamilies.Count == 0)
            primaryFamilies = PrimaryFamilies(intent, objects, detailRequested).ToList();

        var primary = ResolveUse(primaryFamilies, objects, assetIndex, "primary", true, null);
        var secondary = SecondaryFamily(intent) is { } secondaryFamily
            ? ResolveUse([secondaryFamily], objects, assetIndex, "secondary", true, null)
            : null;
        var overlay = OverlayFamily(intent) is { } overlayFamily
            ? ResolveUse([overlayFamily], objects, assetIndex, "overlay", false, OverlayPlacement(intent)) with
            {
                MaxFullscreenSeconds = overlayFamily.Equals("MotionGraphics", StringComparison.OrdinalIgnoreCase) ? 3 : 0,
                Fullscreen = false
            }
            : null;

        var requests = new List<WeeklyInternalCelestialAssetRequest>();
        var nasaOrInternalMissing = objects.Count > 0
            && (detailRequested
                || intent is WeeklyVisualIntentType.ScientificContext or WeeklyVisualIntentType.EducationalExplanation
                || secondary?.AssetFamily == "NASA_JWST_InternalCelestial")
            && !assetIndex.HasAvailable("NASA_JWST_InternalCelestial", objects);
        if ((primary.Availability == "requestedButUnavailable" || nasaOrInternalMissing) && objects.Count > 0)
        {
            requests.AddRange(objects.Select(o => new WeeklyInternalCelestialAssetRequest
            {
                ObjectKey = NormalizeInternalObjectKey(o),
                Reason = detailRequested
                    ? "Narration mentions rings or planetary detail; prefer NASA/JWST/InternalCelestial detail imagery."
                    : "Preferred NASA/JWST asset was unavailable or weak; request InternalCelestial fallback."
            }));
        }

        return new WeeklyAssetSelection(primary, secondary, overlay, requests);
    }

    private static WeeklyVisualAssetUse ResolveUse(IReadOnlyCollection<string> families, IReadOnlyCollection<string> objects, WeeklyAssetIndex assetIndex, string role, bool fullscreen, string? placement)
    {
        foreach (var family in families)
        {
            var asset = assetIndex.Find(family, objects);
            if (asset is not null)
            {
                return new WeeklyVisualAssetUse
                {
                    AssetFamily = family,
                    AssetSource = asset.Source,
                    AssetId = asset.Id,
                    Path = asset.Path,
                    MatchedObject = asset.MatchedObject,
                    Role = role,
                    Placement = placement,
                    Fullscreen = fullscreen,
                    Availability = "available"
                };
            }
        }

        var fallbackFamily = families.FirstOrDefault() ?? "InternalCelestial";
        return new WeeklyVisualAssetUse
        {
            AssetFamily = fallbackFamily,
            AssetSource = fallbackFamily.Equals("InternalCelestial", StringComparison.OrdinalIgnoreCase) ? "InternalCelestial" : null,
            MatchedObject = objects.FirstOrDefault(),
            Role = role,
            Placement = placement,
            Fullscreen = fullscreen,
            Availability = fallbackFamily.Equals("InternalCelestial", StringComparison.OrdinalIgnoreCase) ? "requestedButUnavailable" : "missing"
        };
    }

    private static List<string> PrimaryFamilies(WeeklyVisualIntentType intent, IReadOnlyCollection<string> objects, bool detailRequested)
    {
        if (detailRequested && objects.Count > 0)
            return ["NASA_JWST_InternalCelestial", "InternalCelestial", "Stellarium"];

        return intent switch
        {
            WeeklyVisualIntentType.Hook => ["AICinematic", "Stellarium"],
            WeeklyVisualIntentType.Observation => ["Stellarium", "NASA_JWST_InternalCelestial", "InternalCelestial"],
            WeeklyVisualIntentType.DirectionGuidance => ["Stellarium"],
            WeeklyVisualIntentType.BestTime => ["Stellarium", "AICinematic"],
            WeeklyVisualIntentType.ScientificContext => ["NASA_JWST_InternalCelestial", "InternalCelestial", "Stellarium"],
            WeeklyVisualIntentType.EducationalExplanation => ["Stellarium", "NASA_JWST_InternalCelestial", "InternalCelestial"],
            WeeklyVisualIntentType.AstrophotographyTip => ["Stellarium", "InternalCelestial"],
            WeeklyVisualIntentType.Summary => ["AICinematic", "Stellarium"],
            WeeklyVisualIntentType.CallToAction => ["AICinematic", "Stellarium"],
            _ => ["Stellarium"]
        };
    }

    private static string? SecondaryFamily(WeeklyVisualIntentType intent)
        => intent switch
        {
            WeeklyVisualIntentType.Hook => "Stellarium",
            WeeklyVisualIntentType.Observation => "NASA_JWST_InternalCelestial",
            WeeklyVisualIntentType.ScientificContext => "Stellarium",
            WeeklyVisualIntentType.Summary => "MotionGraphics",
            _ => null
        };

    private static string? OverlayFamily(WeeklyVisualIntentType intent)
        => intent switch
        {
            WeeklyVisualIntentType.DirectionGuidance => "MotionGraphics",
            WeeklyVisualIntentType.BestTime => "MotionGraphics",
            WeeklyVisualIntentType.EducationalExplanation => "EducationalOverlay",
            WeeklyVisualIntentType.AstrophotographyTip => "MotionGraphics",
            WeeklyVisualIntentType.Summary => "MotionGraphics",
            WeeklyVisualIntentType.CallToAction => "MotionGraphics",
            _ => null
        };

    private static string OverlayPlacement(WeeklyVisualIntentType intent)
        => intent switch
        {
            WeeklyVisualIntentType.CallToAction => "small-cta-text",
            WeeklyVisualIntentType.EducationalExplanation => "non-fullscreen-educational-overlay",
            WeeklyVisualIntentType.BestTime => "best-time-lower-third",
            WeeklyVisualIntentType.AstrophotographyTip => "camera-visibility-tip-lower-third",
            _ => "lower-third"
        };

    private static WeeklyVisualIntentType ClassifyIntent(string text, int sequence, string form)
    {
        if (sequence == 0 || HookRegex().IsMatch(text))
            return WeeklyVisualIntentType.Hook;
        if (CallToActionRegex().IsMatch(text))
            return WeeklyVisualIntentType.CallToAction;
        if (AstroPhotoRegex().IsMatch(text))
            return WeeklyVisualIntentType.AstrophotographyTip;
        if (BestTimeRegex().IsMatch(text))
            return WeeklyVisualIntentType.BestTime;
        if (DirectionRegex().IsMatch(text))
            return WeeklyVisualIntentType.DirectionGuidance;
        if (ScienceRegex().IsMatch(text))
            return WeeklyVisualIntentType.ScientificContext;
        if (EducationRegex().IsMatch(text))
            return WeeklyVisualIntentType.EducationalExplanation;
        if (SummaryRegex().IsMatch(text) || (form.Equals("shortform", StringComparison.OrdinalIgnoreCase) && sequence > 2))
            return WeeklyVisualIntentType.Summary;
        return WeeklyVisualIntentType.Observation;
    }

    private static IReadOnlyCollection<string> DetectObjects(string text)
    {
        var found = new List<string>();
        foreach (var key in InternalCelestialLookupKeys)
        {
            if (Regex.IsMatch(text, $"\\b{Regex.Escape(key.Replace("DeepSkyObject", "deep sky"))}\\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
                found.Add(key);
        }

        if (Regex.IsMatch(text, "\\brings?\\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)) && !found.Contains("Saturn"))
            found.Add("Saturn");

        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static WeeklyVisualIntentValidationReport Validate(IReadOnlyCollection<WeeklyVisualIntentBeat> beats, IReadOnlyCollection<string> missingInputs)
    {
        var errors = new List<string>();
        var warnings = missingInputs.Select(x => $"Input file was not found and was skipped: {x}").ToList();
        if (beats.Count == 0)
            errors.Add("No narration beats could be extracted from the supplied WeeklySkyForecast inputs.");

        var fullscreenMotionOveruse = beats.Count(x => x.Primary.AssetFamily == "MotionGraphics" && x.Primary.Fullscreen && (x.DurationSeconds ?? 0) > 3);
        var fullscreenEducation = beats.Count(x => x.Primary.AssetFamily == "EducationalOverlay" && x.Primary.Fullscreen)
            + beats.Count(x => x.Overlay?.AssetFamily == "EducationalOverlay" && x.Overlay.Fullscreen);
        var mismatchCount = beats.Count(HasMismatch);
        var matchedCount = beats.Count - mismatchCount;

        return new WeeklyVisualIntentValidationReport
        {
            VisualIntentReady = errors.Count == 0,
            TotalBeats = beats.Count,
            MatchedBeatCount = Math.Max(0, matchedCount),
            UnmatchedBeatCount = mismatchCount,
            NarrationVisualMismatchCount = mismatchCount,
            FullscreenMotionGraphicOveruseCount = fullscreenMotionOveruse,
            FullscreenEducationalOverlayCount = fullscreenEducation,
            SameFamilyConsecutiveMax = SameFamilyConsecutiveMax(beats),
            ShortformHookStrongVisualPassed = ShortformHookStrongVisualPassed(beats),
            SaturnNarrationMatchedToSaturnVisual = ObjectMatched(beats, "Saturn"),
            VenusNarrationMatchedToVenusVisual = ObjectMatched(beats, "Venus"),
            MoonNarrationMatchedToMoonVisual = ObjectMatched(beats, "Moon"),
            Warnings = warnings,
            Errors = errors
        };
    }

    private static bool HasMismatch(WeeklyVisualIntentBeat beat)
    {
        foreach (var obj in beat.MentionedObjects)
        {
            if (MatchesObject(beat.Primary, obj) || MatchesObject(beat.Secondary, obj) || MatchesObject(beat.Overlay, obj))
                continue;
            return true;
        }

        return beat.VisualIntent switch
        {
            WeeklyVisualIntentType.DirectionGuidance => beat.Primary.AssetFamily != "Stellarium" || beat.Overlay?.AssetFamily != "MotionGraphics",
            WeeklyVisualIntentType.EducationalExplanation => beat.Primary.AssetFamily == "EducationalOverlay" || beat.Overlay?.Fullscreen == true,
            WeeklyVisualIntentType.CallToAction => beat.Primary.AssetFamily == "MotionGraphics",
            _ => false
        };
    }

    private static bool ObjectMatched(IReadOnlyCollection<WeeklyVisualIntentBeat> beats, string objectKey)
    {
        var relevant = beats.Where(x => x.MentionedObjects.Contains(objectKey, StringComparer.OrdinalIgnoreCase)).ToList();
        return relevant.Count == 0 || relevant.All(x => MatchesObject(x.Primary, objectKey) || MatchesObject(x.Secondary, objectKey) || x.InternalCelestialRequests.Any(r => r.ObjectKey == objectKey));
    }

    private static bool MatchesObject(WeeklyVisualAssetUse? use, string objectKey)
        => use is not null && (string.Equals(use.MatchedObject, objectKey, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(use.AssetId) && use.AssetId.Contains(objectKey, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(use.Path) && use.Path.Contains(objectKey, StringComparison.OrdinalIgnoreCase)));

    private static bool ShortformHookStrongVisualPassed(IReadOnlyCollection<WeeklyVisualIntentBeat> beats)
    {
        var firstShort = beats.Where(x => x.Form.Equals("shortform", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.StartSeconds ?? x.Sequence).FirstOrDefault();
        if (firstShort is null)
            return true;

        return firstShort.Primary.AssetFamily is "AICinematic" or "Stellarium" or "NASA_JWST_InternalCelestial" or "InternalCelestial";
    }

    private static int SameFamilyConsecutiveMax(IReadOnlyCollection<WeeklyVisualIntentBeat> beats)
    {
        var max = 0;
        var current = 0;
        string? last = null;
        foreach (var family in beats.Select(x => x.Primary.AssetFamily))
        {
            current = family == last ? current + 1 : 1;
            max = Math.Max(max, current);
            last = family;
        }

        return max;
    }

    private static string BuildRationale(WeeklyVisualIntentType intent, IReadOnlyCollection<string> objects, WeeklyVisualAssetUse primary, WeeklyVisualAssetUse? overlay)
    {
        var objectPhrase = objects.Count == 0 ? "the narration beat" : string.Join("/", objects);
        var overlayPhrase = overlay is null ? "without a standalone card" : $"with a {overlay.Placement} overlay";
        return $"{intent} beat uses {primary.AssetFamily} for {objectPhrase} {overlayPhrase}; motion/education graphics are composited rather than used as full-screen slides.";
    }

    private static void CollectAssets(JsonNode? node, List<IndexedAsset> assets, string? inheritedKey)
    {
        if (node is null)
            return;

        if (node is JsonObject obj)
        {
            var key = FirstString(obj, "objectKey", "objectName", "target", "celestialObject", "matchedObject") ?? inheritedKey;
            var path = FirstString(obj, "path", "filePath", "uri", "url", "assetPath", "imagePath", "videoPath");
            var family = NormalizeFamily(FirstString(obj, "family", "assetFamily", "type", "assetType", "source", "provider", "kind") ?? path ?? "");
            if (!string.IsNullOrWhiteSpace(path) || !string.IsNullOrWhiteSpace(FirstString(obj, "id", "assetId")))
            {
                assets.Add(new IndexedAsset(
                    FirstString(obj, "id", "assetId", "name") ?? path ?? $"asset-{assets.Count + 1}",
                    family,
                    FirstString(obj, "source", "provider") ?? family,
                    path,
                    key));
            }

            foreach (var child in obj)
                CollectAssets(child.Value, assets, key);
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var child in array)
                CollectAssets(child, assets, inheritedKey);
        }
    }

    private static string NormalizeFamily(string value)
    {
        if (Regex.IsMatch(value, "stellarium|sky", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) return "Stellarium";
        if (Regex.IsMatch(value, "ai|cinematic", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) return "AICinematic";
        if (Regex.IsMatch(value, "nasa|jwst|internalcelestial|celestial", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) return "NASA_JWST_InternalCelestial";
        if (Regex.IsMatch(value, "motion|lower|graphic", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) return "MotionGraphics";
        if (Regex.IsMatch(value, "education|explain|overlay", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) return "EducationalOverlay";
        return "Unknown";
    }

    private static string NormalizeInternalObjectKey(string objectKey)
        => InternalCelestialLookupKeys.FirstOrDefault(x => x.Equals(objectKey, StringComparison.OrdinalIgnoreCase)) ?? objectKey;

    private static string? FirstString(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetPropertyValue(name, out var value) && value is not null)
                return value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
        }

        return null;
    }

    private static double? FirstDouble(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetPropertyValue(name, out var value) || value is null)
                continue;
            try
            {
                return value.GetValue<double>();
            }
            catch (InvalidOperationException) { }
            catch (FormatException) { }
        }

        return null;
    }

    private static string FormFromObject(JsonObject obj, string fallback)
        => FirstString(obj, "form", "format", "videoType")?.Contains("short", StringComparison.OrdinalIgnoreCase) == true ? "shortform" : fallback;

    private static bool IsNarrationLike(string value)
        => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3;

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
        => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);

    [GeneratedRegex("\\b(look|watch|tonight|don.t miss|this week|sky forecast|spectacular|beautiful)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex HookRegex();
    [GeneratedRegex("\\b(subscribe|follow|like|share|comment|save this|clear skies)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex CallToActionRegex();
    [GeneratedRegex("\\b(camera|photograph|astrophotography|exposure|lens|tripod|iso|shutter)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex AstroPhotoRegex();
    [GeneratedRegex("\\b(best time|after sunset|before dawn|dawn|dusk|evening|morning|tonight at|around \\d)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex BestTimeRegex();
    [GeneratedRegex("\\b(north|south|east|west|horizon|altitude|azimuth|degrees|look toward|direction)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex DirectionRegex();
    [GeneratedRegex("\\b(science|orbit|atmosphere|rings?|planetary|crater|phase|magnitude|reflect|distance|jwst|nasa)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScienceRegex();
    [GeneratedRegex("\\b(why|because|here.s how|explains|means|learn|understand|appears)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex EducationRegex();
    [GeneratedRegex("\\b(to recap|summary|remember|overall|week ahead|wrap)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryRegex();
    [GeneratedRegex("\\b(rings?|detail|band|crater|phase|cloud tops?)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex DetailRegex();

    private sealed record WeeklyVisualIntentInputs(
        IReadOnlyDictionary<string, JsonNode?> Documents,
        IReadOnlyCollection<string> LoadedInputFileNames,
        IReadOnlyCollection<string> MissingInputFileNames);

    private sealed record SourceSegment(string Form, int Sequence, string Id, string Text, double? StartSeconds, double? EndSeconds, double? DurationSeconds)
    {
        public string StableKey => $"{Form}:{Id}:{Text}";
    }

    private sealed record IndexedAsset(string Id, string Family, string Source, string? Path, string? MatchedObject);
    private sealed record WeeklyAssetSelection(WeeklyVisualAssetUse Primary, WeeklyVisualAssetUse? Secondary, WeeklyVisualAssetUse? Overlay, IReadOnlyCollection<WeeklyInternalCelestialAssetRequest> InternalRequests);

    private sealed class WeeklyAssetIndex
    {
        private readonly IReadOnlyCollection<IndexedAsset> _assets;

        public WeeklyAssetIndex(IReadOnlyCollection<IndexedAsset> assets)
        {
            _assets = assets;
        }

        public bool HasAvailable(string family, IReadOnlyCollection<string> objects)
            => Find(family, objects) is not null;

        public IndexedAsset? Find(string family, IReadOnlyCollection<string> objects)
        {
            var candidates = _assets.Where(x => FamilyMatches(x.Family, family)).ToList();
            if (candidates.Count == 0)
                return null;

            foreach (var obj in objects)
            {
                var objectMatch = candidates.FirstOrDefault(x => Matches(x.MatchedObject, obj) || Matches(x.Id, obj) || Matches(x.Path, obj));
                if (objectMatch is not null)
                    return objectMatch with { MatchedObject = obj };
            }

            return candidates.FirstOrDefault();
        }

        private static bool FamilyMatches(string actual, string requested)
            => actual.Equals(requested, StringComparison.OrdinalIgnoreCase)
                || (requested == "NASA_JWST_InternalCelestial" && (actual == "InternalCelestial" || actual.Contains("NASA", StringComparison.OrdinalIgnoreCase)));

        private static bool Matches(string? value, string objectKey)
            => !string.IsNullOrWhiteSpace(value) && value.Contains(objectKey, StringComparison.OrdinalIgnoreCase);
    }
}
