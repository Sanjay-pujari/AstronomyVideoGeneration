using System.Globalization;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class SceneIntentBuilder(ILogger<SceneIntentBuilder> logger)
{
    private const string PhaseName = "Editorial Intelligence Foundation";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<SceneIntentBuilderResult> BuildAndWriteDiagnosticsAsync(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, CancellationToken cancellationToken)
    {
        logger.LogInformation("Editorial Intelligence Foundation executed for RC2 batch generation. OutputRoot={OutputRoot}; Success={Success}", response.OutputRoot, response.Success);

        if (string.IsNullOrWhiteSpace(response.OutputRoot))
        {
            logger.LogWarning("Editorial Intelligence Foundation skipped diagnostics because RC2 response did not include an OutputRoot.");
            return SceneIntentBuilderResult.Empty;
        }

        var outputRoot = response.OutputRoot!;
        var productionIntelligencePath = Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json");
        var questionAnswerSetPath = Path.Combine(outputRoot, "question-engine", "question-answer-set.json");
        var scenePlanPath = Path.Combine(outputRoot, "question-engine", "question-driven-scene-plan.json");
        var planInput = ReadFirstJson(Path.Combine(outputRoot, "plan-input", "content-plan-production-request.json"));
        var intelligence = ReadFirstJson(productionIntelligencePath);
        var questionAnswerSet = ReadFirstJson(questionAnswerSetPath);
        var scenePlan = ReadFirstJson(scenePlanPath);
        var observationMetadata = BuildObservationMetadata(planInput, intelligence);
        var sceneElements = ReadSceneElements(scenePlan);

        if (sceneElements.Count == 0)
        {
            sceneElements.Add(default);
        }

        var intents = sceneElements.Select((scene, index) => BuildIntent(request, response, planInput, intelligence, observationMetadata, scene, index)).ToArray();
        var diagnosticsRoot = Path.Combine(outputRoot, "editorial");
        Directory.CreateDirectory(diagnosticsRoot);
        var observationMetadataPath = Path.Combine(diagnosticsRoot, "observation-metadata.json");
        var sceneIntentsPath = Path.Combine(diagnosticsRoot, "scene-intents.json");
        var diagnosticsPath = Path.Combine(diagnosticsRoot, "editorial-diagnostics.json");
        var inputFiles = new[] { productionIntelligencePath, questionAnswerSetPath, scenePlanPath };
        var allWarnings = observationMetadata.MissingFactWarnings.Concat(intents.SelectMany(intent => intent.MissingFactWarnings)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var diagnostics = new
        {
            phaseNo = 6,
            phaseName = PhaseName,
            orchestrationVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion,
            subPhases = new[]
            {
                "6.1 Observation Metadata Builder",
                "6.2 Scene Intent Builder",
                "6.4 Editorial Diagnostics"
            },
            inputs = inputFiles.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) }).ToArray(),
            outputs = new[] { NormalizePath(observationMetadataPath), NormalizePath(sceneIntentsPath), NormalizePath(diagnosticsPath) },
            sceneIntentCount = intents.Length,
            missingFactWarningCount = allWarnings.Length,
            missingFactWarnings = allWarnings,
            questionAnswerSetLoaded = questionAnswerSet.HasValue,
            scenePlanLoaded = scenePlan.HasValue,
            productionEventIntelligenceLoaded = intelligence.HasValue
        };
        await File.WriteAllTextAsync(observationMetadataPath, JsonSerializer.Serialize(observationMetadata, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(sceneIntentsPath, JsonSerializer.Serialize(intents, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);

        logger.LogInformation("Editorial Intelligence Foundation wrote observation metadata, {SceneIntentCount} scene intents, and diagnostics to {DiagnosticsPath}.", intents.Length, diagnosticsPath);
        return new SceneIntentBuilderResult(intents, [observationMetadataPath, sceneIntentsPath, diagnosticsPath]);
    }

    private static ObservationMetadata BuildObservationMetadata(JsonElement? planInput, JsonElement? intelligence)
    {
        var source = intelligence ?? planInput;
        var timeZone = FirstNonEmpty(FindString(source, "timeZone"), FindString(planInput, "timeZone"));
        var startUtc = FirstNonEmpty(FindString(source, "startUtc"), FindString(planInput, "startUtc"));
        var peakUtc = FirstNonEmpty(FindString(source, "peakUtc"), FindString(planInput, "peakUtc"));
        var endUtc = FirstNonEmpty(FindString(source, "endUtc"), FindString(planInput, "endUtc"));
        var scheduledUtc = FirstNonEmpty(FindString(source, "scheduledUtc"), FindString(planInput, "scheduledUtc"));
        var angularSeparationDegrees = FirstNonEmpty(FindString(source, "angularSeparationDegrees"), FindString(planInput, "angularSeparationDegrees"));
        var primaryObjects = FindStringArray(source, "primaryObjects").Concat(FindStringArray(planInput, "primaryObjects")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var secondaryObjects = FindStringArray(source, "secondaryObjects").Concat(FindStringArray(planInput, "secondaryObjects")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var warnings = new List<string>();

        WarnIfMissing(warnings, "startUtc", startUtc);
        WarnIfMissing(warnings, "peakUtc", peakUtc);
        WarnIfMissing(warnings, "endUtc", endUtc);
        WarnIfMissing(warnings, "scheduledUtc", scheduledUtc);
        WarnIfMissing(warnings, "timeZone", timeZone);
        if (primaryObjects.Length == 0) warnings.Add("Missing metadata for primaryObjects.");
        if (secondaryObjects.Length == 0) warnings.Add("Missing metadata for secondaryObjects.");
        WarnIfMissing(warnings, "angularSeparationDegrees", angularSeparationDegrees);

        var bestViewingWindowLocal = FirstNonEmpty(FindString(source, "bestViewingWindowLocal"), FindString(planInput, "bestViewingWindowLocal"));
        var skyDirectionHint = FirstNonEmpty(FindString(source, "skyDirectionHint"), FindString(planInput, "skyDirectionHint"));
        var moonInterference = FirstNonEmpty(FindString(source, "moonInterference"), FindString(planInput, "moonInterference"));
        var moonIlluminationPercent = FirstNonEmpty(FindString(source, "moonIlluminationPercent"), FindString(planInput, "moonIlluminationPercent"));
        var visibilityRegion = FirstNonEmpty(FindString(source, "visibilityRegion"), FindString(planInput, "visibilityRegion"));
        WarnIfMissing(warnings, "bestViewingWindowLocal", bestViewingWindowLocal);
        WarnIfMissing(warnings, "skyDirectionHint", skyDirectionHint);
        WarnIfMissing(warnings, "moonInterference", moonInterference);
        WarnIfMissing(warnings, "moonIlluminationPercent", moonIlluminationPercent);
        WarnIfMissing(warnings, "visibilityRegion", visibilityRegion);

        var timing = new ObservationTimingFacts(startUtc, ToLocal(startUtc, timeZone), peakUtc, ToLocal(peakUtc, timeZone), endUtc, ToLocal(endUtc, timeZone), scheduledUtc, ToLocal(scheduledUtc, timeZone), timeZone);
        var objectFacts = new ObservationObjectFacts(primaryObjects, secondaryObjects, angularSeparationDegrees);
        var fields = new ObservationFields(bestViewingWindowLocal, skyDirectionHint, moonInterference, moonIlluminationPercent, visibilityRegion);
        var derived = new ObservationDerivedFacts(startUtc is not null || endUtc is not null ? new EventWindowUtc(startUtc, endUtc) : null, ToLocal(peakUtc, timeZone), angularSeparationDegrees, primaryObjects.Concat(secondaryObjects).ToArray());
        return new ObservationMetadata(timing, objectFacts, fields, derived, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static SceneIntent BuildIntent(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, JsonElement? planInput, JsonElement? intelligence, ObservationMetadata metadata, JsonElement scene, int index)
    {
        var sceneId = FirstNonEmpty(GetString(scene, "sceneId"), GetString(scene, "id"), $"scene-{index + 1:000}")!;
        var purpose = FirstNonEmpty(GetString(scene, "scenePurpose"), GetString(scene, "purpose"), GetString(scene, "sceneType"), GetString(scene, "segment"), InferPurpose(sceneId))!;
        var eventType = FirstNonEmpty(GetString(scene, "eventType"), GetString(planInput, "eventType"), GetString(intelligence, "eventType"), response.SelectedPlans.FirstOrDefault()?.ContentCategoryCode, "Unknown")!;
        var eventName = FirstNonEmpty(GetString(planInput, "title"), GetString(intelligence, "title"), response.Title, response.SelectedPlans.FirstOrDefault()?.Title, "Unknown")!;
        var observation = string.Equals(purpose, "Observation", StringComparison.OrdinalIgnoreCase) || sceneId.Contains("observ", StringComparison.OrdinalIgnoreCase);
        var eventDate = FirstNonEmpty(metadata.Timing.PeakUtc, metadata.Timing.StartUtc, metadata.Timing.ScheduledUtc);

        var required = new SceneIntentRequiredFacts(
            Fact("EventDate", eventDate, observation),
            Fact("BestViewingTime", FirstNonEmpty(metadata.Fields.BestViewingWindowLocal, metadata.Timing.PeakLocal, metadata.Timing.ScheduledLocal), observation),
            Fact("ViewingWindow", FirstNonEmpty(metadata.Fields.BestViewingWindowLocal, FormatWindow(metadata.DerivedFacts.EventWindowUtc)), observation),
            Fact("Direction", metadata.Fields.SkyDirectionHint, observation),
            Fact("Altitude", null, false),
            Fact("Constellation", null, false),
            Fact("Brightness", null, false),
            Fact("MoonInterference", metadata.Fields.MoonInterference, false),
            Fact("Visibility", metadata.Fields.VisibilityRegion, false),
            Fact("RelativePositions", metadata.DerivedFacts.AngularSeparation, false));

        var warnings = MissingWarnings(required).ToArray();
        var observationFacts = ToObservationFacts(required);
        return new SceneIntent(sceneId, purpose, request.Language, eventType, eventName, required, observationFacts,
            FirstNonEmpty(GetString(scene, "narrationIntent"), GetString(scene, "narrationText"), GetString(scene, "caption"), $"Explain the {purpose.ToLowerInvariant()} role without adding unsupported facts.")!,
            FirstNonEmpty(GetString(scene, "visualIntent"), GetString(scene, "visualPrompt"), $"Show a scientifically respectful {purpose.ToLowerInvariant()} visual for {eventName}.")!,
            ["Do not invent missing facts.", "Use observation-metadata.json as the factual source for observation details.", "Surface missing metadata as warnings."],
            FirstNonEmpty(GetString(scene, "editorialTone"), "Clear, accurate, practical, and wonder-driven")!, warnings);
    }

    private static string? ToLocal(string? utcValue, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(utcValue) || string.IsNullOrWhiteSpace(timeZoneId) || !DateTimeOffset.TryParse(utcValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var utc)) return null;
        try { return TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).ToString("O", CultureInfo.InvariantCulture); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    private static void WarnIfMissing(List<string> warnings, string name, string? value) { if (string.IsNullOrWhiteSpace(value)) warnings.Add($"Missing metadata for {name}."); }
    private static string? FormatWindow(EventWindowUtc? window)
    {
        if (window is null || FirstNonEmpty(window.StartUtc, window.EndUtc) is null) return null;
        return $"{window.StartUtc ?? "unknown"} to {window.EndUtc ?? "unknown"}";
    }
    private static SceneIntentFact Fact(string name, string? value, bool highPriority) => new(name, string.IsNullOrWhiteSpace(value) ? null : value, highPriority ? "High" : "Normal", string.IsNullOrWhiteSpace(value));
    private static IEnumerable<string> MissingWarnings(SceneIntentRequiredFacts facts) => new[] { facts.EventDate, facts.BestViewingTime, facts.ViewingWindow, facts.Direction, facts.Altitude, facts.Constellation, facts.Brightness, facts.MoonInterference, facts.Visibility, facts.RelativePositions }.Where(f => f.IsMissing).Select(f => $"Missing metadata for {f.Name}.");
    private static IReadOnlyDictionary<string, string> ToObservationFacts(SceneIntentRequiredFacts facts) => new[] { facts.EventDate, facts.BestViewingTime, facts.ViewingWindow, facts.Direction, facts.Altitude, facts.Constellation, facts.Brightness, facts.MoonInterference, facts.Visibility, facts.RelativePositions }.Where(f => !f.IsMissing && f.Value is not null).ToDictionary(f => f.Name, f => f.Value!);
    private static string InferPurpose(string sceneId) => sceneId.Contains("observ", StringComparison.OrdinalIgnoreCase) || sceneId.Contains("view", StringComparison.OrdinalIgnoreCase) ? "Observation" : "Editorial";
    private static JsonElement? ReadFirstJson(string path) { if (!File.Exists(path)) return null; using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.Clone(); }
    private static List<JsonElement> ReadSceneElements(JsonElement? scenePlan)
    {
        var scenes = new List<JsonElement>();
        if (scenePlan.HasValue) AddScenes(scenePlan.Value, scenes);
        return scenes.GroupBy(s => FirstNonEmpty(GetString(s, "sceneId"), GetString(s, "id"), s.GetRawText())).Select(g => g.First()).ToList();
    }
    private static void AddScenes(JsonElement element, List<JsonElement> scenes)
    {
        if (element.ValueKind == JsonValueKind.Array) { foreach (var item in element.EnumerateArray()) AddScenes(item, scenes); return; }
        if (element.ValueKind != JsonValueKind.Object) return;
        if (GetString(element, "sceneId") is not null || GetString(element, "sceneType") is not null) scenes.Add(element.Clone());
        foreach (var property in element.EnumerateObject().Where(p => p.Name.Contains("scene", StringComparison.OrdinalIgnoreCase))) AddScenes(property.Value, scenes);
    }
    private static string? GetString(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e) return null;
        foreach (var property in e.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(property.Value);
        }
        return null;
    }
    private static string? FindString(JsonElement? element, string name)
    {
        if (!element.HasValue) return null;
        if (element.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.Value.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(property.Value);
                var nested = FindString(property.Value, name);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        if (element.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.Value.EnumerateArray())
            {
                var nested = FindString(item, name);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return null;
    }
    private static IReadOnlyList<string> FindStringArray(JsonElement? element, string name)
    {
        if (!element.HasValue) return [];
        if (element.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.Value.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToStringArray(property.Value);
                var nested = FindStringArray(property.Value, name);
                if (nested.Count > 0) return nested;
            }
        }
        if (element.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.Value.EnumerateArray())
            {
                var nested = FindStringArray(item, name);
                if (nested.Count > 0) return nested;
            }
        }
        return [];
    }
    private static IReadOnlyList<string> ValueToStringArray(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(ValueToString).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToArray() : ValueToString(value) is { } single ? [single] : [];
    private static string? ValueToString(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
}

public sealed record ObservationMetadata(ObservationTimingFacts Timing, ObservationObjectFacts ObjectFacts, ObservationFields Fields, ObservationDerivedFacts DerivedFacts, IReadOnlyList<string> MissingFactWarnings);
public sealed record ObservationTimingFacts(string? StartUtc, string? StartLocal, string? PeakUtc, string? PeakLocal, string? EndUtc, string? EndLocal, string? ScheduledUtc, string? ScheduledLocal, string? TimeZone);
public sealed record ObservationObjectFacts(IReadOnlyList<string> PrimaryObjects, IReadOnlyList<string> SecondaryObjects, string? AngularSeparationDegrees);
public sealed record ObservationFields(string? BestViewingWindowLocal, string? SkyDirectionHint, string? MoonInterference, string? MoonIlluminationPercent, string? VisibilityRegion);
public sealed record ObservationDerivedFacts(EventWindowUtc? EventWindowUtc, string? PeakLocal, string? AngularSeparation, IReadOnlyList<string> ObjectPair);
public sealed record EventWindowUtc(string? StartUtc, string? EndUtc);

public sealed record SceneIntentBuilderResult(IReadOnlyList<SceneIntent> SceneIntents, IReadOnlyList<string> GeneratedFiles)
{
    public static SceneIntentBuilderResult Empty { get; } = new([], []);
}
