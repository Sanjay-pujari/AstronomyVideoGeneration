using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
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

public sealed class SceneEditorialPreviewService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<SceneEditorialPreviewService> logger) : ISceneEditorialPreviewService
{
    private const string RecipeDirectoryName = "render-recipes";
    private const string WorkingFramesDirectoryName = "render-working-frames";
    private const string PolishedNarrationPath = "narration/narration-polished.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<SceneEditorialPreviewResponse> GenerateSceneEditorialPreviewAsync(SceneEditorialPreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("RegionId is required.");
        if (request.MaxPlans is < 1) throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var root = ResolveWorkingDirectoryRoot();
        var planIds = await ResolvePlanIdsAsync(request, root, cancellationToken);
        var generatedFiles = new List<string>();
        var warnings = new List<string>();
        var outputs = new List<SceneEditorialPreviewOutput>();
        var approvedPlanCount = 0;

        foreach (var planId in planIds)
        {
            var planRoot = BuildPlanRoot(root, request.RegionId!, planId.ToString("D"));
            var workingFramesRoot = Path.Combine(planRoot, WorkingFramesDirectoryName);
            var recipes = await LoadRecipesAsync(planRoot, cancellationToken);
            if (recipes.Count == 0)
            {
                warnings.Add($"No render recipes found for plan {planId:D} under {Path.Combine(planRoot, RecipeDirectoryName)}.");
                continue;
            }

            var narrationByScene = await LoadNarrationBySceneAsync(planRoot, cancellationToken);
            var sceneDrafts = new List<SceneDraft>();
            foreach (var recipe in recipes.OrderBy(x => x.SceneNumber).Take(4))
            {
                var narration = ResolveNarration(recipe, narrationByScene);
                var cardText = await BuildCardTextAsync(planRoot, recipe, narration, cancellationToken);
                var duration = recipe.DurationSeconds <= 0 ? EstimateDurationSeconds(narration) : recipe.DurationSeconds;
                sceneDrafts.Add(new SceneDraft(recipe, narration, cardText, duration));
            }

            var planApproved = true;
            foreach (var draft in sceneDrafts)
            {
                var sceneNumber = draft.Recipe.SceneNumber;
                var productionVisualPath = ResolveProductionVisualPath(planRoot, sceneNumber);
                var cardPath = productionVisualPath ?? Path.Combine(workingFramesRoot, $"scene-{sceneNumber:000}-card.png");
                var srtPath = Path.Combine(workingFramesRoot, $"scene-{sceneNumber:000}.srt");
                var reviewPath = Path.Combine(workingFramesRoot, $"scene-{sceneNumber:000}-review.json");
                var review = ValidateScene(draft, sceneDrafts);
                planApproved &= review.VisualApproved && review.NarrationApproved && review.AlignmentApproved;

                outputs.Add(new SceneEditorialPreviewOutput(
                    planId.ToString("D"),
                    request.RegionId!,
                    sceneNumber,
                    cardPath,
                    srtPath,
                    reviewPath,
                    review.VisualApproved,
                    review.NarrationApproved,
                    review.AlignmentApproved,
                    review.Issues,
                    review.Recommendations));

                warnings.AddRange(review.Issues.Select(issue => $"Plan {planId:D} scene {sceneNumber:000}: {issue}"));

                if (request.DryRun) continue;
                Directory.CreateDirectory(workingFramesRoot);
                if (productionVisualPath is not null)
                {
                    generatedFiles.Add(productionVisualPath);
                }
                else if (File.Exists(cardPath) && !request.OverwriteExisting)
                {
                    warnings.Add($"Skipped existing scene editorial card for plan {planId:D} scene {sceneNumber:000}. Set overwriteExisting=true to replace it.");
                }
                else
                {
                    await RenderSceneCardAsync(cardPath, draft, cancellationToken);
                    generatedFiles.Add(cardPath);
                }

                if (File.Exists(srtPath) && !request.OverwriteExisting)
                {
                    warnings.Add($"Skipped existing scene editorial SRT for plan {planId:D} scene {sceneNumber:000}. Set overwriteExisting=true to replace it.");
                }
                else
                {
                    await File.WriteAllTextAsync(srtPath, BuildCaptionSrt(draft.Narration, draft.DurationSeconds), cancellationToken);
                    generatedFiles.Add(srtPath);
                }

                if (File.Exists(reviewPath) && !request.OverwriteExisting)
                {
                    warnings.Add($"Skipped existing scene editorial review for plan {planId:D} scene {sceneNumber:000}. Set overwriteExisting=true to replace it.");
                }
                else
                {
                    await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(review, JsonOptions), cancellationToken);
                    generatedFiles.Add(reviewPath);
                }
            }

            if (planApproved && sceneDrafts.Count > 0) approvedPlanCount++;
        }

        var approvedSceneCount = outputs.Count(x => x.VisualApproved && x.NarrationApproved && x.AlignmentApproved);
        logger.LogInformation("Phase 9A.5 generated scene editorial preview for {PlanCount} plan(s). Scenes={SceneCount} ApprovedScenes={ApprovedSceneCount} DryRun={DryRun}", planIds.Count, outputs.Count, approvedSceneCount, request.DryRun);
        return new SceneEditorialPreviewResponse(planIds.Count, outputs.Count, approvedSceneCount, outputs.Count - approvedSceneCount, approvedPlanCount, generatedFiles, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), outputs);
    }

    private async Task<IReadOnlyList<Guid>> ResolvePlanIdsAsync(SceneEditorialPreviewRequest request, string root, CancellationToken cancellationToken)
    {
        if (request.PlanIds is { Count: > 0 })
            return request.PlanIds.Take(request.MaxPlans ?? int.MaxValue).ToArray();

        var region = request.RegionId!.Trim();
        var plans = await db.ContentGenerationPlans.AsNoTracking()
            .Where(p => p.RegionId == region && (p.AstronomyContentOpportunityId != null || p.AstronomyEventIntelligenceId != null))
            .OrderByDescending(p => p.ScheduledUtc ?? DateTimeOffset.MinValue)
            .ThenBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        return plans.Where(id => Directory.Exists(Path.Combine(BuildPlanRoot(root, region, id.ToString("D")), RecipeDirectoryName)))
            .Take(request.MaxPlans ?? int.MaxValue)
            .ToArray();
    }

    private static string? ResolveProductionVisualPath(string planRoot, int sceneNumber)
    {
        var path = Path.Combine(planRoot, "production-visuals", $"scene-{sceneNumber:000}-final.png");
        return File.Exists(path) && new FileInfo(path).Length > 1024 ? path : null;
    }

    private static async Task<IReadOnlyList<RenderRecipeDocument>> LoadRecipesAsync(string planRoot, CancellationToken cancellationToken)
    {
        var recipeRoot = Path.Combine(planRoot, RecipeDirectoryName);
        if (!Directory.Exists(recipeRoot)) return [];
        var recipes = new List<RenderRecipeDocument>();
        foreach (var path in Directory.EnumerateFiles(recipeRoot, "scene-*.recipe.json", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var recipe = JsonSerializer.Deserialize<RenderRecipeDocument>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
                if (recipe is not null) recipes.Add(recipe);
            }
            catch
            {
                // Unreadable recipes are reported by the missing scene count/review warnings rather than blocking preview generation.
            }
        }
        return recipes;
    }

    private static async Task<Dictionary<int, string>> LoadNarrationBySceneAsync(string planRoot, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, string>();
        var polishedPath = Path.Combine(planRoot, PolishedNarrationPath.Replace('/', Path.DirectorySeparatorChar));
        var paths = File.Exists(polishedPath)
            ? new[] { polishedPath }
            : Directory.Exists(planRoot)
                ? Directory.EnumerateFiles(planRoot, "*.json", SearchOption.AllDirectories).Where(path => !path.Contains(RecipeDirectoryName, StringComparison.OrdinalIgnoreCase)).OrderBy(path => path.Length).ToArray()
                : [];

        foreach (var path in paths)
        {
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                CollectSceneNarration(doc.RootElement, result);
            }
            catch
            {
                // Ignore unrelated JSON artifacts in the plan folder.
            }
        }
        return result;
    }

    private static void CollectSceneNarration(JsonElement element, IDictionary<int, string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var scene = FindIntByNames(element, ["sceneNumber", "scene", "sceneIndex"]);
            var narration = FindDirectStringByNames(element, ["finalNarration", "polishedNarration", "narrationText", "scriptText", "script", "text"]);
            if (scene.HasValue && !string.IsNullOrWhiteSpace(narration))
            {
                var sceneNumber = scene.Value <= 0 ? scene.Value + 1 : scene.Value;
                result.TryAdd(sceneNumber, CleanText(narration!));
            }
            foreach (var property in element.EnumerateObject()) CollectSceneNarration(property.Value, result);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectSceneNarration(item, result);
        }
    }

    private static string ResolveNarration(RenderRecipeDocument recipe, IReadOnlyDictionary<int, string> narrationByScene)
        => narrationByScene.TryGetValue(recipe.SceneNumber, out var narration) && !string.IsNullOrWhiteSpace(narration)
            ? narration
            : recipe.SceneName;

    private static async Task<SceneCardText> BuildCardTextAsync(string planRoot, RenderRecipeDocument recipe, string narration, CancellationToken cancellationToken)
    {
        var title = HumanizeTitle(FindMeaningfulString(recipe.SceneName, recipe.ContentCategory, "Sky event"));
        var mainObjects = ExtractObjects(recipe, narration);
        var guidance = ExtractGuidance(narration, recipe.SceneName);
        var emphasis = recipe.SceneNumber switch
        {
            1 => "Tonight's sky highlight",
            2 => "What to watch",
            3 => "Where and when to look",
            4 => "Clear skies reminder",
            _ => "Viewing checkpoint"
        };

        foreach (var input in recipe.Inputs)
        {
            var path = ResolveAssetPath(planRoot, input.AssetPath);
            if (!File.Exists(path) || !Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                title = FirstNonInternal(FindStringByNames(doc.RootElement, ["eventTitle", "eventName", "title", "headline"]), title);
                mainObjects = MergeObjects(mainObjects, FindStringArrayByNames(doc.RootElement, ["objectNames", "objects", "targets", "celestialObjects", "planets"]));
                guidance = FirstNonInternal(FindStringByNames(doc.RootElement, ["viewingGuidance", "direction", "bestTime", "summary", "description", "subtitle"]), guidance);
            }
            catch
            {
                // Ignore individual asset metadata issues; the review JSON captures approval concerns.
            }
        }

        var body = recipe.SceneNumber switch
        {
            1 => $"{Trim(title, 58)}\n{Trim(mainObjects, 72)}\n{Trim(FindHook(narration, guidance), 84)}",
            2 => $"Watch for {Trim(HighlightVenusJupiter(mainObjects, narration), 60)}\n{Trim(guidance, 86)}",
            3 => $"Use the sky reference nearby\n{Trim(guidance, 86)}\nBest time: {Trim(FindBestTime(narration, guidance), 48)}",
            4 => $"{Trim(FindPayoff(narration), 76)}\nObserve safely, and clear skies.",
            _ => Trim(guidance, 120)
        };

        return new SceneCardText(Trim(title, 70), emphasis, Trim(mainObjects, 100), body);
    }

    private static SceneEditorialReview ValidateScene(SceneDraft draft, IReadOnlyList<SceneDraft> allScenes)
    {
        var issues = new List<string>();
        var recommendations = new List<string>();
        var sceneNumber = draft.Recipe.SceneNumber;
        var normalizedNarration = Normalize(draft.Narration);

        if (string.IsNullOrWhiteSpace(draft.Card.Title) || string.IsNullOrWhiteSpace(draft.Card.Body))
            issues.Add("Scene card is missing viewer-facing title or guidance text.");
        if (ContainsInternalMetadata(draft.Card.Title) || ContainsInternalMetadata(draft.Card.Body) || ContainsInternalMetadata(draft.Card.MainObjects))
            issues.Add("Scene card contains internal metadata or implementation labels.");

        if (string.IsNullOrWhiteSpace(normalizedNarration))
            issues.Add("Scene narration is empty.");
        if (sceneNumber == allScenes.Max(x => x.Recipe.SceneNumber) && string.IsNullOrWhiteSpace(normalizedNarration))
            issues.Add("Closing scene narration must not be empty.");
        if (allScenes.Count(s => Normalize(s.Narration).Equals(normalizedNarration, StringComparison.OrdinalIgnoreCase)) > 1)
            issues.Add("Duplicate scene narration detected.");
        var scene1 = allScenes.FirstOrDefault(s => s.Recipe.SceneNumber == 1);
        var scene2 = allScenes.FirstOrDefault(s => s.Recipe.SceneNumber == 2);
        if (scene1 is not null && scene2 is not null && sceneNumber is 1 or 2 && Normalize(scene1.Narration).Equals(Normalize(scene2.Narration), StringComparison.OrdinalIgnoreCase))
            issues.Add("Scene 1 and scene 2 SRT content must be different.");
        if (!MentionsKeyEventOrGuidance(draft))
            issues.Add("Narration does not clearly mention the key event, object, direction, time, or viewing guidance.");
        if (!NarrationAlignsWithVisual(draft))
            issues.Add("Visual card text and narration are not sufficiently aligned.");

        if (sceneNumber == 2 && !ContainsAny(draft.Card.Body + " " + draft.Narration, ["venus", "jupiter"]))
            recommendations.Add("If this is the Venus/Jupiter pilot scene, explicitly call out Venus or Jupiter.");
        if (sceneNumber == 4 && !ContainsAny(draft.Card.Body + " " + draft.Narration, ["safe", "clear", "skies", "reminder", "outside"]))
            recommendations.Add("Add a short safe-observing or clear-skies closing callout.");

        var visualApproved = !issues.Any(issue => issue.Contains("card", StringComparison.OrdinalIgnoreCase) || issue.Contains("metadata", StringComparison.OrdinalIgnoreCase));
        var narrationApproved = !issues.Any(issue => issue.Contains("narration", StringComparison.OrdinalIgnoreCase) || issue.Contains("SRT", StringComparison.OrdinalIgnoreCase) || issue.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
        var alignmentApproved = !issues.Any(issue => issue.Contains("aligned", StringComparison.OrdinalIgnoreCase) || issue.Contains("key event", StringComparison.OrdinalIgnoreCase));
        return new SceneEditorialReview(sceneNumber, visualApproved, narrationApproved, alignmentApproved, issues, recommendations);
    }

    private static async Task RenderSceneCardAsync(string outputPath, SceneDraft draft, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgba32>(1920, 1080, Color.ParseHex("#050712"));
        var titleFont = ResolveFont(78, FontStyle.Bold);
        var emphasisFont = ResolveFont(36, FontStyle.Bold);
        var bodyFont = ResolveFont(46, FontStyle.Regular);
        var smallFont = ResolveFont(30, FontStyle.Regular);
        image.Mutate(ctx =>
        {
            DrawCinematicBackground(ctx, 1920, 1080);
            DrawStarField(ctx, 1920, 1080, draft.Recipe.SceneNumber);
            ctx.Fill(Color.Black.WithAlpha(0.34f), new RectangleF(0, 0, 1920, 1080));
            ctx.DrawLine(Color.ParseHex("#F4B35F").WithAlpha(0.82f), 5, new PointF(120, 126), new PointF(690, 126));
            ctx.DrawText(new RichTextOptions(emphasisFont) { Origin = new PointF(120, 150), WrappingLength = 1260 }, draft.Card.Emphasis, Color.ParseHex("#F9B24E"));
            ctx.DrawText(new RichTextOptions(titleFont) { Origin = new PointF(120, 215), WrappingLength = 1320 }, draft.Card.Title, Color.White);

            var panel = new RectangleF(116, 520, 1380, 360);
            ctx.Fill(Color.Black.WithAlpha(0.52f), panel);
            ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(0.38f), 2, panel);
            ctx.DrawText(new RichTextOptions(bodyFont) { Origin = new PointF(158, 555), WrappingLength = 1290 }, draft.Card.Body, Color.ParseHex("#EAF6FF"));
            ctx.DrawText(new RichTextOptions(smallFont) { Origin = new PointF(158, 900), WrappingLength = 1280 }, $"Look for: {draft.Card.MainObjects}", Color.ParseHex("#CBE3FF"));
            DrawVignette(ctx, 1920, 1080);
        });
        await image.SaveAsPngAsync(outputPath, new PngEncoder(), cancellationToken);
    }

    private static string BuildCaptionSrt(string text, double durationSeconds)
    {
        var clean = CleanText(text);
        var chunks = SplitCaption(clean, 78).DefaultIfEmpty(" ").Take(6).ToArray();
        var cueDuration = Math.Max(1, durationSeconds) / Math.Max(1, chunks.Length);
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

    private static string ExtractObjects(RenderRecipeDocument recipe, string narration)
    {
        var objects = new[] { "Venus", "Jupiter", "Moon", "Mars", "Saturn", "Mercury", "Orion", "Scorpius", "Leo", "Virgo", "Pleiades" }
            .Where(name => narration.Contains(name, StringComparison.OrdinalIgnoreCase) || recipe.SceneName.Contains(name, StringComparison.OrdinalIgnoreCase) || recipe.Inputs.Any(input => input.AssetPath.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (objects.Count == 0)
            objects.AddRange(recipe.Inputs.SelectMany(input => Tokenize(Path.GetFileNameWithoutExtension(input.AssetPath))).Where(token => !ContainsInternalMetadata(token)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
        return objects.Count == 0 ? "bright planets and nearby stars" : string.Join(", ", objects.Take(5));
    }

    private static string MergeObjects(string current, IReadOnlyList<string> found)
    {
        var values = current.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(found.Where(value => !ContainsInternalMetadata(value)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        return values.Length == 0 ? current : string.Join(", ", values);
    }

    private static string HighlightVenusJupiter(string objects, string narration)
        => ContainsAny(objects + " " + narration, ["venus", "jupiter"])
            ? string.Join(" and ", new[] { "Venus", "Jupiter" }.Where(name => (objects + " " + narration).Contains(name, StringComparison.OrdinalIgnoreCase)))
            : objects;

    private static string ExtractGuidance(string narration, string fallback)
    {
        var sentence = Regex.Split(narration, @"(?<=[.!?])\s+")
            .FirstOrDefault(s => ContainsAny(s, ["look", "watch", "time", "after sunset", "before sunrise", "horizon", "east", "west", "south", "north", "sky"]));
        return string.IsNullOrWhiteSpace(sentence) ? fallback : sentence.Trim();
    }

    private static string FindHook(string narration, string guidance)
        => Regex.Split(narration, @"(?<=[.!?])\s+").FirstOrDefault(s => s.Length >= 20) ?? guidance;

    private static string FindBestTime(string narration, string guidance)
    {
        var combined = narration + " " + guidance;
        var match = Regex.Match(combined, @"\b(after sunset|before sunrise|dawn|dusk|twilight|tonight|early evening|late evening|\d{1,2}(:\d{2})?\s?(am|pm))\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : "when the sky is darkest and clear";
    }

    private static string FindPayoff(string narration)
        => Regex.Split(narration, @"(?<=[.!?])\s+").Reverse().FirstOrDefault(s => s.Length >= 20) ?? "Step outside for one quiet look before the moment fades.";

    private static bool MentionsKeyEventOrGuidance(SceneDraft draft)
        => ContainsAny(draft.Narration + " " + draft.Recipe.SceneName + " " + draft.Card.MainObjects, ["venus", "jupiter", "moon", "planet", "star", "constellation", "meteor", "eclipse", "look", "watch", "horizon", "east", "west", "north", "south", "sunset", "sunrise", "tonight", "sky"]);

    private static bool NarrationAlignsWithVisual(SceneDraft draft)
    {
        var cardTokens = ImportantTokens(draft.Card.Title + " " + draft.Card.MainObjects + " " + draft.Card.Body).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var narrationTokens = ImportantTokens(draft.Narration).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return cardTokens.Overlaps(narrationTokens) || ContainsAny(draft.Narration, ["look", "watch", "sky", "tonight", "horizon"]);
    }

    private static IEnumerable<string> ImportantTokens(string value)
        => Regex.Matches(value ?? string.Empty, @"\b[\p{L}][\p{L}']{3,}\b").Select(m => m.Value.ToLowerInvariant()).Where(x => !new[] { "scene", "visual", "with", "from", "this", "that", "your", "look", "watch" }.Contains(x));

    private static bool ContainsInternalMetadata(string? value)
        => !string.IsNullOrWhiteSpace(value) && (Regex.IsMatch(value, @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(value, @"\b[A-Za-z]:?[\\/].+\.(json|png|jpg|jpeg|webp|wav|mp4)\b", RegexOptions.IgnoreCase)
            || value.Contains("TextOverlayCard", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SkyMapCard", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ConstellationGuide", StringComparison.OrdinalIgnoreCase)
            || value.Contains("asset id", StringComparison.OrdinalIgnoreCase)
            || value.Contains("prompt id", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Visual:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Objects:", StringComparison.OrdinalIgnoreCase));

    private static string FirstNonInternal(string? candidate, string fallback)
        => !string.IsNullOrWhiteSpace(candidate) && !ContainsInternalMetadata(candidate) ? CleanText(candidate!) : fallback;

    private static string FindMeaningfulString(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !ContainsInternalMetadata(value)) ?? "Sky event";

    private static string HumanizeTitle(string value)
        => CleanText(Regex.Replace(value, "[-_]+", " "));

    private static string CleanText(string text)
        => Regex.Replace(text.Trim(), "\\s+", " ");

    private static string Normalize(string text)
        => CleanText(text ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

    private static bool ContainsAny(string value, IReadOnlyList<string> needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string Trim(string value, int max)
        => string.IsNullOrWhiteSpace(value) || value.Length <= max ? value : value[..max].TrimEnd() + "…";

    private static IEnumerable<string> Tokenize(string value)
        => Regex.Split(value ?? string.Empty, "[-_\\s]+").Where(x => x.Length > 2 && !x.All(char.IsDigit));

    private static string ResolveAssetPath(string planRoot, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return string.Empty;
        if (Path.IsPathRooted(assetPath)) return assetPath;
        var rooted = Path.Combine(planRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(rooted) ? rooted : assetPath;
    }

    private static string? FindDirectStringByNames(JsonElement element, IReadOnlyList<string> names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
            if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        return null;
    }

    private static string? FindStringByNames(JsonElement element, IReadOnlyList<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var direct = FindDirectStringByNames(element, names);
            if (!string.IsNullOrWhiteSpace(direct)) return direct;
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

    private static double EstimateDurationSeconds(string narration)
        => Math.Clamp(Regex.Matches(narration ?? string.Empty, @"\b[\p{L}\p{N}']+\b").Count / 2.4, 4, 18);

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string BuildPlanRoot(string root, string regionId, string planId)
        => Path.Combine(root, "assets", regionId, "plans", planId);

    private static Font ResolveFont(float size, FontStyle style = FontStyle.Regular)
    {
        var family = SystemFonts.Collection.Families.FirstOrDefault(f => f.Name.Contains("DejaVu Sans", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(family.Name)) family = SystemFonts.Collection.Families.FirstOrDefault(f => f.Name.Contains("Arial", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(family.Name)) family = SystemFonts.Collection.Families.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(family.Name)) throw new InvalidOperationException("No system fonts available for scene editorial card rendering.");
        return family.CreateFont(size, style);
    }

    private static void DrawCinematicBackground(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(Color.ParseHex("#071126"), new RectangleF(0, 0, width, height));
        ctx.Fill(Color.ParseHex("#1A2E5B").WithAlpha(0.44f), new EllipsePolygon(width * 0.74f, height * 0.30f, width * 0.48f));
        ctx.Fill(Color.ParseHex("#5B2A86").WithAlpha(0.24f), new EllipsePolygon(width * 0.28f, height * 0.28f, width * 0.36f));
        ctx.Fill(Color.ParseHex("#F2B35F").WithAlpha(0.11f), new EllipsePolygon(width * 0.80f, height * 0.78f, width * 0.42f));
    }

    private static void DrawStarField(IImageProcessingContext ctx, int width, int height, int seed)
    {
        var random = new Random(seed * 7919);
        for (var i = 0; i < 230; i++)
        {
            var x = random.NextSingle() * width;
            var y = random.NextSingle() * height;
            var r = 0.8f + random.NextSingle() * 2.3f;
            ctx.Fill(Color.White.WithAlpha(0.24f + random.NextSingle() * 0.58f), new EllipsePolygon(x, y, r));
        }
    }

    private static void DrawVignette(IImageProcessingContext ctx, int width, int height)
        => ctx.Draw(Color.Black.WithAlpha(0.40f), 82, new RectangleF(-34, -34, width + 68, height + 68));

    private sealed record SceneDraft(RenderRecipeDocument Recipe, string Narration, SceneCardText Card, double DurationSeconds);
    private sealed record SceneCardText(string Title, string Emphasis, string MainObjects, string Body);
}
