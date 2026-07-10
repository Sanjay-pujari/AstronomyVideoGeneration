using System.Globalization;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class SceneIntentBuilder(ILogger<SceneIntentBuilder> logger)
{
    private const string PhaseName = "Editorial Intelligence";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<SceneIntentBuilderResult> BuildAndWriteDiagnosticsAsync(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, CancellationToken cancellationToken)
    {
        logger.LogInformation("Editorial Intelligence executed for RC2 batch generation. OutputRoot={OutputRoot}; Success={Success}", response.OutputRoot, response.Success);

        if (string.IsNullOrWhiteSpace(response.OutputRoot))
        {
            logger.LogWarning("Editorial Intelligence skipped diagnostics because RC2 response did not include an OutputRoot.");
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
        var storyGraph = ReadStoryGraph(storyGraphPath) ?? throw new InvalidOperationException("Phase 5 Editorial Intelligence requires Phase 4 output editorial/story-graph.json.");
        var intents = storyGraph.Scenes.Select(scene => BuildIntent(request, storyGraph, observationMetadata, scene)).ToArray();
        var phase5Validation = ValidateSceneIntents(intents);
        var inputFiles = new[] { productionIntelligencePath, storyGraphPath };
        var contract = BuildEditorialContract(request, response, observationMetadata, storyGraph, intents);
        var allWarnings = observationMetadata.MissingFactWarnings.Concat(storyGraph.MissingFactWarnings).Concat(intents.SelectMany(intent => intent.MissingFactWarnings)).Concat(contract.MissingFactWarnings).Concat(phase5Validation).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await File.WriteAllTextAsync(observationMetadataPath, JsonSerializer.Serialize(observationMetadata, JsonOptions), cancellationToken);
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
                "5.2 Scene Intent Builder",
                "5.3 Editorial Contract Builder",
                "5.4 Editorial Diagnostics"
            },
            inputs = inputFiles.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) }).ToArray(),
            outputs = new[] { NormalizePath(observationMetadataPath), NormalizePath(sceneIntentsPath), NormalizePath(editorialContractPath), NormalizePath(diagnosticsPath) },
            storyGraphConsumed = File.Exists(storyGraphPath),
            storySceneCount = storyGraph.Scenes.Count,
            sceneIntentCount = intents.Length,
            missingFactWarningCount = allWarnings.Length,
            missingFactWarnings = allWarnings,
            questionAnswerSetLoaded = questionAnswerSet.HasValue,
            scenePlanLoaded = scenePlan.HasValue,
            productionEventIntelligenceLoaded = intelligence.HasValue,
            narrativeArchetype = storyGraph.NarrativeArchetype,
            sourceQuestionCount = storyGraph.SourceQuestionCount,
            semanticBeatCount = storyGraph.SemanticBeatCount,
            mergedQuestionCount = storyGraph.MergedQuestionCount,
            splitQuestionCount = storyGraph.SplitQuestionCount,
            globalFactCount = storyGraph.RequiredObservationFacts.Count,
            allocatedFactCountByScene = intents.ToDictionary(i => i.SceneId, i => i.AllocatedFacts.Count),
            duplicateFactAllocationWarnings = DuplicateFactWarnings(intents),
            misclassifiedFactWarnings = MisclassifiedFactWarnings(storyGraph, intents),
            fixedTemplateUsed = IsFixedUniversalTemplate(storyGraph.Scenes)
        };
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);

        logger.LogInformation("Editorial Intelligence wrote observation metadata, {SceneIntentCount} scene intents, and diagnostics to {DiagnosticsPath}.", intents.Length, diagnosticsPath);
        return new SceneIntentBuilderResult(intents, [observationMetadataPath, sceneIntentsPath, editorialContractPath, diagnosticsPath]);
    }

    public async Task<StoryGraphBuilderResult> BuildAndWriteStoryGraphAsync(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(response.OutputRoot)) return StoryGraphBuilderResult.Empty;
        var outputRoot = response.OutputRoot!;
        var planInput = ReadFirstJson(Path.Combine(outputRoot, "plan-input", "content-plan-production-request.json"));
        var intelligence = ReadFirstJson(Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json"));
        var questionAnswerSetPath = Path.Combine(outputRoot, "question-engine", "question-answer-set.json");
        var scenePlanPath = Path.Combine(outputRoot, "question-engine", "question-driven-scene-plan.enriched.json");
        if (!File.Exists(scenePlanPath)) scenePlanPath = Path.Combine(outputRoot, "question-engine", "question-driven-scene-plan.json");
        var questionAnswerSet = ReadFirstJson(questionAnswerSetPath);
        var scenePlan = ReadFirstJson(scenePlanPath);
        var observationMetadata = BuildObservationMetadata(planInput, intelligence);
        var storyGraph = BuildStoryGraph(request, response, planInput, intelligence, questionAnswerSet, observationMetadata, ReadSceneElements(scenePlan));
        var editorialRoot = Path.Combine(outputRoot, "editorial");
        Directory.CreateDirectory(editorialRoot);
        var storyGraphPath = Path.Combine(editorialRoot, "story-graph.json");
        await File.WriteAllTextAsync(storyGraphPath, JsonSerializer.Serialize(storyGraph, JsonOptions), cancellationToken);
        return new StoryGraphBuilderResult(storyGraph, [storyGraphPath]);
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
        var globalFacts = BuildRequiredObservationFacts(metadata);
        var sourceQuestionCount = sourceScenes.Select(s => FirstNonEmpty(GetString(s, "questionId"), GetString(s, "sourceQuestionId"), GetString(s, "keyQuestion"), GetString(s, "question"))).Where(q => !string.IsNullOrWhiteSpace(q)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var beats = DeriveSemanticBeats(sourceScenes, eventType, eventName, metadata, warnings);
        var fixedTemplateUsed = IsFixedUniversalTemplate(beats);
        if (fixedTemplateUsed) warnings.Add("Story graph validation failed: fixed universal six-node template detected.");
        if (beats.Any(b => b.ScenePurpose.Equals("Science", StringComparison.OrdinalIgnoreCase) && b.RequiredFactKeys.Any(IsTimingOrOrientationKey) && !b.RequiredFactKeys.Any(IsScienceKey))) warnings.Add("Story graph validation failed: Science beat was assigned timing/orientation as its primary purpose.");
        if (globalFacts.TryGetValue("Visibility", out var visibility) && LooksLikeRegion(visibility)) warnings.Add("Story graph validation failed: region identifier was classified as Visibility.");

        var scenes = beats.Select((beat, index) => beat with { SceneOrder = index + 1 }).ToArray();
        var transitions = scenes.Where((_, index) => index < scenes.Length - 1)
            .Select((scene, index) => new StoryGraphTransition(scene.SceneId, scenes[index + 1].SceneId, scene.TransitionToNext))
            .ToArray();

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
            globalFacts,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            NarrativeArchetype(eventType, sourceScenes.Count, metadata),
            sourceQuestionCount,
            scenes.Length,
            Math.Max(0, sourceScenes.Count - scenes.Length),
            CountSplitQuestions(sourceScenes));
    }

    private static IReadOnlyList<StoryGraphScene> DeriveSemanticBeats(IReadOnlyList<JsonElement> sourceScenes, string eventType, string eventName, ObservationMetadata metadata, List<string> warnings)
    {
        var beats = new List<StoryGraphScene>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in sourceScenes)
        {
            var keyQuestion = FirstNonEmpty(GetString(scene, "keyQuestion"), GetString(scene, "question"), GetString(scene, "sourceQuestionText"));
            var sourcePurpose = FirstNonEmpty(GetString(scene, "scenePurpose"), GetString(scene, "purpose"), GetString(scene, "sceneType"), GetString(scene, "segment"));
            var message = FirstNonEmpty(GetString(scene, "keyMessage"), GetString(scene, "viewerTakeaway"), GetString(scene, "takeaway"), GetString(scene, "sourceAnswer"), GetString(scene, "answer"));
            var purpose = NormalizePurpose(sourcePurpose) ?? InferNarrativeRole(keyQuestion, message, eventType, beats.Count == 0);
            var normalized = NormalizeQuestion(keyQuestion ?? message ?? purpose);
            if (!seen.Add(purpose + ":" + normalized)) { warnings.Add($"Merged overlapping audience question into {purpose} beat: {keyQuestion ?? message ?? "unknown"}."); continue; }
            beats.Add(BuildStoryGraphScene(scene, beats.Count, eventName, metadata, warnings, purpose));
        }
        if (beats.Count == 0)
        {
            beats.Add(BuildSyntheticBeat("beat-hook", "Hook", eventName, metadata));
            if (HasTiming(metadata)) beats.Add(BuildSyntheticBeat("beat-timing", "Timing", eventName, metadata));
            if (HasScience(metadata)) beats.Add(BuildSyntheticBeat("beat-science", "Science", eventName, metadata));
            if (HasObservation(metadata)) beats.Add(BuildSyntheticBeat("beat-observation", "Observation", eventName, metadata));
            beats.Add(BuildSyntheticBeat("beat-closing", "Closing", eventName, metadata));
        }
        return beats;
    }

    private static StoryGraphScene BuildStoryGraphScene(JsonElement scene, int index, string eventName, ObservationMetadata metadata, List<string> warnings, string purpose)
    {
        var sceneId = FirstNonEmpty(GetString(scene, "sceneId"), GetString(scene, "id"), GetString(scene, "sourceSceneId"), $"beat-{index + 1:000}")!;
        var questionId = FirstNonEmpty(GetString(scene, "questionId"), GetString(scene, "sourceQuestionId"), GetString(scene, "sourceQuestion"));
        var keyQuestion = FirstNonEmpty(GetString(scene, "keyQuestion"), GetString(scene, "question"), GetString(scene, "sourceQuestionText"));
        var keyMessage = FirstNonEmpty(GetString(scene, "keyMessage"), GetString(scene, "viewerTakeaway"), GetString(scene, "takeaway"), GetString(scene, "sourceAnswer"), GetString(scene, "answer"), $"Explain the {purpose.ToLowerInvariant()} of {eventName} using only supported observation metadata.")!;
        var requiredKeys = RequiredKeysForPurpose(purpose, metadata);
        if (requiredKeys.Count == 0) warnings.Add($"Story graph validation failed: unsupported empty {purpose} beat would have no fact ownership.");
        var optionalKeys = OptionalKeysForPurpose(purpose, metadata).Except(requiredKeys, StringComparer.OrdinalIgnoreCase).ToArray();
        var transition = FirstNonEmpty(GetString(scene, "transitionToNext"), $"Move from {purpose} to the next audience need without repeating the complete fact bundle.")!;
        return new StoryGraphScene(sceneId, purpose, index + 1, questionId, FirstNonEmpty(GetString(scene, "sourceSceneId"), sceneId), keyQuestion, keyMessage, requiredKeys, optionalKeys, AudienceOutcome(purpose), purpose, transition, FirstNonEmpty(GetString(scene, "narrationRole"), GetString(scene, "narrationIntent"), $"Narrate the {purpose.ToLowerInvariant()} beat with factual restraint.")!, FirstNonEmpty(GetString(scene, "visualRole"), GetString(scene, "visualIntent"), $"Visualize the {purpose.ToLowerInvariant()} beat for {eventName}.")!, FirstNonEmpty(GetString(scene, "motionRole"), GetString(scene, "motionIntent"), "Support comprehension with calm editorial motion.")!, transition);
    }

    private static SceneIntent BuildIntent(BatchGenerateFromPlansRequest request, StoryGraph storyGraph, ObservationMetadata metadata, StoryGraphScene scene)
    {
        var requiredKeys = scene.RequiredFactKeys.Count > 0 ? scene.RequiredFactKeys : RequiredKeysForPurpose(scene.ScenePurpose, metadata);
        var optionalKeys = scene.OptionalFactKeys;
        var allocated = AllocateFacts(requiredKeys.Concat(optionalKeys).Distinct(StringComparer.OrdinalIgnoreCase), metadata);
        var required = new SceneIntentRequiredFacts(
            Fact("EventDate", allocated.GetValueOrDefault("EventDate"), IsOwnedBy(scene.ScenePurpose, "EventDate")),
            Fact("BestViewingTime", allocated.GetValueOrDefault("BestViewingTime"), IsOwnedBy(scene.ScenePurpose, "BestViewingTime")),
            Fact("ViewingWindow", allocated.GetValueOrDefault("ViewingWindow"), IsOwnedBy(scene.ScenePurpose, "ViewingWindow")),
            Fact("Direction", allocated.GetValueOrDefault("Direction"), IsOwnedBy(scene.ScenePurpose, "Direction")),
            Fact("Altitude", allocated.GetValueOrDefault("Altitude"), false),
            Fact("Constellation", allocated.GetValueOrDefault("Constellation"), false),
            Fact("Brightness", allocated.GetValueOrDefault("Brightness"), false),
            Fact("MoonInterference", allocated.GetValueOrDefault("MoonInterference"), IsOwnedBy(scene.ScenePurpose, "MoonInterference")),
            Fact("Visibility", allocated.GetValueOrDefault("Visibility"), IsOwnedBy(scene.ScenePurpose, "Visibility")),
            Fact("RelativePositions", allocated.GetValueOrDefault("RelativePositions"), IsOwnedBy(scene.ScenePurpose, "RelativePositions")));

        var missingRequired = requiredKeys.Where(key => !allocated.ContainsKey(key) || string.IsNullOrWhiteSpace(allocated[key])).ToArray();
        var warnings = MissingWarnings(required).Concat(missingRequired.Select(f => $"Missing required fact for {scene.ScenePurpose}: {f}."));
        return new SceneIntent(scene.SceneId, scene.ScenePurpose, request.Language, storyGraph.EventType, storyGraph.EventName, required, allocated,
            requiredKeys.ToArray(), optionalKeys.ToArray(), missingRequired,
            scene.NarrationRole,
            scene.VisualRole,
            ["Do not invent missing facts.", "Use only allocatedFacts for this scene's narration facts.", "Use observation-metadata.json only to verify allocated fact values.", "Do not move timing/orientation facts into Science unless explaining physical geometry."],
            "Clear, accurate, practical, and wonder-driven", warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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
            .Concat(intents.SelectMany(intent => intent.AllocatedFacts.Select(f => new EditorialContractFact(f.Value, false, "editorial/observation-metadata.json"))))
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
    private static IEnumerable<string> MissingWarnings(SceneIntentRequiredFacts facts) => new[] { facts.EventDate, facts.BestViewingTime, facts.ViewingWindow, facts.Direction, facts.Altitude, facts.Constellation, facts.Brightness, facts.MoonInterference, facts.Visibility, facts.RelativePositions }.Where(f => f.IsMissing && string.Equals(f.Priority, "High", StringComparison.OrdinalIgnoreCase)).Select(f => $"Missing metadata for {f.Name}.");
    private static IReadOnlyDictionary<string, string> ToObservationFacts(SceneIntentRequiredFacts facts) => new[] { facts.EventDate, facts.BestViewingTime, facts.ViewingWindow, facts.Direction, facts.Altitude, facts.Constellation, facts.Brightness, facts.MoonInterference, facts.Visibility, facts.RelativePositions }.Where(f => !f.IsMissing && f.Value is not null).ToDictionary(f => f.Name, f => f.Value!);
    private static IReadOnlyDictionary<string, string> BuildRequiredObservationFacts(ObservationMetadata metadata)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddFact(facts, "EventDate", FirstNonEmpty(metadata.Timing.PeakUtc, metadata.Timing.StartUtc, metadata.Timing.ScheduledUtc));
        AddFact(facts, "BestViewingTime", FirstNonEmpty(metadata.Fields.BestViewingWindowLocal, metadata.Timing.PeakLocal, metadata.Timing.ScheduledLocal));
        AddFact(facts, "ViewingWindow", FirstNonEmpty(metadata.Fields.BestViewingWindowLocal, FormatWindow(metadata.DerivedFacts.EventWindowUtc)));
        AddFact(facts, "Direction", metadata.Fields.SkyDirectionHint);
        AddFact(facts, "MoonInterference", metadata.Fields.MoonInterference);
        AddFact(facts, "VisibilityRegion", metadata.Fields.VisibilityRegion);
        AddFact(facts, "RelativePositions", metadata.DerivedFacts.AngularSeparation);
        return facts;
    }
    private static void AddFact(Dictionary<string, string> facts, string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) facts[name] = value; }
    private static string BuildStoryArc(IReadOnlyList<StoryGraphScene> scenes) => scenes.Count == 0 ? "No source scenes available from question-driven scene plan." : string.Join(" → ", scenes.OrderBy(s => s.SceneOrder).Select(s => s.ScenePurpose));
    private static string FallbackPurpose(int index) => index switch { 0 => "Hook", 1 => "Timing", 2 => "Science", 3 => "Observation", 4 => "Closing", _ => "Significance" };
    private static bool IsSpecificPurpose(string? purpose) => NormalizePurpose(purpose) is "Hook" or "Discovery" or "Timing" or "Orientation" or "Science" or "Observation" or "Significance" or "Closing" or "Takeaway" or "SupportingDetail";
    private static string? NormalizePurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose)) return null;
        var compact = new string(purpose.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return compact switch
        {
            "hook" => "Hook",
            "discovery" => "Discovery",
            "timing" or "date" or "viewingwindow" => "Timing",
            "orientation" or "direction" or "location" => "Orientation",
            "science" or "explanation" => "Science",
            "observation" or "viewing" or "observing" => "Observation",
            "significance" or "importance" => "Significance",
            "takeaway" or "summary" or "closing" => "Closing",
            "supportingdetail" or "detail" => "Significance",
            _ => null
        };
    }
    private static StoryGraphScene BuildSyntheticBeat(string id, string purpose, string eventName, ObservationMetadata metadata)
        => new(id, purpose, 1, null, id, null, $"Cover the {purpose.ToLowerInvariant()} need for {eventName}.", RequiredKeysForPurpose(purpose, metadata), OptionalKeysForPurpose(purpose, metadata), AudienceOutcome(purpose), purpose, $"Transition from {purpose} without duplicating global facts.", $"Narrate the {purpose.ToLowerInvariant()} beat with factual restraint.", $"Visualize the {purpose.ToLowerInvariant()} beat for {eventName}.", "Support comprehension with calm editorial motion.", $"Transition from {purpose} without duplicating global facts.");
    private static string InferNarrativeRole(string? question, string? message, string eventType, bool first)
    {
        if (first) return "Hook";
        var text = ((question ?? "") + " " + (message ?? "")).ToLowerInvariant();
        if (text.Contains("when") || text.Contains("date") || text.Contains("time") || text.Contains("window") || text.Contains("peak")) return "Timing";
        if (text.Contains("where") || text.Contains("direction") || text.Contains("look") || text.Contains("region") || text.Contains("location")) return "Orientation";
        if (text.Contains("why") || text.Contains("how does") || text.Contains("science") || text.Contains("happen") || text.Contains("separation") || text.Contains("orbit")) return "Science";
        if (text.Contains("see") || text.Contains("observe") || text.Contains("binocular") || text.Contains("telescope") || text.Contains("conditions")) return "Observation";
        if (text.Contains("matter") || text.Contains("important") || text.Contains("rare") || text.Contains("significance")) return "Significance";
        if (text.Contains("takeaway") || text.Contains("next") || text.Contains("action")) return "Closing";
        return eventType.Contains("Event", StringComparison.OrdinalIgnoreCase) ? "Significance" : "Science";
    }
    private static IReadOnlyList<string> RequiredKeysForPurpose(string purpose, ObservationMetadata metadata) => purpose switch
    {
        "Hook" => metadata.ObjectFacts.SecondaryObjects.Count > 0 ? ["PrimaryObjects", "SecondaryObjects"] : ["PrimaryObjects"],
        "Timing" => ["EventDate", "ViewingWindow"],
        "Orientation" => ["Direction", "VisibilityRegion"],
        "Science" => ["RelativePositions"],
        "Observation" => ["BestViewingTime", "Direction", "MoonInterference"],
        "Significance" => ["PrimaryObjects", "RelativePositions"],
        "Closing" or "Takeaway" => ["BestViewingTime"],
        "Discovery" => ["PrimaryObjects", "EventDate"],
        _ => ["PrimaryObjects"]
    };
    private static IReadOnlyList<string> OptionalKeysForPurpose(string purpose, ObservationMetadata metadata) => purpose switch
    {
        "Hook" => ["EventDate"],
        "Timing" => ["StartUtc", "PeakUtc", "EndUtc", "BestViewingTime"],
        "Orientation" => ["Altitude", "Azimuth", "Constellation"],
        "Science" => ["PrimaryObjects", "SecondaryObjects"],
        "Observation" => ["Visibility", "Brightness", "MoonIlluminationPercent"],
        "Closing" or "Takeaway" => ["Direction"],
        _ => []
    };
    private static IReadOnlyDictionary<string,string> AllocateFacts(IEnumerable<string> keys, ObservationMetadata metadata)
    {
        var facts = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys) AddFact(facts, key, FactValueForKey(key, metadata));
        return facts;
    }
    private static string? FactValueForKey(string key, ObservationMetadata metadata) => key switch
    {
        "PrimaryObjects" => metadata.ObjectFacts.PrimaryObjects.Count == 0 ? null : string.Join(", ", metadata.ObjectFacts.PrimaryObjects),
        "SecondaryObjects" => metadata.ObjectFacts.SecondaryObjects.Count == 0 ? null : string.Join(", ", metadata.ObjectFacts.SecondaryObjects),
        "EventDate" => FirstNonEmpty(metadata.Timing.PeakUtc, metadata.Timing.StartUtc, metadata.Timing.ScheduledUtc),
        "StartUtc" => metadata.Timing.StartUtc,
        "PeakUtc" => metadata.Timing.PeakUtc,
        "EndUtc" => metadata.Timing.EndUtc,
        "BestViewingTime" => FirstNonEmpty(metadata.Fields.BestViewingWindowLocal, metadata.Timing.PeakLocal, metadata.Timing.ScheduledLocal),
        "ViewingWindow" => FirstNonEmpty(metadata.Fields.BestViewingWindowLocal, FormatWindow(metadata.DerivedFacts.EventWindowUtc)),
        "Direction" => metadata.Fields.SkyDirectionHint,
        "VisibilityRegion" => metadata.Fields.VisibilityRegion,
        "MoonInterference" => metadata.Fields.MoonInterference,
        "MoonIlluminationPercent" => metadata.Fields.MoonIlluminationPercent,
        "RelativePositions" => metadata.DerivedFacts.AngularSeparation,
        "Visibility" => null,
        _ => null
    };
    private static bool IsOwnedBy(string purpose, string key) => purpose switch
    {
        "Timing" => key is "EventDate" or "ViewingWindow" or "BestViewingTime",
        "Orientation" => key is "Direction",
        "Science" => key is "RelativePositions",
        "Observation" => key is "BestViewingTime" or "Direction" or "MoonInterference" or "Visibility",
        _ => false
    };
    private static string AudienceOutcome(string purpose) => purpose switch { "Timing" => "Know when the event occurs and when to watch.", "Orientation" => "Know where to look and which region/location context applies.", "Science" => "Understand the physical or perspective explanation.", "Observation" => "Know how to identify and observe the objects.", "Significance" => "Understand why the subject matters.", "Closing" or "Takeaway" => "Leave with a clear observation action or takeaway.", _ => "Understand why this subject is worth watching." };
    private static string NormalizeQuestion(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static bool HasTiming(ObservationMetadata m) => FirstNonEmpty(m.Timing.StartUtc, m.Timing.PeakUtc, m.Fields.BestViewingWindowLocal) is not null;
    private static bool HasScience(ObservationMetadata m) => FirstNonEmpty(m.ObjectFacts.AngularSeparationDegrees) is not null;
    private static bool HasObservation(ObservationMetadata m) => FirstNonEmpty(m.Fields.SkyDirectionHint, m.Fields.MoonInterference) is not null;
    private static bool IsTimingOrOrientationKey(string key) => key is "EventDate" or "ViewingWindow" or "BestViewingTime" or "Direction" or "VisibilityRegion";
    private static bool IsScienceKey(string key) => key is "RelativePositions" or "AngularSeparation";
    private static bool LooksLikeRegion(string value) => value.Contains("United States", StringComparison.OrdinalIgnoreCase) || value.Contains("Region", StringComparison.OrdinalIgnoreCase) || value.Length == 2;
    private static string NarrativeArchetype(string eventType, int sourceSceneCount, ObservationMetadata metadata) => eventType.Contains("Conjunction", StringComparison.OrdinalIgnoreCase) ? "event-observation-science" : sourceSceneCount > 0 ? "question-led-semantic" : "metadata-led-semantic";
    private static int CountSplitQuestions(IReadOnlyList<JsonElement> scenes) => scenes.Count(s => (GetString(s, "keyQuestion") ?? GetString(s, "question") ?? "").Contains(" and ", StringComparison.OrdinalIgnoreCase));
    private static bool IsFixedUniversalTemplate(IReadOnlyList<StoryGraphScene> scenes) => string.Join("→", scenes.Select(s => s.ScenePurpose)) == "Hook→Discovery→Science→Observation→Takeaway→SupportingDetail";
    private static IReadOnlyList<string> DuplicateFactWarnings(IReadOnlyList<SceneIntent> intents) => intents.Count > 1 && intents.Select(i => string.Join('|', i.AllocatedFacts.Keys.OrderBy(k => k))).Distinct().Count() == 1 ? ["Phase 5 validation failed: identical allocated fact sets appear across all scene intents."] : [];
    private static IReadOnlyList<string> MisclassifiedFactWarnings(StoryGraph storyGraph, IReadOnlyList<SceneIntent> intents)
    {
        var warnings = new List<string>();
        if (storyGraph.RequiredObservationFacts.TryGetValue("Visibility", out var v) && LooksLikeRegion(v)) warnings.Add("Visibility contains a region identifier; use VisibilityRegion for location/region values.");
        foreach (var i in intents.Where(i => i.ScenePurpose == "Science" && !i.AllocatedFacts.ContainsKey("RelativePositions") && (i.AllocatedFacts.ContainsKey("EventDate") || i.AllocatedFacts.ContainsKey("ViewingWindow")))) warnings.Add($"Science scene {i.SceneId} lacks scientific facts but contains timing facts.");
        return warnings;
    }
    private static IReadOnlyList<string> ValidateSceneIntents(IReadOnlyList<SceneIntent> intents) => DuplicateFactWarnings(intents).Concat(MisclassifiedFactWarnings(new StoryGraph("","","","","","","",[],[],new Dictionary<string,string>(),[] ,"",0,0,0,0), intents)).ToArray();

    private static StoryGraph? ReadStoryGraph(string path)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<StoryGraph>(File.ReadAllText(path), JsonOptions);
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
    IReadOnlyList<string> MissingFactWarnings,
    string NarrativeArchetype,
    int SourceQuestionCount,
    int SemanticBeatCount,
    int MergedQuestionCount,
    int SplitQuestionCount);

public sealed record StoryGraphScene(
    string SceneId,
    string ScenePurpose,
    int SceneOrder,
    string? SourceQuestionId,
    string? SourceSceneId,
    string? KeyQuestion,
    string KeyMessage,
    IReadOnlyList<string> RequiredFactKeys,
    IReadOnlyList<string> OptionalFactKeys,
    string AudienceOutcome,
    string NarrativeRole,
    string TransitionIntent,
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

public sealed record StoryGraphBuilderResult(StoryGraph? StoryGraph, IReadOnlyList<string> GeneratedFiles)
{
    public static StoryGraphBuilderResult Empty { get; } = new(null, []);
}

public sealed record SceneIntentBuilderResult(IReadOnlyList<SceneIntent> SceneIntents, IReadOnlyList<string> GeneratedFiles)
{
    public static SceneIntentBuilderResult Empty { get; } = new([], []);
}
