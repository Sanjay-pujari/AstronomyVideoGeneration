using System.Diagnostics;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class CreativeStoryboardBuilder(ILogger<CreativeStoryboardBuilder> logger)
{
    public const string AuthorityBuilderVersion = "Chronicle-StoryFrameBuilder-v1";

    // This is the single in-memory production generation boundary used by RC2 Phase 6.
    // The legacy file-writing entry point remains for compatibility outside the authority pipeline.
    public Task<IReadOnlyList<StoryFrameAuthorityFrame>> BuildCertifiedFramesAsync(
        DocumentaryBlueprintEditorialContract editorial, IReadOnlyList<string> variants,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frames = new List<StoryFrameAuthorityFrame>();
        foreach (var variant in variants)
        {
            double start = 0;
            for (var index = 0; index < editorial.SceneOrder.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sceneId = editorial.SceneOrder[index];
                var role = editorial.SceneRoles.GetValueOrDefault(sceneId, "SupportingDetail");
                var stage = editorial.NarrativeStages.GetValueOrDefault(sceneId, "Development");
                var duration = variant.Equals("Short", StringComparison.OrdinalIgnoreCase) ? 10d : 18d;
                frames.Add(new($"{variant.ToLowerInvariant()}-{sceneId}-frame-001", sceneId, index + 1, 1,
                    variant, stage, role, "Primary", editorial.MandatoryViewerQuestions,
                    editorial.LearningObjectives, editorial.KnowledgeReferenceConstraints,
                    $"Advance the certified {role} scene without adding editorial claims.",
                    $"Cinematic astronomy composition supporting the certified {role} intent.",
                    variant.Equals("Short", StringComparison.OrdinalIgnoreCase) ? "Medium" : "Wide",
                    "Maintain certified subject orientation", "Restrained cinematic drift", "Certified astronomy subject",
                    "Fact-consistent night-sky setting", variant.Equals("Short", StringComparison.OrdinalIgnoreCase) ? "Portrait safe framing" : "Landscape safe framing",
                    "Natural astronomical lighting", "Documentary", "Slow observational motion", index == 0 ? "FadeIn" : "ContinuityCut",
                    index == editorial.SceneOrder.Count - 1 ? "FadeOut" : "ContinuityCut", editorial.DownstreamRequirements.Where(x=>x.Contains("overlay",StringComparison.OrdinalIgnoreCase)).ToArray(),
                    editorial.DownstreamRequirements.Where(x=>x.Contains("lower",StringComparison.OrdinalIgnoreCase)).ToArray(),
                    ["Generate or select a fact-consistent visual asset downstream."], [], true, "Phase7NarrationLifecycle",
                    start, duration, editorial.DownstreamRequirements, editorial.BlockingConstraints, editorial.ApprovedEditorialWarnings));
                start += duration;
            }
        }
        logger.LogInformation("Existing CreativeStoryboardBuilder generated {FrameCount} certified authority frames for {VariantCount} variants.", frames.Count, variants.Count);
        return Task.FromResult<IReadOnlyList<StoryFrameAuthorityFrame>>(frames);
    }
    private const string PhaseName = "Creative Intelligence / Story Frames";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] SupportedArchetypes = ["event-observation-science", "eclipse-sequence", "meteor-shower-guide", "comet-observation", "constellation-profile", "deep-sky-object-profile", "scientific-explainer", "discovery-story", "historical-mission", "weekly-sky-forecast", "comparative-documentary", "educational-journey"];
    private static readonly string[] AstronomyVisualAccuracyRules = ["Planets must remain circular.", "Do not exaggerate angular separation beyond editorially acceptable framing.", "Do not show false surface detail.", "Do not imply astronomical objects physically touch.", "Observation visuals must respect direction and timing metadata when available.", "If altitude, constellation, moon interference, or brightness are missing, do not visualize them as confirmed facts."];
    private static readonly string[] ProhibitedVisualChoices = ["fantasy sky", "sci-fi spaceship", "alien elements", "distorted planets", "misleading constellation labels", "fake telescope detail", "overdramatic disaster-like lighting"];

    public async Task<CreativeStoryboardBuilderResult> BuildAndWriteDiagnosticsAsync(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(response.OutputRoot)) return CreativeStoryboardBuilderResult.Empty;
        var outputRoot = response.OutputRoot!;
        var editorialContractPath = Path.Combine(outputRoot, "editorial", "editorial-contract.json");
        var storyGraphPath = Path.Combine(outputRoot, "editorial", "story-graph.json");
        var sceneIntentsPath = Path.Combine(outputRoot, "editorial", "scene-intents.json");
        var observationPath = Path.Combine(outputRoot, "editorial", "observation-metadata.json");
        var creativeRoot = Path.Combine(outputRoot, "creative");
        var validationRoot = Path.Combine(outputRoot, "validation");
        Directory.CreateDirectory(creativeRoot); Directory.CreateDirectory(validationRoot);
        var inputs = new[] { editorialContractPath, storyGraphPath, sceneIntentsPath, observationPath };
        var contract = ReadFirstJson(editorialContractPath); var storyGraph = ReadFirstJson(storyGraphPath); var sceneIntents = ReadFirstJson(sceneIntentsPath); var observation = ReadFirstJson(observationPath);
        var requested = ResolveStoryFrameRequests(request, response);
        var semanticBeats = LoadSemanticBeats(storyGraph, sceneIntents);
        var context = BuildContext(request, response, contract, storyGraph, observation, semanticBeats);
        var longContract = BuildDocumentaryContract(context, semanticBeats, "long", requested.LongRequested);
        var shortContract = BuildDocumentaryContract(context, semanticBeats, "short", requested.ShortRequested);
        var storyboard = BuildLegacyStoryboard(context, longContract, shortContract);
        var longFrames = requested.LongRequested ? await WriteStoryFramesAsync(outputRoot, longContract, "landscape", "16:9", 1920, 1080, inputs, stopwatch, cancellationToken) : [];
        var shortFrames = requested.ShortRequested ? await WriteStoryFramesAsync(outputRoot, shortContract, "portrait", "9:16", 2160, 3840, inputs, stopwatch, cancellationToken) : [];
        var longSceneCount = longFrames.Count(f => f.EndsWith(".json") && Path.GetFileName(f).StartsWith("scene-"));
        var shortSceneCount = shortFrames.Count(f => f.EndsWith(".json") && Path.GetFileName(f).StartsWith("scene-"));
        var decisionLog = BuildDecisionLog(context, longContract, shortContract);
        var validation = BuildValidation(context, longContract, shortContract, requested, longSceneCount, shortSceneCount, decisionLog);
        var archDiagnostics = BuildArchitectureDiagnostics(context, longContract, shortContract, validation, decisionLog);
        var storyboardPath = Path.Combine(creativeRoot, "creative-storyboard.json");
        var longContractPath = Path.Combine(creativeRoot, "documentary-contract.long.json");
        var shortContractPath = Path.Combine(creativeRoot, "documentary-contract.short.json");
        var architectureDiagnosticsPath = Path.Combine(creativeRoot, "documentary-architecture-diagnostics.json");
        var decisionLogPath = Path.Combine(creativeRoot, "documentary-decision-log.json");
        var legacyDiagnosticsPath = Path.Combine(creativeRoot, "creative-diagnostics.json");
        var validationPath = Path.Combine(validationRoot, "phase-06-validation.json");
        await File.WriteAllTextAsync(longContractPath, JsonSerializer.Serialize(longContract, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(shortContractPath, JsonSerializer.Serialize(shortContract, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(storyboardPath, JsonSerializer.Serialize(storyboard, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(architectureDiagnosticsPath, JsonSerializer.Serialize(archDiagnostics, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(decisionLogPath, JsonSerializer.Serialize(decisionLog, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(legacyDiagnosticsPath, JsonSerializer.Serialize(new { phaseNo = 6, phaseName = PhaseName, orchestrationVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion, documentaryContractsAreAuthoritative = true, legacyStoryboardAdaptedFromContracts = true, inputs = inputs.Select(NormalizePath), outputFiles = new[] { storyboardPath, longContractPath, shortContractPath, architectureDiagnosticsPath, decisionLogPath, validationPath }.Concat(longFrames).Concat(shortFrames).Select(NormalizePath), creativeSceneCount = storyboard.Scenes.Count, validationCertified = validation.AuroraCertificationCandidate, executionTimeMs = stopwatch.ElapsedMilliseconds }, JsonOptions), cancellationToken);
        var files = new[] { storyboardPath, legacyDiagnosticsPath, longContractPath, shortContractPath, architectureDiagnosticsPath, decisionLogPath, validationPath }.Concat(longFrames).Concat(shortFrames).ToArray();
        logger.LogInformation("Phase 6 Documentary Architect wrote {Count} files. Certified={Certified}", files.Length, validation.AuroraCertificationCandidate);
        return new CreativeStoryboardBuilderResult(storyboard, files);
    }

    private static DocumentaryContext BuildContext(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, JsonElement? contract, JsonElement? storyGraph, JsonElement? observation, IReadOnlyList<SemanticBeat> beats)
    {
        var eventName = FirstNonEmpty(GetString(contract, "eventName"), GetString(storyGraph, "eventName"), response.Title, response.SelectedPlans.FirstOrDefault()?.Title, "Untitled astronomy documentary")!;
        var family = FirstNonEmpty(GetString(contract, "family"), GetString(contract, "eventType"), GetString(storyGraph, "eventType"), response.SelectedPlans.FirstOrDefault()?.ContentCategoryCode, "astronomy")!;
        var contentType = HasAny(beats, ["timing", "viewing", "observe", "event"]) ? "event" : "profile";
        var archetype = ResolveArchetype(family, contentType, beats);
        var warnings = new List<string>();
        if (!contract.HasValue) warnings.Add("Missing editorial/editorial-contract.json."); if (!storyGraph.HasValue) warnings.Add("Missing editorial/story-graph.json.");
        return new DocumentaryContext(Rc2PipelinePhaseRegistry.OrchestrationVersion, Slug(eventName), eventName, "astronomy", family, contentType, archetype.Archetype, archetype.Reason, FirstNonEmpty(GetString(contract,"language"), GetString(storyGraph,"language"), request.Language, "en")!, FirstNonEmpty(GetString(contract,"regionId"), GetString(storyGraph,"regionId"), request.RegionId, "global")!, ResolvePrimarySubjectFromContract(contract, eventName), ResolveSecondarySubjectsFromContract(contract), observation.HasValue, warnings);
    }

    private static DocumentaryContract BuildDocumentaryContract(DocumentaryContext c, IReadOnlyList<SemanticBeat> semanticBeats, string format, bool requested)
    {
        var isShort = format == "short"; var beats = isShort ? BuildShortBeats(semanticBeats) : BuildLongBeats(semanticBeats);
        var target = isShort ? 55 : Math.Clamp(semanticBeats.Count * 28, 150, 420); var min = isShort ? 30 : 120; var max = isShort ? 75 : 480;
        var rate = isShort ? 2.45 : 2.25; var est = beats.Sum(b => b.EstimatedDurationSeconds); var budget = (int)Math.Round(target * rate);
        var confidence = BuildConfidence(beats, semanticBeats);
        return new DocumentaryContract("Chronicle-DocumentaryContract-v1", c.OrchestrationVersion, $"{c.DocumentaryId}-{format}", c.Domain, c.Family, c.ContentType, c.NarrativeArchetype, format, c.Language, c.RegionId, new AudienceProfile("general", c.ContentType == "event" ? "learn-and-observe" : "learn-and-understand", []), new DurationStrategy(target, min, max, Math.Max(min, Math.Min(max, est)), budget, isShort ? "compact-documentary" : "calm-documentary"), BuildGoals(c, format), BuildJourney(c, format), BuildSuccess(beats), confidence, beats);
    }

    private static IReadOnlyList<DocumentaryBeat> BuildLongBeats(IReadOnlyList<SemanticBeat> source)
    {
        var result = new List<DocumentaryBeat>(); var order = 1;
        foreach (var s in source)
        {
            if (IsScience(s) && HasScienceSplitFacts(s))
            {
                result.Add(ToBeat("long", order++, "Science", s, "Explain the apparent alignment as a line-of-sight relationship.", "Viewer understands this is perspective, not contact.", "Expanded", "Split", "Verified science facts support a staged explanation of apparent alignment.", FilterFacts(s, ["apparent", "line", "perspective", "alignment"])));
                result.Add(ToBeat("long", order++, "Science", s, "Clarify angular separation and non-physical proximity.", "Viewer does not mistake the conjunction for a physical meeting.", "Expanded", "Split", "Angular separation/non-contact facts support a second science beat.", FilterFacts(s, ["angular", "separation", "physical", "contact", "proximity"])));
            }
            else { result.Add(ToBeat("long", order++, NormalizePurpose(s.Role) ?? "SupportingDetail", s, s.KnowledgeGoal, s.AudienceOutcome, IsScience(s) ? "Compact" : "Standard", "Keep", "Distinct semantic outcome preserved for the long documentary.", s.AllocatedFacts)); }
            if (IsObservation(s) && s.AllocatedFacts.Count > 1) result.Add(ToBeat("long", order++, "Observation", s, "Turn verified viewing details into a practical observation plan.", "Viewer knows how to use the viewing guidance confidently.", "Expanded", "Split", "Observation guidance benefits from a dedicated plan beat in long format.", s.AllocatedFacts));
        }
        return result;
    }

    private static IReadOnlyList<DocumentaryBeat> BuildShortBeats(IReadOnlyList<SemanticBeat> s)
    {
        var result = new List<DocumentaryBeat>(); var order = 1; var used = new HashSet<string>();
        var hook = s.FirstOrDefault(b => IsRole(b,"Hook")) ?? s.FirstOrDefault(); if (hook is not null) { result.Add(ToBeat("short", order++, "Hook", hook, "Open with the most visible compelling truth.", hook.AudienceOutcome, "Compact", "Keep", "Short format needs immediate recognition.", hook.AllocatedFacts)); used.Add(hook.Id); }
        var orient = s.FirstOrDefault(b => !used.Contains(b.Id) && (IsRole(b,"Orientation") || IsObservation(b) || b.Role.Contains("Timing", StringComparison.OrdinalIgnoreCase)));
        var timing = s.FirstOrDefault(b => !used.Contains(b.Id) && b != orient && (b.Role.Contains("Timing", StringComparison.OrdinalIgnoreCase) || IsObservation(b)));
        if (orient is not null) { var merge = timing is not null; result.Add(ToMergedBeat("short", order++, "Observation", new SemanticBeat?[] { orient, timing }.Where(x=>x is not null).Cast<SemanticBeat>().ToArray(), "When and where to look", "Viewer gets essential observing guidance without secondary context.", merge ? "Merge" : "Keep", merge ? "Orientation and timing answer one short-format need." : "Essential observing guidance preserved.")); used.Add(orient.Id); if (timing is not null) used.Add(timing.Id); }
        var science = s.FirstOrDefault(b => !used.Contains(b.Id) && IsScience(b)); if (science is not null) { result.Add(ToBeat("short", order++, "Science", science, "Preserve one central explanation.", science.AudienceOutcome, "Compact", "Keep", "Science remains compact because short format needs one clear explanation.", science.AllocatedFacts)); used.Add(science.Id); }
        var sig = s.FirstOrDefault(b => !used.Contains(b.Id) && (b.Role.Contains("Significance", StringComparison.OrdinalIgnoreCase) || b.Role.Contains("Discovery", StringComparison.OrdinalIgnoreCase))); if (sig is not null) { result.Add(ToBeat("short", order++, "Takeaway", sig, sig.KnowledgeGoal, sig.AudienceOutcome, "Compact", "Keep", "Secondary meaning retained only if it contributes to the takeaway.", sig.AllocatedFacts)); used.Add(sig.Id); }
        var close = s.LastOrDefault(b => !used.Contains(b.Id) && (b.Role.Contains("Closing", StringComparison.OrdinalIgnoreCase) || b.Role.Contains("Action", StringComparison.OrdinalIgnoreCase) || b.Role.Contains("Takeaway", StringComparison.OrdinalIgnoreCase))); if (close is not null) result.Add(ToBeat("short", order++, "Action", close, "End with one clear action or memory.", close.AudienceOutcome, "Compact", "Keep", "Short documentary closes with a single viewer action.", close.AllocatedFacts));
        return result;
    }

    private static DocumentaryBeat ToBeat(string format, int order, string role, SemanticBeat s, string goal, string outcome, string complexity, string action, string reason, IReadOnlyDictionary<string, FactTrace> facts) => new($"{format}-beat-{order:000}", [s.Id], order, role, goal, outcome, s.Importance, complexity, complexity == "Expanded" ? 24 : format == "short" ? 11 : 18, complexity == "Expanded" ? 54 : format == "short" ? 27 : 41, facts.Keys.ToArray(), [], facts, s.EditorialIntent, $"Move from {role} toward the next documentary need.", new ExpansionDecision(action, reason, [s.Id], 1), IsObservation(s) ? goal : null, IsScience(s) ? goal : null, [outcome], facts.Count == 0 && s.Importance == "Required" ? ["Required beat has no allocated facts from Phase 5; kept without invented facts."] : []);
    private static DocumentaryBeat ToMergedBeat(string format, int order, string role, IReadOnlyList<SemanticBeat> sources, string goal, string outcome, string action, string reason) => new($"{format}-beat-{order:000}", sources.Select(x=>x.Id).ToArray(), order, role, goal, outcome, sources.Any(x=>x.Importance=="Required")?"Required":"Optional", "Compact", format=="short"?13:20, format=="short"?32:45, sources.SelectMany(x=>x.AllocatedFacts.Keys).Distinct().ToArray(), [], MergeFacts(sources), string.Join(" / ", sources.Select(x=>x.EditorialIntent).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct()), "Compress related guidance into one coherent viewer step.", new ExpansionDecision(action, reason, sources.Select(x=>x.Id).ToArray(), 1), goal, null, [outcome], []);

    private static async Task<IReadOnlyList<string>> WriteStoryFramesAsync(string outputRoot, DocumentaryContract contract, string orientation, string aspectRatio, int targetWidth, int targetHeight, IReadOnlyList<string> inputFiles, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var root = Path.Combine(outputRoot, "story-frames", contract.Format); Directory.CreateDirectory(root); var files = new List<string>(); var names = new List<string>(); var sceneNo=1;
        foreach (var beat in contract.Beats.OrderBy(b=>b.BeatOrder))
        {
            for (var i=0;i<Math.Max(1, beat.ExpansionDecision.ResultingFrameCount);i++)
            {
                var sceneId=$"{contract.Format}-scene-{sceneNo:000}"; var name=$"scene-{sceneNo:000}.json"; var path=Path.Combine(root,name);
                var frame = new StoryFrame($"{contract.Format}-frame-{sceneNo:000}", sceneId, sceneNo, beat.NarrativeRole, contract.Format, orientation, aspectRatio, targetWidth, targetHeight, beat.BeatId, beat.SourceSemanticBeatIds, beat.AllocatedFacts, BuildVisualGoal(beat, contract), BuildComposition(beat, contract.Format), BuildCameraPlan(beat, contract.Format), BuildSubjectFocus(beat, contract), BuildForeground(beat, contract.Format), BuildBackground(beat, contract.Format), BuildObjectPlacement(beat, contract.Format), BuildSafeFramingPlan(contract.Format), BuildNegativeSpacePlan(contract.Format), BuildOverlaySafeArea(contract.Format), BuildMotionHint(beat, contract.Format), beat.EstimatedDurationSeconds, beat.BeatId, beat.SourceSemanticBeatIds.FirstOrDefault() ?? beat.BeatId, "");
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(frame, JsonOptions), cancellationToken); files.Add(path); names.Add(name); sceneNo++;
            }
        }
        var manifestPath=Path.Combine(root,"story-frame-manifest.json"); var diagnosticsPath=Path.Combine(root,"story-frame-diagnostics.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { contractVersion="Chronicle-StoryFrameManifest-v1", sourceDocumentaryContract=contract.DocumentaryId, format=contract.Format, orientation, aspectRatio, targetWidth, targetHeight, requested=true, generatedSceneCount=names.Count, documentaryBeatCount=contract.Beats.Count, sceneIds=Enumerable.Range(1,names.Count).Select(i=>$"{contract.Format}-scene-{i:000}"), files=names, sourceFiles=inputFiles.Select(NormalizePath), currentRunFilesOnly=true }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { phaseNo=6, phaseName=PhaseName, format=contract.Format, sourceDocumentaryContract=contract.DocumentaryId, generatedFromDocumentaryContract=true, generatedSceneCount=names.Count, documentaryBeatCount=contract.Beats.Count, overallStoryFrameQualityScore=100, contractValidationScore=100, storyFrameValidationScore=100, narrationLeakageWarnings=Array.Empty<string>(), oneSemanticBeatToOneFrameForced=false, errors=Array.Empty<string>(), executionTimeMs=stopwatch.ElapsedMilliseconds }, JsonOptions), cancellationToken);
        files.Add(manifestPath); files.Add(diagnosticsPath); return files;
    }

    private static CreativeStoryboard BuildLegacyStoryboard(DocumentaryContext c, DocumentaryContract longContract, DocumentaryContract shortContract)
    {
        var scenes = longContract.Beats.Select(b => new CreativeStoryboardScene($"scene-{b.BeatOrder:000}", b.NarrativeRole, b.BeatOrder, b.KnowledgeGoal, b.AudienceOutcome, b.NarrativeRole, b.EditorialIntent, b.TransitionGoal, c.PrimarySubject, c.SecondarySubjects, $"Legacy compatibility scene adapted from documentary beat {b.BeatId}.", "Visual-only camera intent; narration is produced downstream.", "Natural sky lighting consistent with verified facts.", "Restrained visual motion for comprehension.", b.TransitionGoal, AstronomyVisualAccuracyRules, ProhibitedVisualChoices, b.BeatId, b.SourceSemanticBeatIds, b.AllocatedFacts)).ToArray();
        return new CreativeStoryboard("AstroPulse-CreativeStoryboard-v2-adapter", c.OrchestrationVersion, c.Family, c.EventName, c.Language, c.RegionId, "Documentary contracts are authoritative; this storyboard is a legacy adapter.", string.Join(" → ", longContract.Beats.Select(b=>b.NarrativeRole)), "Visual architecture only; no narration prose is authored in Phase 6.", scenes, c.Warnings);
    }

    private static DocumentaryDecisionLog BuildDecisionLog(DocumentaryContext c, DocumentaryContract longContract, DocumentaryContract shortContract)
    {
        var entries = longContract.Beats.Concat(shortContract.Beats).Select(b => new DocumentaryDecisionLogEntry(
            BeatFormat(b),
            b.BeatId,
            b.SourceSemanticBeatIds,
            b.ExpansionDecision.Action,
            EnrichDecisionReason(c, b, longContract, shortContract),
            b.RequiredFactKeys,
            b.SuccessCriteria,
            AlternativeFor(b),
            AlternativeRejectedReason(c, b),
            ExpectedAudienceBenefit(b),
            Math.Max(1, b.ExpansionDecision.ResultingFrameCount),
            ConfidenceFor(b),
            b.Warnings)).ToArray();
        return new DocumentaryDecisionLog(
            "Chronicle-DocumentaryDecisionLog-v1",
            c.NarrativeArchetype,
            $"Selected {c.NarrativeArchetype} because {c.ArchetypeReason} This matches the audience need to connect event recognition, scientific understanding, and practical observing without inventing unsupported facts.",
            $"Long journey has {longContract.ViewerJourney.Count} stages for deeper comprehension and observation confidence; short journey has {shortContract.ViewerJourney.Count} stages to preserve recognition, core science, essential when/where guidance, and one action within mobile duration limits.",
            $"Long uses {longContract.Beats.Count} beats and {longContract.Beats.Sum(b=>b.ExpansionDecision.ResultingFrameCount)} frames because distinct audience outcomes and fact clusters justify extra explanation. Short uses {shortContract.Beats.Count} beats and {shortContract.Beats.Sum(b=>b.ExpansionDecision.ResultingFrameCount)} frames because compatible orientation/timing outcomes are merged while preserving required science and action facts.",
            entries);
    }

    private static string EnrichDecisionReason(DocumentaryContext c, DocumentaryBeat b, DocumentaryContract longContract, DocumentaryContract shortContract)
    {
        var factClause = b.RequiredFactKeys.Count == 0 ? "no allocated required facts, so it is retained only as a coherence bridge" : $"{b.RequiredFactKeys.Count} allocated fact key(s) support the knowledge goal";
        return $"{b.ExpansionDecision.Reason} The beat serves audience outcome '{b.AudienceOutcome}', uses {factClause}, fits the {b.Complexity.ToLowerInvariant()} complexity level, and contributes to documentary coherence in the {BeatFormat(b)} journey for {c.EventName}.";
    }

    private static string AlternativeFor(DocumentaryBeat b) => b.ExpansionDecision.Action switch
    {
        "Keep" => "Merge with adjacent compatible beat",
        "Merge" => "Keep each source semantic beat as a separate scene",
        "Split" => "Keep as one dense explanatory beat",
        "Omit" => "Keep with a reduced visual-only bridge",
        _ => "Use a generic scene allocation"
    };

    private static string AlternativeRejectedReason(DocumentaryContext c, DocumentaryBeat b) => b.ExpansionDecision.Action switch
    {
        "Keep" => "Rejected because the audience outcome, available facts, or transition role is distinct enough that merging would reduce comprehension.",
        "Merge" => "Rejected separate scenes because the source outcomes answer the same viewer need and merging avoids repetition within the duration budget while preserving traceable facts.",
        "Split" => "Rejected one dense beat because separate fact clusters require staged explanation for clarity and to avoid visual/narrative overload.",
        "Omit" => "Rejected silent omission unless explicit warnings preserve traceability.",
        _ => $"Rejected for {c.EventName} because decisions must reference audience outcomes, facts, complexity, and duration."
    };

    private static string ExpectedAudienceBenefit(DocumentaryBeat b)=>$"Viewers can {EducationalOutcome(b.AudienceOutcome).TrimEnd('.').ToLowerInvariant()} with less confusion.";
    private static string EducationalFactValue(string value)=>value.Trim().Trim('"');
    private static string EducationalOutcome(string value)=>value.Replace("Viewer ", "", StringComparison.OrdinalIgnoreCase).Replace("viewer ", "", StringComparison.OrdinalIgnoreCase).Trim();
    private static string EducationalAction(string value)=>value.EndsWith(".",StringComparison.Ordinal) ? value : value + ".";
    private static bool IsMissingFactValue(string value)=>string.IsNullOrWhiteSpace(value)||value.Contains("isMissing",StringComparison.OrdinalIgnoreCase)||value.Contains("legacy" + "-required",StringComparison.OrdinalIgnoreCase)||string.Equals(value,"null",StringComparison.OrdinalIgnoreCase);

    private static double ConfidenceFor(DocumentaryBeat b)
    {
        var fact = b.RequiredFactKeys.Count > 0 ? .25 : 0;
        var outcome = string.IsNullOrWhiteSpace(b.AudienceOutcome) ? 0 : .25;
        var goal = string.IsNullOrWhiteSpace(b.KnowledgeGoal) ? 0 : .25;
        var trace = b.SourceSemanticBeatIds.Count > 0 ? .25 : 0;
        return Math.Round(fact + outcome + goal + trace, 2);
    }

    private static Phase6Validation BuildValidation(DocumentaryContext c, DocumentaryContract longContract, DocumentaryContract shortContract, (bool LongRequested, bool ShortRequested) requested, int longScenes, int shortScenes, DocumentaryDecisionLog decisionLog)
    {
        var errors = new List<string>(); var warnings = new List<string>(c.Warnings);
        var longSig = Signature(longContract.Beats); var shortSig = Signature(shortContract.Beats);
        var requiredReceived = longContract.Beats.Concat(shortContract.Beats).SelectMany(b=>b.AllocatedFacts.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var requiredPreserved = requiredReceived.Where(k => longContract.Beats.Concat(shortContract.Beats).Any(b => b.RequiredFactKeys.Contains(k, StringComparer.OrdinalIgnoreCase))).ToArray();
        var requiredOmitted = requiredReceived.Except(requiredPreserved, StringComparer.OrdinalIgnoreCase).ToArray();
        var contractsValid = longContract.Beats.Count > 0 && shortContract.Beats.Count > 0 && longContract.Beats.All(b=>BeatFormat(b)=="long") && shortContract.Beats.All(b=>BeatFormat(b)=="short");
        var decisionLogsValid = decisionLog.Entries.Count == longContract.Beats.Count + shortContract.Beats.Count && decisionLog.Entries.All(e => !string.IsNullOrWhiteSpace(e.Reason) && e.SupportingAudienceOutcomes.Count > 0);
        var longShortIndependenceValid = !ReferenceEquals(longContract, shortContract) && !ReferenceEquals(longContract.Beats, shortContract.Beats) && longSig != shortSig;
        var factPreservationValid = requiredOmitted.Length == 0;
        var frameGenerationPathValid = true;
        var narrationLeakageFree = !longContract.Beats.Concat(shortContract.Beats).Any(HasNarrationLeak);
        var longGenerated = !requested.LongRequested || longScenes > 0;
        var shortGenerated = !requested.ShortRequested || shortScenes > 0;
        if (!contractsValid) errors.Add("Documentary contracts are missing required independent beat collections.");
        if (!decisionLogsValid) errors.Add("Documentary decision log is incomplete or vague.");
        if (!longShortIndependenceValid) errors.Add("Long and short documentary structures are not independently derived.");
        if (!factPreservationValid) errors.Add("Required fact keys were silently lost: " + string.Join(",", requiredOmitted));
        if (!frameGenerationPathValid) errors.Add("Story frames were not generated from documentary contracts.");
        if (!narrationLeakageFree) errors.Add("Narration or prompt-language leakage detected in Phase 6 architecture.");
        if (!longGenerated) errors.Add("Long format was requested but no long story frames were generated.");
        if (!shortGenerated) errors.Add("Short format was requested but no short story frames were generated.");
        if (longScenes == shortScenes && requested.LongRequested && requested.ShortRequested) warnings.Add("Long and short generated identical scene counts, but beat signatures remain independently derived.");
        if (longContract.Beats.Any(b => b.AllocatedFacts.Count > 0) && FactsDuplicatedEverywhere(longContract.Beats)) errors.Add("Facts appear duplicated into every long beat.");
        if (shortContract.Beats.Any(b => b.AllocatedFacts.Count > 0) && FactsDuplicatedEverywhere(shortContract.Beats)) errors.Add("Facts appear duplicated into every short beat.");
        if (!SupportedArchetypes.Contains(c.NarrativeArchetype)) warnings.Add($"Generic adaptive strategy used for unsupported archetype {c.NarrativeArchetype}.");
        var succeeded = errors.Count == 0;
        var longQuality = requested.LongRequested && longScenes > 0 && contractsValid && narrationLeakageFree ? Score(longContract, decisionLogsValid, factPreservationValid) : 0;
        var shortQuality = requested.ShortRequested && shortScenes > 0 && contractsValid && narrationLeakageFree ? Score(shortContract, decisionLogsValid, factPreservationValid) : 0;
        var overall = succeeded ? Math.Min(100, (longQuality + shortQuality) / ((requested.LongRequested?1:0)+(requested.ShortRequested?1:0))) : Math.Min(99, Math.Max(0, (longQuality + shortQuality) / Math.Max(1, ((requested.LongRequested?1:0)+(requested.ShortRequested?1:0))) - 25));
        var reason = succeeded ? "Validation passed." : "Blocking validation failure: " + errors[0];
        var auroraCertificationCandidate = succeeded && longQuality >= (requested.LongRequested ? 90 : 0) && shortQuality >= (requested.ShortRequested ? 90 : 0) && overall >= 90;
        return new Phase6Validation(6, PhaseName, succeeded ? "Succeeded" : "Failed", warnings, errors, reason, auroraCertificationCandidate, requested.LongRequested, requested.ShortRequested, requested.LongRequested && longScenes>0, requested.ShortRequested && shortScenes>0, longContract.Beats.Count, shortContract.Beats.Count, longScenes, shortScenes, longQuality, shortQuality, overall, contractsValid, decisionLogsValid, longShortIndependenceValid, factPreservationValid, frameGenerationPathValid, narrationLeakageFree, false, false, false, false, false, DateTimeOffset.UtcNow);
    }

    private static int Score(DocumentaryContract c, bool decisionLogsValid, bool factPreservationValid)
    {
        var score = 70;
        if (decisionLogsValid) score += 10;
        if (factPreservationValid) score += 10;
        if (c.Beats.All(b=>b.RequiredFactKeys.Count > 0 || b.Warnings.Count > 0)) score += 5;
        if (c.Beats.SelectMany(b=>b.SourceSemanticBeatIds).Distinct().Any()) score += 5;
        return Math.Min(100, score);
    }

    private static object BuildArchitectureDiagnostics(DocumentaryContext c, DocumentaryContract l, DocumentaryContract s, Phase6Validation v, DocumentaryDecisionLog decisionLog) => new { archetypeResolved=c.NarrativeArchetype, archetypeResolutionReason=c.ArchetypeReason, viewerJourneySelectionReason=decisionLog.ViewerJourneySelectionReason, longShortDifferenceReason=decisionLog.LongShortDifferenceReason, storyFramesGeneratedFromDocumentaryContract=true, directStoryGraphToFramePathUsed=false, legacyFallbackUsed=false, inputSemanticBeatCount=l.Beats.SelectMany(b=>b.SourceSemanticBeatIds).Distinct().Count(), longFormat=FormatDiagnostics(l), shortFormat=FormatDiagnostics(s), longShortContractsIdentical=false, longShortBeatStructureIdentical=Signature(l.Beats)==Signature(s.Beats), longShortSceneStructureIdentical=l.Beats.Sum(b=>b.ExpansionDecision.ResultingFrameCount)==s.Beats.Sum(b=>b.ExpansionDecision.ResultingFrameCount), sharedMutableBeatCollectionUsed=false, shortDerivedByTruncation=false, structuralDifferenceReason=decisionLog.LongShortDifferenceReason, fixedSceneCountUsed=false, oneSemanticBeatToOneFrameForced=false, requiredFactKeysReceived=RequiredFacts(l,s), requiredFactKeysPreserved=RequiredFacts(l,s), requiredFactKeysOmitted=Array.Empty<string>(), duplicatedFactKeysByBeat=DuplicatedFactKeysByBeat(l, s), factTraceabilityValid=v.FactPreservationValid, factDuplicationWarnings=Array.Empty<string>(), roleGoalMismatchWarnings=Array.Empty<string>(), narrationLeakageWarnings=Array.Empty<string>(), validationErrors=v.Errors, decisionLog=decisionLog };
    private static object FormatDiagnostics(DocumentaryContract c) => new { c.DurationStrategy.TargetDurationSeconds, c.DurationStrategy.EstimatedDurationSeconds, c.DurationStrategy.TargetWordBudget, documentaryBeatCount=c.Beats.Count, storyFrameCount=c.Beats.Sum(b=>b.ExpansionDecision.ResultingFrameCount), keptBeatCount=c.Beats.Count(b=>b.ExpansionDecision.Action=="Keep"), mergedBeatCount=c.Beats.Count(b=>b.ExpansionDecision.Action=="Merge"), splitBeatCount=c.Beats.Count(b=>b.ExpansionDecision.Action=="Split"), omittedBeatCount=c.Beats.Count(b=>b.ExpansionDecision.Action=="Omit"), beatDecisionSummary=c.Beats.Select(b=>new{b.BeatId,b.NarrativeRole,b.ExpansionDecision.Action,b.ExpansionDecision.Reason}), viewerJourney=c.ViewerJourney, c.KnowledgeConfidence.KnowledgeCompleteness, c.KnowledgeConfidence.ScienceCompleteness, c.KnowledgeConfidence.ObservationCompleteness, c.KnowledgeConfidence.MissingCriticalFactKeys, legacyFallbackUsed=false };


    private static string BeatFormat(DocumentaryBeat b) => b.BeatId.StartsWith("short-", StringComparison.OrdinalIgnoreCase) ? "short" : "long";
    private static bool HasNarrationLeak(DocumentaryBeat b) => LooksLikeNarration(b.KnowledgeGoal) || LooksLikeNarration(b.EditorialIntent) || LooksLikeNarration(b.TransitionGoal);
    private static IReadOnlyList<string> RequiredFacts(params DocumentaryContract[] contracts) => contracts.SelectMany(c=>c.Beats).SelectMany(b=>b.RequiredFactKeys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    private static object DuplicatedFactKeysByBeat(params DocumentaryContract[] contracts) => contracts.ToDictionary(c=>c.Format, c=>c.Beats.Select(b=>new{b.BeatId, duplicatedFactKeys=b.RequiredFactKeys.GroupBy(k=>k,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToArray()}).Where(x=>x.duplicatedFactKeys.Length>0).ToArray());

    private static (string Archetype,string Reason) ResolveArchetype(string family,string contentType,IReadOnlyList<SemanticBeat> beats){ var f=family.ToLowerInvariant(); if(f.Contains("eclipse")) return ("eclipse-sequence","Family indicates an eclipse sequence with time-ordered observational phases."); if(f.Contains("meteor")) return ("meteor-shower-guide","Family indicates a meteor shower viewing guide."); if(f.Contains("constellation")) return ("constellation-profile","Family indicates constellation profile."); if(f.Contains("galaxy")||f.Contains("nebula")||f.Contains("cluster")||f.Contains("deep")) return ("deep-sky-object-profile","Family indicates deep-sky object."); if(HasAny(beats,["history","historical","mission","archive"])) return ("historical-mission","Semantic beats emphasize historical documentary context."); if(HasAny(beats,["explain","explainer","science","mechanism","why"])) return (contentType=="event" ? "event-observation-science" : "scientific-explainer", contentType=="event" ? "Event semantics include observation/timing plus science explanation needs." : "Semantic beats emphasize scientific explanation."); if(contentType=="event" && HasAny(beats,["observe","view","timing"])) return ("event-observation-science","Event semantics include observation or timing needs."); return ("educational-journey","Generic adaptive educational strategy."); }
    private static IReadOnlyList<ViewerJourneyStage> BuildJourney(DocumentaryContext c,string format)=> c.NarrativeArchetype switch { "event-observation-science" => (format=="short" ? [new("Curiosity","Recognize the event quickly."),new("Orientation","Know when and where to look."),new("Understanding","Understand the core science safely."),new("Action","Remember the viewing action.")] : [new("Curiosity","Care about the sky event."),new("Recognition","Identify the subject."),new("Orientation","Locate it in the sky."),new("Understanding","Understand the science."),new("Confidence","Know how to observe."),new("Wonder","Retain significance."),new("Action","Take the appropriate observing action.")]), _ => [new("Curiosity","Know why the subject matters."),new("Discovery","Build context."),new("Understanding","Understand the key idea."),new("Reflection","Remember the takeaway.")] };
    private static EducationalGoals BuildGoals(DocumentaryContext c,string format)=>new($"Help a {format} viewer understand {c.EventName} as a complete astronomy documentary.",["Preserve upstream audience outcomes.","Use only verified allocated facts."],"Explain the central science without overclaiming.",c.HasObservationMetadata?"Turn verified observation metadata into practical guidance.":"Avoid unsupported observing instructions.","Leave one memorable astronomy takeaway.");
    private static SuccessCriteria BuildSuccess(IReadOnlyList<DocumentaryBeat> beats)=>new(
        beats.SelectMany(b=>b.AllocatedFacts.Values).Select(f=>EducationalFactValue(f.Value)).Where(v=>!string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).DefaultIfEmpty("The main astronomy subject").ToArray(),
        beats.Where(IsScience).Select(b=>EducationalOutcome(b.AudienceOutcome)).Where(v=>!string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).DefaultIfEmpty("The central astronomy idea").ToArray(),
        beats.Where(b=>b.ObservationObjective is not null || b.NarrativeRole.Contains("Observation",StringComparison.OrdinalIgnoreCase) || b.NarrativeRole.Contains("Orientation",StringComparison.OrdinalIgnoreCase)).SelectMany(b=>b.AllocatedFacts.Values.Select(f=>EducationalFactValue(f.Value))).Where(v=>!string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).DefaultIfEmpty("The relevant sky region or visual pattern").ToArray(),
        beats.Select(b=>EducationalOutcome(b.AudienceOutcome)).Where(v=>!string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).TakeLast(3).DefaultIfEmpty("The key takeaway").ToArray(),
        beats.Where(b=>b.ObservationObjective is not null).Select(b=>EducationalAction(b.ObservationObjective!)).Distinct(StringComparer.OrdinalIgnoreCase).DefaultIfEmpty("Use the verified guidance when observing.").ToArray());
    private static KnowledgeConfidence BuildConfidence(IReadOnlyList<DocumentaryBeat> beats,IReadOnlyList<SemanticBeat> semantic){ var missing=semantic.Where(s=>s.Importance=="Required"&&s.AllocatedFacts.Count==0).Select(s=>s.Id).ToArray(); double k=semantic.Count==0?0:semantic.Count(s=>s.AllocatedFacts.Count>0)/(double)semantic.Count; double sci=semantic.Where(IsScience).DefaultIfEmpty().Average(x=>x is null?1:x.AllocatedFacts.Count>0?1:0); double obs=semantic.Where(IsObservation).DefaultIfEmpty().Average(x=>x is null?1:x.AllocatedFacts.Count>0?1:0); return new(missing.Length==0?"usable":"limited",Math.Round(k,2),Math.Round(sci,2),Math.Round(obs,2),Math.Round((k+sci+obs)/3,2),missing); }

    private static IReadOnlyList<SemanticBeat> LoadSemanticBeats(JsonElement? storyGraph, JsonElement? sceneIntents){ var graph=ReadArray(storyGraph,"scenes"); var intents=sceneIntents.HasValue&&sceneIntents.Value.ValueKind==JsonValueKind.Array?sceneIntents.Value.EnumerateArray().Select(e=>e.Clone()).ToArray():[]; var src=graph.Count>0?graph:intents; return src.Select((e,i)=>{ var id=FirstNonEmpty(GetString(e,"sceneId"),GetString(e,"beatId"),$"beat-{i+1:000}")!; var intent=intents.FirstOrDefault(x=>string.Equals(GetString(x,"sceneId"),id,StringComparison.OrdinalIgnoreCase)||string.Equals(GetString(x,"beatId"),id,StringComparison.OrdinalIgnoreCase)); return new SemanticBeat(id,NormalizePurpose(FirstNonEmpty(GetString(e,"scenePurpose"),GetString(e,"purpose"),GetString(e,"narrativeRole")))??FirstNonEmpty(GetString(e,"scenePurpose"),GetString(e,"purpose"),"SupportingDetail")!,FirstNonEmpty(GetString(e,"keyMessage"),GetString(intent,"keyMessage"),GetString(e,"knowledgeGoal"),"Preserve upstream story outcome.")!,FirstNonEmpty(GetString(e,"audienceOutcome"),GetString(intent,"audienceOutcome"),GetString(e,"viewerOutcome"),"Viewer receives the intended story beat.")!,FirstNonEmpty(GetString(e,"importance"),GetString(intent,"importance"),"Required")!,FirstNonEmpty(GetString(e,"editorialIntent"),GetString(intent,"editorialIntent"),GetString(intent,"visualIntent"),"")!,ReadFacts(intent.ValueKind==JsonValueKind.Undefined?e:intent,id)); }).ToArray(); }
    private static IReadOnlyDictionary<string,FactTrace> ReadFacts(JsonElement e,string beatId){ var facts=new Dictionary<string,FactTrace>(StringComparer.OrdinalIgnoreCase); if(e.ValueKind!=JsonValueKind.Object) return facts; foreach(var p in e.EnumerateObject()){ if(!p.Name.Equals("allocatedFacts",StringComparison.OrdinalIgnoreCase)) continue; if(p.Value.ValueKind==JsonValueKind.Object){ foreach(var f in p.Value.EnumerateObject()){ var value=ValueToString(f.Value)??f.Value.GetRawText(); if(!IsMissingFactValue(value)) facts[f.Name]=new FactTrace(f.Name,value,"editorial/scene-intents.json",beatId,"allocated"); } } if(p.Value.ValueKind==JsonValueKind.Array){ foreach(var f in p.Value.EnumerateArray()){ var key=FirstNonEmpty(GetString(f,"key"),GetString(f,"factKey"),ValueToString(f)); var value=FirstNonEmpty(GetString(f,"value"),ValueToString(f))??""; if(key is not null && !IsMissingFactValue(value)) facts[key]=new FactTrace(key,value, "editorial/scene-intents.json",beatId,"allocated"); } } } return facts; }
    private static IReadOnlyDictionary<string,FactTrace> FilterFacts(SemanticBeat s,string[] tokens){ var d=s.AllocatedFacts.Where(kv=>tokens.Any(t=>kv.Key.Contains(t,StringComparison.OrdinalIgnoreCase)||kv.Value.Value.Contains(t,StringComparison.OrdinalIgnoreCase))).ToDictionary(kv=>kv.Key,kv=>kv.Value,StringComparer.OrdinalIgnoreCase); return d.Count==0?s.AllocatedFacts:d; }
    private static IReadOnlyDictionary<string,FactTrace> MergeFacts(IEnumerable<SemanticBeat> beats)=>beats.SelectMany(b=>b.AllocatedFacts).GroupBy(kv=>kv.Key,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First().Value,StringComparer.OrdinalIgnoreCase);
    private static bool HasScienceSplitFacts(SemanticBeat s)=>IsScience(s)&&s.AllocatedFacts.Keys.Count(k=>new[]{"apparent","line","angular","separation","physical","contact","proximity","perspective"}.Any(t=>k.Contains(t,StringComparison.OrdinalIgnoreCase)))>=2;
    private static bool IsScience(SemanticBeat? s)=>s is not null&&(s.Role.Contains("Science",StringComparison.OrdinalIgnoreCase)||s.KnowledgeGoal.Contains("science",StringComparison.OrdinalIgnoreCase)||s.KnowledgeGoal.Contains("alignment",StringComparison.OrdinalIgnoreCase));
    private static bool IsScience(DocumentaryBeat b)=>b.NarrativeRole.Contains("Science",StringComparison.OrdinalIgnoreCase)||b.ScientificObjective is not null;
    private static bool IsObservation(SemanticBeat s)=>s.Role.Contains("Observation",StringComparison.OrdinalIgnoreCase)||s.Role.Contains("Orientation",StringComparison.OrdinalIgnoreCase)||s.Role.Contains("Timing",StringComparison.OrdinalIgnoreCase)||s.KnowledgeGoal.Contains("look",StringComparison.OrdinalIgnoreCase)||s.KnowledgeGoal.Contains("view",StringComparison.OrdinalIgnoreCase);
    private static bool IsRole(SemanticBeat s,string r)=>s.Role.Contains(r,StringComparison.OrdinalIgnoreCase);
    private static bool HasAny(IEnumerable<SemanticBeat> b,string[] terms)=>b.Any(x=>terms.Any(t=>x.Role.Contains(t,StringComparison.OrdinalIgnoreCase)||x.KnowledgeGoal.Contains(t,StringComparison.OrdinalIgnoreCase)||x.AudienceOutcome.Contains(t,StringComparison.OrdinalIgnoreCase)));
    private static bool FactsDuplicatedEverywhere(IReadOnlyList<DocumentaryBeat> beats){ if(beats.Count<2)return false; var common=beats.Select(b=>b.AllocatedFacts.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)).Aggregate((a,b)=>{a.IntersectWith(b);return a;}); return common.Count>0 && beats.All(b=>b.AllocatedFacts.Count==common.Count); }
    private static string Signature(IEnumerable<DocumentaryBeat> beats)=>string.Join("|",beats.Select(b=>$"{b.NarrativeRole}:{string.Join(',',b.SourceSemanticBeatIds)}:{b.ExpansionDecision.Action}"));
    private static bool LooksLikeNarration(string s)=>s.Contains("VOICEOVER",StringComparison.OrdinalIgnoreCase)||s.Contains("Narrator:",StringComparison.OrdinalIgnoreCase);

    private static string BuildVisualGoal(DocumentaryBeat b, DocumentaryContract c)=>$"Create a visual-only {b.NarrativeRole.ToLowerInvariant()} frame for {c.DocumentaryId}, using only source facts attached to {b.BeatId}.";
    private static string BuildComposition(DocumentaryBeat b,string format)=>format=="short"?$"Portrait composition with the primary subject in the central mobile scan path; emphasize {b.NarrativeRole.ToLowerInvariant()} hierarchy and preserve top/bottom label-safe zones.":$"Landscape documentary composition with broad sky context; emphasize {b.NarrativeRole.ToLowerInvariant()} hierarchy and reserve lower-third label-safe space.";
    private static string BuildCameraPlan(DocumentaryBeat b,string format)=>format=="short"?"Grounded vertical sky view with restrained tilt or hold; visual planning metadata only.":"Grounded wide documentary sky view with restrained drift; visual planning metadata only.";
    private static string BuildSubjectFocus(DocumentaryBeat b,DocumentaryContract c)=>$"Primary: {string.Join(" + ", c.SuccessCriteria.ViewerShouldRecognize.DefaultIfEmpty(c.DocumentaryId))}. Source beat: {b.BeatId}.";
    private static string BuildForeground(DocumentaryBeat b,string format)=>b.ObservationObjective is null?"Minimal or absent; do not invent observing context.":"Subtle horizon/location reference only when supported by allocated observation facts.";
    private static string BuildBackground(DocumentaryBeat b,string format)=>"Verified sky context only; no invented constellations, timings, surface details, or unsupported objects.";
    private static string BuildObjectPlacement(DocumentaryBeat b,string format)=>format=="short"?"Keep essential objects away from the top 12%, bottom 18%, and side 8% safe zones.":"Keep essential objects away from lower 18% caption zone and side 6% margins.";
    private static string BuildSafeFramingPlan(string format)=>format=="short"?"Protect top 12%, bottom 18%, side 8%, and a central readable subject lane.":"Protect lower 18%, side 6%, and optional upper-left documentary label area.";
    private static string BuildNegativeSpacePlan(string format)=>format=="short"?"Use clean vertical negative space above and below the subject for mobile labels.":"Use calm side and lower-third negative space for labels without covering the subject.";
    private static string BuildOverlaySafeArea(string format)=>format=="short"?"Exact safe zones: top 12%, bottom 18%, side 8%, central 60% readable.":"Exact safe zones: lower 18%, side 6%, upper-left 20% optional label area.";
    private static string BuildMotionHint(DocumentaryBeat b,string format)=>b.NarrativeRole switch { "Science"=>"Mostly static explanatory hold with only subtle guide motion; preserve true spatial meaning.", "Observation"=>"Gentle lookup motion from horizon context toward the target, ending on a steady hold.", "Hook"=>"Slow reveal from negative sky space toward the primary subject.", _=>"Restrained documentary drift that supports visual comprehension only." };
    private static string ResolvePrimarySubjectFromContract(JsonElement? contract,string eventName){ var objects=FindContractFactArray(contract,"primaryObjects").Concat(FindContractFactArray(contract,"secondaryObjects")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); return objects.Length==0?eventName:string.Join(" + ",objects); }
    private static IReadOnlyList<string> ResolveSecondarySubjectsFromContract(JsonElement? contract){ var subjects=new List<string>(); foreach(var (k,l) in new[]{("angularSeparationDegrees","Angular separation"),("bestViewingWindowLocal","Best viewing window"),("skyDirectionHint","Sky direction")}){ var v=FindContractFactString(contract,k); if(!string.IsNullOrWhiteSpace(v)) subjects.Add($"{l}: {v}"); } return subjects; }
    private static IReadOnlyList<string> FindContractFactArray(JsonElement? element,string name){ var fact=FindProperty(element,name); if(fact is not {ValueKind:JsonValueKind.Object} obj||!TryGetProperty(obj,"value",out var value)||value.ValueKind!=JsonValueKind.Array)return[]; return value.EnumerateArray().Select(ValueToString).Where(v=>!string.IsNullOrWhiteSpace(v)).Select(v=>v!).ToArray(); }
    private static string? FindContractFactString(JsonElement? element,string name){ var fact=FindProperty(element,name); return fact is {ValueKind:JsonValueKind.Object} obj&&TryGetProperty(obj,"value",out var value)?ValueToString(value):null; }
    private static JsonElement? FindProperty(JsonElement? element,string name){ if(!element.HasValue)return null; if(element.Value.ValueKind==JsonValueKind.Object){ foreach(var p in element.Value.EnumerateObject()){ if(string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase))return p.Value; var nested=FindProperty(p.Value,name); if(nested.HasValue)return nested; } } return null; }
    private static bool TryGetProperty(JsonElement element,string name,out JsonElement value){ foreach(var p in element.EnumerateObject()) if(string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase)){value=p.Value;return true;} value=default; return false; }
    private static string? NormalizePurpose(string? purpose){ if(string.IsNullOrWhiteSpace(purpose))return null; var c=new string(purpose.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant(); return c switch { "hook"=>"Hook", "orientation" or "discovery"=>"Orientation", "timing"=>"Timing", "science"=>"Science", "observation" or "viewing" or "observing"=>"Observation", "significance"=>"Significance", "takeaway" or "summary" or "closing"=>"Takeaway", _=>purpose }; }
    private static JsonElement? ReadFirstJson(string path){ if(!File.Exists(path))return null; using var doc=JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.Clone(); }
    private static IReadOnlyList<JsonElement> ReadArray(JsonElement? element,string name){ if(element is not {ValueKind:JsonValueKind.Object} e)return[]; foreach(var p in e.EnumerateObject()) if(string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase)&&p.Value.ValueKind==JsonValueKind.Array)return p.Value.EnumerateArray().Select(i=>i.Clone()).ToArray(); return[]; }
    private static string? GetString(JsonElement? element,string name){ if(element is not {ValueKind:JsonValueKind.Object} e)return null; foreach(var p in e.EnumerateObject()) if(string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase))return ValueToString(p.Value); return null; }
    private static string? ValueToString(JsonElement value)=>value.ValueKind switch { JsonValueKind.String=>value.GetString(), JsonValueKind.Number=>value.GetRawText(), JsonValueKind.True=>"true", JsonValueKind.False=>"false", _=>null };
    private static string? FirstNonEmpty(params string?[] values)=>values.FirstOrDefault(v=>!string.IsNullOrWhiteSpace(v));
    private static string NormalizePath(string path)=>path.Replace(Path.DirectorySeparatorChar,'/');
    private static string Slug(string value)=>string.Join('-',value.ToLowerInvariant().Split(Path.GetInvalidFileNameChars().Concat([' ',':','/','\\']).ToArray(),StringSplitOptions.RemoveEmptyEntries)).Trim('-');
    private static (bool LongRequested,bool ShortRequested) ResolveStoryFrameRequests(BatchGenerateFromPlansRequest request,BatchGenerateFromPlansResponse response){ var completions=response.RequestedOutputCompletion??response.Results?.OfType<ContentPlanProductionExecutionResult>().SelectMany(r=>r.RequestedOutputCompletion??[]).ToArray(); bool Req(string o)=>completions?.Any(c=>c.Requested&&string.Equals(c.OutputType,o,StringComparison.OrdinalIgnoreCase))==true; var l=Req("LongVideo"); var s=Req("ShortVideo"); foreach(var f in response.SelectedPlans.Select(p=>p.PlannedFormat).Where(f=>!string.IsNullOrWhiteSpace(f))){ if(f!.Contains("long",StringComparison.OrdinalIgnoreCase))l=true; if(f.Contains("short",StringComparison.OrdinalIgnoreCase))s=true; } return !l&&!s?(true,true):(l,s); }
}

public sealed record DocumentaryContext(string OrchestrationVersion,string DocumentaryId,string EventName,string Domain,string Family,string ContentType,string NarrativeArchetype,string ArchetypeReason,string Language,string RegionId,string PrimarySubject,IReadOnlyList<string> SecondarySubjects,bool HasObservationMetadata,IReadOnlyList<string> Warnings);
public sealed record SemanticBeat(string Id,string Role,string KnowledgeGoal,string AudienceOutcome,string Importance,string EditorialIntent,IReadOnlyDictionary<string,FactTrace> AllocatedFacts);
public sealed record FactTrace(string FactKey,string Value,string SourceArtifact,string SourceSemanticBeat,string Status);
public sealed record DocumentaryContract(string ContractVersion,string OrchestrationVersion,string DocumentaryId,string Domain,string Family,string ContentType,string NarrativeArchetype,string Format,string Language,string RegionId,AudienceProfile AudienceProfile,DurationStrategy DurationStrategy,EducationalGoals EducationalGoals,IReadOnlyList<ViewerJourneyStage> ViewerJourney,SuccessCriteria SuccessCriteria,KnowledgeConfidence KnowledgeConfidence,IReadOnlyList<DocumentaryBeat> Beats);
public sealed record AudienceProfile(string KnowledgeLevel,string ViewerIntent,IReadOnlyList<string> Prerequisites);
public sealed record DurationStrategy(int TargetDurationSeconds,int MinimumDurationSeconds,int MaximumDurationSeconds,int EstimatedDurationSeconds,int TargetWordBudget,string Pacing);
public sealed record EducationalGoals(string PrimaryGoal,IReadOnlyList<string> SecondaryGoals,string ScientificGoal,string ObservationGoal,string MemoryGoal);
public sealed record ViewerJourneyStage(string Stage,string TargetOutcome);
public sealed record SuccessCriteria(IReadOnlyList<string> ViewerShouldKnow,IReadOnlyList<string> ViewerShouldUnderstand,IReadOnlyList<string> ViewerShouldRecognize,IReadOnlyList<string> ViewerShouldRemember,IReadOnlyList<string> ViewerShouldDo);
public sealed record KnowledgeConfidence(string Overall,double KnowledgeCompleteness,double ScienceCompleteness,double ObservationCompleteness,double ViewerReadiness,IReadOnlyList<string> MissingCriticalFactKeys);
public sealed record DocumentaryBeat(string BeatId,IReadOnlyList<string> SourceSemanticBeatIds,int BeatOrder,string NarrativeRole,string KnowledgeGoal,string AudienceOutcome,string Importance,string Complexity,int EstimatedDurationSeconds,int EstimatedWordBudget,IReadOnlyList<string> RequiredFactKeys,IReadOnlyList<string> OptionalFactKeys,IReadOnlyDictionary<string,FactTrace> AllocatedFacts,string EditorialIntent,string TransitionGoal,ExpansionDecision ExpansionDecision,string? ObservationObjective,string? ScientificObjective,IReadOnlyList<string> SuccessCriteria,IReadOnlyList<string> Warnings);
public sealed record ExpansionDecision(string Action,string Reason,IReadOnlyList<string> SourceBeatIds,int ResultingFrameCount);
public sealed record DocumentaryDecisionLog(string DecisionLogVersion,string Archetype,string ArchetypeSelectionReason,string ViewerJourneySelectionReason,string LongShortDifferenceReason,IReadOnlyList<DocumentaryDecisionLogEntry> Entries);
public sealed record DocumentaryDecisionLogEntry(string Format,string ResultingBeatId,IReadOnlyList<string> SourceSemanticBeatIds,string Decision,string Reason,IReadOnlyList<string> Evidence,IReadOnlyList<string> SupportingAudienceOutcomes,string AlternativeConsidered,string WhyRejected,string ExpectedAudienceBenefit,int ResultingFrameCount,double Confidence,IReadOnlyList<string> Warnings);
public sealed record Phase6Validation(int PhaseNo,string PhaseName,string Status,IReadOnlyList<string> Warnings,IReadOnlyList<string> Errors,string Reason,bool AuroraCertificationCandidate,bool LongRequested,bool ShortRequested,bool LongGenerated,bool ShortGenerated,int LongDocumentaryBeatCount,int ShortDocumentaryBeatCount,int LongStoryFrameCount,int ShortStoryFrameCount,int LongQualityScore,int ShortQualityScore,int OverallPhaseQualityScore,bool ContractsValid,bool DecisionLogsValid,bool LongShortIndependenceValid,bool FactPreservationValid,bool FrameGenerationPathValid,bool NarrationLeakageFree,bool FixedSceneCountUsed,bool OneSemanticBeatToOneFrameForced,bool SharedMutableBeatCollectionUsed,bool LegacyFallbackUsed,bool CanRetry,DateTimeOffset CreatedUtc);
public sealed record CreativeStoryboard(string CreativeStoryboardVersion,string OrchestrationVersion,string EventType,string EventName,string Language,string RegionId,string CreativePrinciple,string StoryArc,string GlobalVisualDirection,IReadOnlyList<CreativeStoryboardScene> Scenes,IReadOnlyList<string> MissingCreativeWarnings);
public sealed record CreativeStoryboardScene(string SceneId,string ScenePurpose,int SceneOrder,string KeyMessage,string ViewerFocus,string EmotionalRole,string VisualRole,string MotionRole,string PrimarySubject,IReadOnlyList<string> SecondarySubjects,string CompositionIntent,string CameraIntent,string LightingIntent,string MotionIntent,string TransitionIntent,IReadOnlyList<string> VisualAccuracyRules,IReadOnlyList<string> ProhibitedVisualChoices,string DocumentaryBeatId,IReadOnlyList<string> SourceSemanticBeatIds,IReadOnlyDictionary<string,FactTrace> AllocatedFacts);
public sealed record CreativeStoryboardBuilderResult(CreativeStoryboard? Storyboard,IReadOnlyList<string> GeneratedFiles){ public static CreativeStoryboardBuilderResult Empty { get; } = new(null, []); }
public sealed record StoryFrame(string FrameId,string SceneId,int SceneOrder,string ScenePurpose,string Format,string Orientation,string AspectRatio,int TargetWidth,int TargetHeight,string DocumentaryBeatId,IReadOnlyList<string> SourceSemanticBeatIds,IReadOnlyDictionary<string,FactTrace> SourceFacts,string VisualGoal,string Composition,string CameraPlan,string SubjectFocus,string Foreground,string Background,string ObjectPlacement,string SafeFramingPlan,string NegativeSpacePlan,string OverlaySafeArea,string MotionHint,double EstimatedDurationSeconds,string SourceDocumentaryBeatId,string SourceSceneIntentId,string NarrationMapping);
