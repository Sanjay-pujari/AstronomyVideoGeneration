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
        var diagnosticsRoot = Path.Combine(outputRoot, "editorial");
        Directory.CreateDirectory(diagnosticsRoot);
        var observationMetadataPath = Path.Combine(diagnosticsRoot, "observation-metadata.json");
        var storyGraphPath = Path.Combine(diagnosticsRoot, "story-graph.json");
        var sceneIntentsPath = Path.Combine(diagnosticsRoot, "scene-intents.json");
        var editorialContractPath = Path.Combine(diagnosticsRoot, "editorial-contract.json");
        var diagnosticsPath = Path.Combine(diagnosticsRoot, "editorial-diagnostics.json");
        var storyGraph = BuildStoryGraph(request, response, planInput, intelligence, questionAnswerSet, observationMetadata, sceneElements);
        var intents = storyGraph.Scenes.Select(scene => BuildIntent(request, storyGraph, observationMetadata, scene)).ToArray();
        var inputFiles = new[] { productionIntelligencePath, questionAnswerSetPath, scenePlanPath, observationMetadataPath, storyGraphPath, sceneIntentsPath };
        var contract = BuildEditorialContract(request, response, observationMetadata, storyGraph, intents);
        var allWarnings = observationMetadata.MissingFactWarnings.Concat(storyGraph.MissingFactWarnings).Concat(intents.SelectMany(intent => intent.MissingFactWarnings)).Concat(contract.MissingFactWarnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await File.WriteAllTextAsync(observationMetadataPath, JsonSerializer.Serialize(observationMetadata, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(storyGraphPath, JsonSerializer.Serialize(storyGraph, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(sceneIntentsPath, JsonSerializer.Serialize(intents, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(editorialContractPath, JsonSerializer.Serialize(contract, JsonOptions), cancellationToken);

        var diagnostics = new
        {
            phaseNo = 5,
            phaseName = PhaseName,
            orchestrationVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion,
            subPhases = new[]
            {
                "5.1 Observation Metadata Builder",
                "5.2A Story Graph Builder",
                "5.2B Scene Intent Builder",
                "5.3 Editorial Contract Builder",
                "5.4 Editorial Diagnostics"
            },
            inputs = inputFiles.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) }).ToArray(),
            outputs = new[] { NormalizePath(observationMetadataPath), NormalizePath(storyGraphPath), NormalizePath(sceneIntentsPath), NormalizePath(editorialContractPath), NormalizePath(diagnosticsPath) },
            storyGraphCreated = File.Exists(storyGraphPath),
            storySceneCount = storyGraph.Scenes.Count,
            sceneIntentCount = intents.Length,
            missingFactWarningCount = allWarnings.Length,
            missingFactWarnings = allWarnings,
            questionAnswerSetLoaded = questionAnswerSet.HasValue,
            scenePlanLoaded = scenePlan.HasValue,
            productionEventIntelligenceLoaded = intelligence.HasValue
        };
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);

        logger.LogInformation("Editorial Intelligence Foundation wrote observation metadata, {SceneIntentCount} scene intents, and diagnostics to {DiagnosticsPath}.", intents.Length, diagnosticsPath);
        return new SceneIntentBuilderResult(intents, [observationMetadataPath, storyGraphPath, sceneIntentsPath, editorialContractPath, diagnosticsPath]);
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

    private static StoryGraph BuildStoryGraph(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, JsonElement? planInput, JsonElement? intelligence, JsonElement? questionAnswerSet, ObservationMetadata metadata, IReadOnlyList<JsonElement> sourceScenes)
    {
        var eventType = FirstNonEmpty(GetString(planInput, "eventType"), GetString(intelligence, "eventType"), response.SelectedPlans.FirstOrDefault()?.ContentCategoryCode, "Unknown")!;
        var eventName = FirstNonEmpty(GetString(planInput, "title"), GetString(intelligence, "title"), response.Title, response.SelectedPlans.FirstOrDefault()?.Title, "Unknown")!;
        var warnings = new List<string>();
        if (sourceScenes.Count < 5) warnings.Add($"Scene plan contains {sourceScenes.Count} scene(s); expected up to 5 canonical story scenes. Created only available scenes.");

        var scenes = sourceScenes.Select((scene, index) => BuildStoryGraphScene(scene, index, eventName, metadata, warnings)).ToArray();
        var transitions = scenes.Where((_, index) => index < scenes.Length - 1)
            .Select((scene, index) => new StoryGraphTransition(scene.SceneId, scenes[index + 1].SceneId, scene.TransitionToNext))
            .ToArray();
        var requiredFacts = BuildRequiredObservationFacts(metadata);

        warnings.AddRange(metadata.MissingFactWarnings);
        return new StoryGraph(
            "AstroPulse-StoryGraph-v1",
            Rc2PipelinePhaseRegistry.OrchestrationVersion,
            eventType,
            eventName,
            request.Language,
            request.RegionId,
            BuildStoryArc(scenes),
            scenes,
            transitions,
            requiredFacts,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static StoryGraphScene BuildStoryGraphScene(JsonElement scene, int index, string eventName, ObservationMetadata metadata, List<string> warnings)
    {
        var fallbackPurpose = FallbackPurpose(index);
        var sourcePurpose = FirstNonEmpty(GetString(scene, "scenePurpose"), GetString(scene, "purpose"), GetString(scene, "sceneType"), GetString(scene, "segment"));
        var purpose = IsSpecificPurpose(sourcePurpose) ? NormalizePurpose(sourcePurpose!) : fallbackPurpose;
        if (!string.IsNullOrWhiteSpace(sourcePurpose) && !IsSpecificPurpose(sourcePurpose)) warnings.Add($"Source scene purpose '{sourcePurpose}' for scene {index + 1} was generic or unknown; used fallback purpose {fallbackPurpose}.");

        var sceneId = FirstNonEmpty(GetString(scene, "sceneId"), GetString(scene, "id"), GetString(scene, "sourceSceneId"), $"scene-{index + 1:000}")!;
        var questionId = FirstNonEmpty(GetString(scene, "questionId"), GetString(scene, "sourceQuestionId"), GetString(scene, "sourceQuestion"));
        var keyQuestion = FirstNonEmpty(GetString(scene, "keyQuestion"), GetString(scene, "question"), GetString(scene, "sourceQuestionText"));
        var keyMessage = FirstNonEmpty(GetString(scene, "keyMessage"), GetString(scene, "viewerTakeaway"), GetString(scene, "takeaway"), GetString(scene, "sourceAnswer"), GetString(scene, "answer"), $"Explain the {purpose.ToLowerInvariant()} of {eventName} using only supported observation metadata.")!;
        var requiredFacts = BuildRequiredObservationFacts(metadata);
        var transition = FirstNonEmpty(GetString(scene, "transitionToNext"), index < 4 ? $"Move from {purpose} to {FallbackPurpose(index + 1)}." : "Close the story without adding unsupported facts.")!;

        return new StoryGraphScene(
            sceneId,
            purpose,
            index + 1,
            questionId,
            FirstNonEmpty(GetString(scene, "sourceSceneId"), sceneId),
            keyQuestion,
            keyMessage,
            requiredFacts,
            FirstNonEmpty(GetString(scene, "narrationRole"), GetString(scene, "narrationIntent"), $"Narrate the {purpose.ToLowerInvariant()} beat with factual restraint.")!,
            FirstNonEmpty(GetString(scene, "visualRole"), GetString(scene, "visualIntent"), $"Visualize the {purpose.ToLowerInvariant()} beat for {eventName}.")!,
            FirstNonEmpty(GetString(scene, "motionRole"), GetString(scene, "motionIntent"), "Support comprehension with calm editorial motion.")!,
            transition);
    }

    private static SceneIntent BuildIntent(BatchGenerateFromPlansRequest request, StoryGraph storyGraph, ObservationMetadata metadata, StoryGraphScene scene)
    {
        var observation = string.Equals(scene.ScenePurpose, "Observation", StringComparison.OrdinalIgnoreCase);
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
        return new SceneIntent(scene.SceneId, scene.ScenePurpose, request.Language, storyGraph.EventType, storyGraph.EventName, required, observationFacts,
            scene.NarrationRole,
            scene.VisualRole,
            ["Do not invent missing facts.", "Use observation-metadata.json as the factual source for observation details.", "Use editorial/story-graph.json as the story structure source.", "Surface missing metadata as warnings."],
            "Clear, accurate, practical, and wonder-driven", warnings);
    }


    private static EditorialContract BuildEditorialContract(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, ObservationMetadata metadata, StoryGraph storyGraph, IReadOnlyList<SceneIntent> intents)
    {
        var firstIntent = intents.FirstOrDefault();
        var eventType = FirstNonEmpty(firstIntent?.EventType, response.SelectedPlans.FirstOrDefault()?.ContentCategoryCode, "Unknown")!;
        var eventName = FirstNonEmpty(firstIntent?.EventName, response.Title, response.SelectedPlans.FirstOrDefault()?.Title, "Unknown")!;
        var warnings = new List<string>();

        EditorialContractFact FactValue(string name, object? value)
        {
            var missing = value switch
            {
                null => true,
                string text => string.IsNullOrWhiteSpace(text),
                IReadOnlyCollection<string> collection => collection.Count == 0,
                _ => false
            };
            if (missing) warnings.Add($"Missing metadata for {name}.");
            return new EditorialContractFact(missing ? null : value, missing, missing ? null : "editorial/observation-metadata.json");
        }

        var eventFacts = new EditorialContractEventFacts(
            FactValue("startUtc", metadata.Timing.StartUtc),
            FactValue("peakUtc", metadata.Timing.PeakUtc),
            FactValue("endUtc", metadata.Timing.EndUtc),
            FactValue("startLocal", metadata.Timing.StartLocal),
            FactValue("peakLocal", metadata.Timing.PeakLocal),
            FactValue("endLocal", metadata.Timing.EndLocal),
            FactValue("primaryObjects", metadata.ObjectFacts.PrimaryObjects),
            FactValue("secondaryObjects", metadata.ObjectFacts.SecondaryObjects),
            FactValue("angularSeparationDegrees", metadata.ObjectFacts.AngularSeparationDegrees));

        var observationFacts = new EditorialContractObservationFacts(
            FactValue("bestViewingWindowLocal", metadata.Fields.BestViewingWindowLocal),
            FactValue("skyDirectionHint", metadata.Fields.SkyDirectionHint),
            FactValue("visibilityRegion", metadata.Fields.VisibilityRegion),
            FactValue("moonInterference", metadata.Fields.MoonInterference),
            FactValue("moonIlluminationPercent", metadata.Fields.MoonIlluminationPercent),
            FactValue("altitude", null),
            FactValue("azimuth", null),
            FactValue("constellation", null),
            FactValue("brightness", null),
            FactValue("elongation", null),
            FactValue("moonPhase", null),
            FactValue("nakedEyeVisibility", null),
            FactValue("binocularVisibility", null),
            FactValue("telescopeVisibility", null),
            FactValue("weatherConfidence", null),
            FactValue("lightPollution", null));

        var confidenceCues = new List<string>();
        if (!string.IsNullOrWhiteSpace(metadata.Fields.BestViewingWindowLocal)) confidenceCues.Add("Use the supported local viewing window when giving observation guidance.");
        if (!string.IsNullOrWhiteSpace(metadata.Fields.SkyDirectionHint)) confidenceCues.Add("Use the supported sky direction hint for where to look.");
        if (!string.IsNullOrWhiteSpace(metadata.Fields.MoonInterference)) confidenceCues.Add("Mention moon interference only in the qualified form supplied by metadata.");

        var requiredNarrationFacts = intents.SelectMany(intent => new[] { intent.RequiredFacts.EventDate, intent.RequiredFacts.BestViewingTime, intent.RequiredFacts.ViewingWindow, intent.RequiredFacts.Direction, intent.RequiredFacts.MoonInterference, intent.RequiredFacts.Visibility, intent.RequiredFacts.RelativePositions }).Where(f => !f.IsMissing).GroupBy(f => f.Name).Select(g => g.First()).ToArray();
        var requiredVisualFacts = storyGraph.RequiredObservationFacts.Select(f => new EditorialContractFact(f.Value, false, "editorial/story-graph.json"))
            .Concat(intents.SelectMany(intent => intent.ObservationFacts.Select(f => new EditorialContractFact(f.Value, false, "editorial/observation-metadata.json"))))
            .ToArray();

        warnings.AddRange(metadata.MissingFactWarnings);
        warnings.AddRange(intents.SelectMany(intent => intent.MissingFactWarnings));

        return new EditorialContract(
            "AstroPulse-EditorialContract-v1",
            Rc2PipelinePhaseRegistry.OrchestrationVersion,
            "AstroPulse-StyleGuide-v1",
            "CalmDocumentary",
            eventType,
            eventName,
            request.Language,
            request.RegionId,
            eventFacts,
            observationFacts,
            new StoryGraphSummary(storyGraph.StoryGraphVersion, storyGraph.StoryArc, storyGraph.Scenes.Count, storyGraph.Scenes.Select(s => new StoryGraphSceneSummary(s.SceneId, s.ScenePurpose, s.SceneOrder, s.KeyMessage)).ToArray()),
            intents,
            requiredNarrationFacts,
            requiredVisualFacts,
            confidenceCues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ["appears", "look toward", "visible near", "reaches its highest point", "brighter object", "steady glow", "clear skies"],
            ["insane", "crazy", "unbelievable", "magical", "mind-blowing", "once in a lifetime", "shocking", "you won’t believe"],
            new EditorialChannelIdentity("AstroPulse", "Until next time, keep looking up."),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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
    private static IReadOnlyDictionary<string, string> BuildRequiredObservationFacts(ObservationMetadata metadata)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddFact(facts, "EventDate", FirstNonEmpty(metadata.Timing.PeakUtc, metadata.Timing.StartUtc, metadata.Timing.ScheduledUtc));
        AddFact(facts, "BestViewingTime", FirstNonEmpty(metadata.Fields.BestViewingWindowLocal, metadata.Timing.PeakLocal, metadata.Timing.ScheduledLocal));
        AddFact(facts, "ViewingWindow", FirstNonEmpty(metadata.Fields.BestViewingWindowLocal, FormatWindow(metadata.DerivedFacts.EventWindowUtc)));
        AddFact(facts, "Direction", metadata.Fields.SkyDirectionHint);
        AddFact(facts, "MoonInterference", metadata.Fields.MoonInterference);
        AddFact(facts, "Visibility", metadata.Fields.VisibilityRegion);
        AddFact(facts, "RelativePositions", metadata.DerivedFacts.AngularSeparation);
        return facts;
    }
    private static void AddFact(Dictionary<string, string> facts, string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) facts[name] = value; }
    private static string BuildStoryArc(IReadOnlyList<StoryGraphScene> scenes) => scenes.Count == 0 ? "No source scenes available from question-driven scene plan." : string.Join(" → ", scenes.OrderBy(s => s.SceneOrder).Select(s => s.ScenePurpose));
    private static string FallbackPurpose(int index) => index switch { 0 => "Hook", 1 => "Discovery", 2 => "Science", 3 => "Observation", 4 => "Takeaway", _ => "SupportingDetail" };
    private static bool IsSpecificPurpose(string? purpose) => NormalizePurpose(purpose) is "Hook" or "Discovery" or "Science" or "Observation" or "Takeaway" or "SupportingDetail";
    private static string? NormalizePurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose)) return null;
        var compact = new string(purpose.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return compact switch
        {
            "hook" => "Hook",
            "discovery" => "Discovery",
            "science" => "Science",
            "observation" or "viewing" or "observing" => "Observation",
            "takeaway" or "summary" or "closing" => "Takeaway",
            "supportingdetail" or "detail" => "SupportingDetail",
            _ => null
        };
    }
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
        if (GetString(element, "sceneId") is not null || GetString(element, "sceneType") is not null || GetString(element, "sceneNumber") is not null || GetString(element, "questionId") is not null) scenes.Add(element.Clone());
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


public sealed record StoryGraph(
    string StoryGraphVersion,
    string OrchestrationVersion,
    string EventType,
    string EventName,
    string Language,
    string RegionId,
    string StoryArc,
    IReadOnlyList<StoryGraphScene> Scenes,
    IReadOnlyList<StoryGraphTransition> Transitions,
    IReadOnlyDictionary<string, string> RequiredObservationFacts,
    IReadOnlyList<string> MissingFactWarnings);

public sealed record StoryGraphScene(
    string SceneId,
    string ScenePurpose,
    int SceneOrder,
    string? SourceQuestionId,
    string? SourceSceneId,
    string? KeyQuestion,
    string KeyMessage,
    IReadOnlyDictionary<string, string> RequiredFacts,
    string NarrationRole,
    string VisualRole,
    string MotionRole,
    string TransitionToNext);

public sealed record StoryGraphTransition(string FromSceneId, string ToSceneId, string Transition);
public sealed record StoryGraphSummary(string StoryGraphVersion, string StoryArc, int SceneCount, IReadOnlyList<StoryGraphSceneSummary> Scenes);
public sealed record StoryGraphSceneSummary(string SceneId, string ScenePurpose, int SceneOrder, string KeyMessage);

public sealed record EditorialContract(
    string ContractVersion,
    string OrchestrationVersion,
    string StyleGuideVersion,
    string VoiceProfile,
    string EventType,
    string EventName,
    string Language,
    string RegionId,
    EditorialContractEventFacts EventFacts,
    EditorialContractObservationFacts ObservationFacts,
    StoryGraphSummary StoryGraph,
    IReadOnlyList<SceneIntent> SceneIntents,
    IReadOnlyList<SceneIntentFact> RequiredNarrationFacts,
    IReadOnlyList<EditorialContractFact> RequiredVisualFacts,
    IReadOnlyList<string> ConfidenceCues,
    IReadOnlyList<string> PreferredPhrases,
    IReadOnlyList<string> ProhibitedPhrases,
    EditorialChannelIdentity ChannelIdentity,
    IReadOnlyList<string> MissingFactWarnings);

public sealed record EditorialContractFact(object? Value, bool IsMissing, string? Source);

public sealed record EditorialContractEventFacts(
    EditorialContractFact StartUtc,
    EditorialContractFact PeakUtc,
    EditorialContractFact EndUtc,
    EditorialContractFact StartLocal,
    EditorialContractFact PeakLocal,
    EditorialContractFact EndLocal,
    EditorialContractFact PrimaryObjects,
    EditorialContractFact SecondaryObjects,
    EditorialContractFact AngularSeparationDegrees);

public sealed record EditorialContractObservationFacts(
    EditorialContractFact BestViewingWindowLocal,
    EditorialContractFact SkyDirectionHint,
    EditorialContractFact VisibilityRegion,
    EditorialContractFact MoonInterference,
    EditorialContractFact MoonIlluminationPercent,
    EditorialContractFact Altitude,
    EditorialContractFact Azimuth,
    EditorialContractFact Constellation,
    EditorialContractFact Brightness,
    EditorialContractFact Elongation,
    EditorialContractFact MoonPhase,
    EditorialContractFact NakedEyeVisibility,
    EditorialContractFact BinocularVisibility,
    EditorialContractFact TelescopeVisibility,
    EditorialContractFact WeatherConfidence,
    EditorialContractFact LightPollution);

public sealed record EditorialChannelIdentity(string ChannelName, string DefaultEnding);

public sealed record SceneIntentBuilderResult(IReadOnlyList<SceneIntent> SceneIntents, IReadOnlyList<string> GeneratedFiles)
{
    public static SceneIntentBuilderResult Empty { get; } = new([], []);
}
