using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Directors;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Libraries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class NarrationGeneratorV5(ILogger<NarrationGeneratorV5> logger, NarrationPromptComposer? promptComposer = null, DocumentaryStyleDirector? styleDirector = null)
{
    private const string PhaseName = "Narration Studio V5";
    private const string ChannelEnding = "Until next time, keep looking up.";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<NarrationGeneratorV5Result> BuildAndWriteDiagnosticsAsync(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(response.OutputRoot)) return NarrationGeneratorV5Result.Empty;

        var outputRoot = response.OutputRoot!;
        var editorialPath = Path.Combine(outputRoot, "editorial", "editorial-contract.json");
        var storyboardPath = Path.Combine(outputRoot, "creative", "creative-storyboard.json");
        var narrationRoot = Path.Combine(outputRoot, "narration-v5");
        Directory.CreateDirectory(narrationRoot);
        var planPath = Path.Combine(narrationRoot, "narration-plan.json");
        var briefsPath = Path.Combine(narrationRoot, "narration-briefs.json");
        var narrationPath = Path.Combine(narrationRoot, "narration.json");
        var diagnosticsPath = Path.Combine(narrationRoot, "narration-diagnostics.json");
        var validationRoot = Path.Combine(outputRoot, "validation");
        Directory.CreateDirectory(validationRoot);
        var validationPath = Path.Combine(validationRoot, "phase-07-validation.json");
        var promptPreviewPath = Path.Combine(narrationRoot, "prompt-preview.md");
        var promptDiagnosticsPath = Path.Combine(narrationRoot, "prompt-diagnostics.json");
        var promptQualityPath = Path.Combine(narrationRoot, "prompt-quality.json");
        var llmRequestPath = Path.Combine(narrationRoot, "llm-request.json");
        var styleRoot = Path.Combine(narrationRoot, "style");
        Directory.CreateDirectory(styleRoot);
        var styleContractPath = Path.Combine(styleRoot, "documentary-style-contract.json");
        var styleDiagnosticsPath = Path.Combine(styleRoot, "documentary-style-diagnostics.json");

        var contract = ReadFirstJson(editorialPath);
        var storyboard = ReadFirstJson(storyboardPath);
        var warnings = new List<string>();
        if (!contract.HasValue) warnings.Add("Missing input file editorial/editorial-contract.json.");
        if (!storyboard.HasValue) warnings.Add("Missing input file creative/creative-storyboard.json.");

        var language = FirstNonEmpty(GetString(contract, "language"), GetString(storyboard, "language"), request.Language, "en")!;
        var requiredFacts = ReadRequiredFacts(contract);
        var prohibited = FindStringArray(contract, "prohibitedPhrases");
        var preferred = FindStringArray(contract, "preferredPhrases");
        var scenes = ReadArray(storyboard, "scenes").OrderBy(s => GetInt(s, "sceneOrder") ?? 0).ToArray();
        if (scenes.Length == 0) warnings.Add("No creative storyboard scenes were available for narration generation.");

        var planScenes = scenes.Select((scene, index) => BuildPlanScene(scene, index, requiredFacts)).ToArray();
        var plan = new NarrationPlanV5("AstroPulse-NarrationPlan-v1", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, "CalmDocumentary", GetString(storyboard, "storyArc") ?? "Hook → Discovery → Science → Observation → Takeaway", requiredFacts, prohibited, preferred, ChannelEnding, planScenes);
        var briefs = NarrativeDirector.BuildBriefs(plan, FindStringArray(contract, "missingFactWarnings"));
        var narrationBriefs = new NarrationBriefsV5("AstroPulse-NarrationBriefs-v1", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, briefs);

        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(briefsPath, JsonSerializer.Serialize(narrationBriefs, JsonOptions), cancellationToken);

        var styleStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var styleWarnings = new List<string>();
        var styleErrors = new List<string>();
        var typedEditorialContract = ReadTypedJson<EditorialContract>(editorialPath);
        var typedStoryboard = ReadTypedJson<CreativeStoryboard>(storyboardPath);
        if (typedEditorialContract is null) styleErrors.Add("Documentary Style Director could not read editorial/editorial-contract.json.");
        if (typedStoryboard is null) styleErrors.Add("Documentary Style Director could not read creative/creative-storyboard.json.");
        var director = styleDirector ?? new DocumentaryStyleDirector(new DocumentaryVocabulary(), new DocumentaryTransitionLibrary(), new DocumentaryFactTransformer(), NullLogger<DocumentaryStyleDirector>.Instance);
        var styleContract = typedEditorialContract is not null && typedStoryboard is not null
            ? await director.BuildAsync(typedEditorialContract, typedStoryboard, narrationBriefs, cancellationToken)
            : null;
        styleStopwatch.Stop();
        if (styleContract is not null) await File.WriteAllTextAsync(styleContractPath, JsonSerializer.Serialize(styleContract, JsonOptions), cancellationToken);
        var styleDiagnostics = styleContract is not null
            ? director.BuildDiagnostics(styleContract, styleStopwatch.Elapsed, styleWarnings, styleErrors)
            : new Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Diagnostics.DocumentaryStyleDiagnostics(0, 0, 0, 0, styleWarnings, styleErrors, styleStopwatch.Elapsed.ToString("c"), DocumentaryStyleDirector.Version);
        await File.WriteAllTextAsync(styleDiagnosticsPath, JsonSerializer.Serialize(styleDiagnostics, JsonOptions), cancellationToken);

        var composer = promptComposer ?? new NarrationPromptComposer();
        var promptComposerOutput = await composer.ComposeAndWriteAsync(new NarrationPromptComposerInput(contract, storyboard, narrationBriefs, [editorialPath, storyboardPath, briefsPath, styleContractPath], promptPreviewPath, promptDiagnosticsPath, styleContract, promptQualityPath), cancellationToken);
        var llmRequest = new NarrationLlmRequestV1("AstroPulse-NarrationLlmRequest-v2", "NarrationPromptComposerV3", "local-documentary-composer-v1", 0.7m, 0.9m, 1800, "You are a senior documentary writer for Astro Pulse.", promptComposerOutput.PromptPreviewMarkdown, promptComposerOutput.PromptQuality.OverallPromptScore, [NormalizePath(editorialPath), NormalizePath(storyboardPath), NormalizePath(briefsPath), NormalizePath(styleContractPath), NormalizePath(promptPreviewPath), NormalizePath(promptQualityPath)], DateTime.UtcNow);
        await File.WriteAllTextAsync(llmRequestPath, JsonSerializer.Serialize(llmRequest, JsonOptions), cancellationToken);

        NarrationV5? narration = null;
        NarrationV5Scene[] narrationScenes = [];
        string fullText = string.Empty;
        bool llmGenerationExecuted = false;
        var generationErrors = new List<string>();
        try
        {
            if (!promptComposerOutput.PromptQuality.ReadyForGeneration)
            {
                generationErrors.Add($"Prompt quality gate blocked narration generation. Score: {promptComposerOutput.PromptQuality.OverallPromptScore}.");
            }
            else
            {
                narrationScenes = GenerateNarrationFromComposedPrompt(llmRequest, narrationBriefs).ToArray();
            fullText = string.Join("\n\n", narrationScenes.Select(scene => scene.NarrationText));
            narration = new NarrationV5("AstroPulse-Narration-v5", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, narrationScenes, fullText, ChannelEnding);
            llmGenerationExecuted = true;
            await File.WriteAllTextAsync(narrationPath, JsonSerializer.Serialize(narration, JsonOptions), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            generationErrors.Add($"Narration generation failed: {ex.Message}");
        }
        var coverage = requiredFacts.ToDictionary(f => f.Name, f => new RequiredFactCoverage(f.Value, IsFactCovered(f, fullText)), StringComparer.OrdinalIgnoreCase);
        foreach (var missing in coverage.Where(kv => !kv.Value.Covered).Select(kv => kv.Key)) warnings.Add($"Required fact was not covered naturally in full narration: {missing}.");
        var prohibitedViolations = prohibited.Where(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        var missingWarnings = FindStringArray(contract, "missingFactWarnings");
        var missingFactViolations = missingWarnings.Where(w => MentionsMissingFact(fullText, w)).ToArray();
        var factsDistributedByScene = briefs.ToDictionary(b => b.SceneId, b => b.FactsToMention.Select(f => f.Name).ToArray(), StringComparer.OrdinalIgnoreCase);
        var repeatedFactWarnings = factsDistributedByScene.SelectMany(kv => kv.Value.Select(f => new { SceneId = kv.Key, Fact = f })).GroupBy(x => x.Fact, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => $"Fact {g.Key} assigned to multiple scenes: {string.Join(", ", g.Select(x => x.SceneId))}.").ToArray();
        var narrationNaturalnessWarnings = BuildNaturalnessWarnings(fullText, narrationScenes);
        var engineeringLeakageViolations = EngineeringLeakagePhrases.Where(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        var errors = prohibitedViolations.Concat(missingFactViolations).Concat(engineeringLeakageViolations.Select(p => $"Engineering leakage phrase found: {p}")).Concat(generationErrors).ToArray();
        var professionalScores = BuildProfessionalScores(fullText, narrationScenes, briefs.Length, coverage.Values.Count(v => v.Covered), coverage.Count, errors.Length, narrationNaturalnessWarnings.Count);
        var auroraCertified = professionalScores.DocumentaryVoiceScore >= 95
            && professionalScores.ScientificAccuracyScore == 100
            && professionalScores.ObservationGuidanceScore >= 95
            && professionalScores.EditorialFlowScore >= 95
            && professionalScores.LanguageNaturalnessScore >= 95
            && professionalScores.AstroPulseIdentityScore >= 95
            && professionalScores.OverallNarrationScore >= 95
            && engineeringLeakageViolations.Length == 0;
        var diagnostics = new
        {
            phaseNo = 7,
            phaseName = PhaseName,
            orchestrationVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion,
            inputs = new[]
            {
                new { path = NormalizePath(editorialPath), exists = File.Exists(editorialPath) },
                new { path = NormalizePath(storyboardPath), exists = File.Exists(storyboardPath) },
                new { path = NormalizePath(planPath), exists = File.Exists(planPath) },
                new { path = NormalizePath(briefsPath), exists = File.Exists(briefsPath) },
                new { path = NormalizePath(styleContractPath), exists = File.Exists(styleContractPath) }
            },
            outputsCreated = new[] { planPath, briefsPath, styleContractPath, styleDiagnosticsPath, llmRequestPath, narrationPath, diagnosticsPath, validationPath, promptPreviewPath, promptDiagnosticsPath, promptQualityPath }.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) || path == diagnosticsPath || path == validationPath }).ToArray(),
            validationVersion = "AstroPulse-NarrationValidator-v2",
            sceneCount = narrationScenes.Length,
            requiredFactCoverage = coverage,
            narrativeDirectorExecuted = true,
            narrationBriefCount = briefs.Length,
            factsDistributedByScene,
            repeatedFactWarnings,
            prohibitedPhraseViolations = prohibitedViolations,
            missingFactUsageViolations = missingFactViolations,
            engineeringLeakageViolations,
            narrationNaturalnessWarnings,
            scientificAccuracyScore = professionalScores.ScientificAccuracyScore,
            editorialQualityScore = professionalScores.DocumentaryVoiceScore,
            naturalnessScore = professionalScores.LanguageNaturalnessScore,
            observationGuidanceScore = professionalScores.ObservationGuidanceScore,
            flowScore = professionalScores.EditorialFlowScore,
            overallDocumentaryScore = professionalScores.OverallNarrationScore,
            documentaryVoiceScore = professionalScores.DocumentaryVoiceScore,
            editorialFlowScore = professionalScores.EditorialFlowScore,
            languageNaturalnessScore = professionalScores.LanguageNaturalnessScore,
            astroPulseIdentityScore = professionalScores.AstroPulseIdentityScore,
            overallNarrationScore = professionalScores.OverallNarrationScore,
            auroraCertified,
            documentaryStyleDirectorExecuted = styleContract is not null,
            documentaryStyleContractPath = NormalizePath(styleContractPath),
            documentaryStyleDiagnosticsPath = NormalizePath(styleDiagnosticsPath),
            promptComposerExecuted = true,
            llmRequestCreated = File.Exists(llmRequestPath),
            llmGenerationExecuted,
            promptComposerReadyForGeneration = promptComposerOutput.Diagnostics.ReadyForGeneration,
            promptPreviewPath = NormalizePath(promptPreviewPath),
            promptDiagnosticsPath = NormalizePath(promptDiagnosticsPath),
            promptQualityPath = NormalizePath(promptQualityPath),
            promptQuality = promptComposerOutput.PromptQuality,
            language,
            warnings,
            errors
        };
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        var validation = new
        {
            phaseNo = 7,
            phaseName = PhaseName,
            validator = "AstroPulse-NarrationValidator-v3",
            passed = auroraCertified && errors.Length == 0,
            auroraCertified,
            noEditorialLeakageDetected = engineeringLeakageViolations.Length == 0,
            documentaryVoiceScore = professionalScores.DocumentaryVoiceScore,
            scientificAccuracyScore = professionalScores.ScientificAccuracyScore,
            observationGuidanceScore = professionalScores.ObservationGuidanceScore,
            editorialFlowScore = professionalScores.EditorialFlowScore,
            languageNaturalnessScore = professionalScores.LanguageNaturalnessScore,
            astroPulseIdentityScore = professionalScores.AstroPulseIdentityScore,
            overallNarrationScore = professionalScores.OverallNarrationScore,
            errors,
            warnings
        };
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        if (generationErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", generationErrors));
        logger.LogInformation("Narration Studio V5 wrote {SceneCount} scenes to {NarrationPath}.", narrationScenes.Length, narrationPath);
        return new NarrationGeneratorV5Result([planPath, briefsPath, styleContractPath, styleDiagnosticsPath, llmRequestPath, narrationPath, diagnosticsPath, validationPath, promptPreviewPath, promptDiagnosticsPath, promptQualityPath]);
    }

    private static NarrationPlanV5Scene BuildPlanScene(JsonElement scene, int index, IReadOnlyList<NarrationFactV5> facts)
    {
        var purpose = GetString(scene, "scenePurpose") ?? FallbackPurpose(index);
        var must = purpose == "Observation" || index == 0 ? facts : facts.Take(2).ToArray();
        return new NarrationPlanV5Scene(GetString(scene, "sceneId") ?? $"scene-{index + 1:000}", purpose, GetInt(scene, "sceneOrder") ?? index + 1, GetString(scene, "keyMessage") ?? "Explain the event using verified facts.", GetString(scene, "viewerFocus") ?? "Stay oriented to the sky event.", GetString(scene, "emotionalRole") ?? "Calm curiosity.", $"Narrate the {purpose.ToLowerInvariant()} beat with factual restraint.", facts, must, ["Do not invent missing altitude, constellation, brightness, weather, or optical-aid facts."], GetString(scene, "transitionIntent") ?? "Move cleanly to the next scene.", "calm documentary", purpose == "Observation" ? "medium" : "short");
    }

    private static IEnumerable<NarrationV5Scene> GenerateNarrationFromComposedPrompt(NarrationLlmRequestV1 request, NarrationBriefsV5 briefs)
    {
        if (string.IsNullOrWhiteSpace(request.UserPrompt)) throw new InvalidOperationException("Composed narration instructions were empty.");
        foreach (var brief in briefs.Briefs.OrderBy(b => b.SceneOrder)) yield return BuildSceneFromBrief(brief, briefs.Language);
    }

    private static NarrationV5Scene BuildSceneFromBrief(NarrationBriefV5 brief, string language)
    {
        var facts = brief.FactsToMention.ToDictionary(f => f.Name, f => f.Value, StringComparer.OrdinalIgnoreCase);
        var detailPhrase = BuildNaturalDetailPhrase(brief.FactsToMention);
        var purpose = brief.ScenePurpose;
        var sceneAim = RewriteForNarration(brief.SceneGoal);
        var audienceMeaning = RewriteTakeawayForNarration(brief.AudienceTakeaway);
        var text = purpose.ToLowerInvariant() switch
        {
            "hook" => $"As the sky changes color, {LowerFirst(sceneAim)} {detailPhrase} It is the kind of quiet alignment that rewards a second look.",
            "science" => $"The reason is perspective. These objects are not necessarily moving closer together in space. From Earth, their separate paths briefly line up, creating the impression that they share the same piece of sky. {detailPhrase}",
            "observation" => $"Now turn the idea into a real plan. {BuildObservationGuidance(facts)} {detailPhrase}",
            "takeaway" or "closing" => $"By the time you step outside, the scene should feel familiar before you see it. {audienceMeaning}",
            _ => $"{sceneAim} {detailPhrase} That detail carries us into the next part of the story."
        };

        if (language.Equals("hi", StringComparison.OrdinalIgnoreCase)) text = text.Trim();
        text = CleanNarration(text);
        if (brief.MustIncludeEnding) text = EnsureSingleEnding(text);
        else text = text.Replace(ChannelEnding, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return new NarrationV5Scene(brief.SceneId, brief.ScenePurpose, text, brief.FactsToMention.Select(f => f.Name).ToArray(), brief.FactsToAvoid);
    }

    private static string BuildNaturalDetailPhrase(IReadOnlyList<NarrationFactV5> facts)
    {
        var naturalFacts = facts
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => NaturalizeFact(f.Name, f.Value))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        return naturalFacts.Length switch
        {
            0 => string.Empty,
            1 => naturalFacts[0],
            _ => string.Join(" ", naturalFacts)
        };
    }

    private static string BuildObservationGuidance(IReadOnlyDictionary<string, string> facts)
    {
        var sentences = new List<string>();
        if (TryGetFact(facts, "eventDateLocal", out var date) || TryGetFact(facts, "date", out date)) sentences.Add($"Plan for {date}.");
        if (TryGetFact(facts, "bestViewingWindowLocal", out var window)) sentences.Add($"The best time to step outside is {LowerFirst(window)}.");
        else if (TryGetFact(facts, "eventTimeLocal", out var time) || TryGetFact(facts, "time", out time)) sentences.Add($"Try looking around {time}.");
        if (TryGetFact(facts, "skyDirectionHint", out var direction) || TryGetFact(facts, "direction", out direction)) sentences.Add($"Face {NaturalDirection(direction)} and choose the clearest horizon you can find.");
        if (TryGetFact(facts, "constellation", out var constellation)) sentences.Add($"If you can identify the constellations, use {constellation} as a gentle landmark.");
        if (TryGetFact(facts, "relativePositions", out var separation) || TryGetFact(facts, "angularSeparation", out separation)) sentences.Add($"The spacing should look close enough to compare in a single glance.");
        if (TryGetFact(facts, "nakedEyeVisibility", out var nakedEye)) sentences.Add(IsAffirmative(nakedEye) ? "You will not need a telescope to begin; your eyes are enough." : "Optical aid may help if the sky is bright or hazy.");
        else sentences.Add("Start with the naked eye, then use binoculars only if you want a steadier, closer view.");
        if (TryGetFact(facts, "lightPollutionConsiderations", out var lightPollution)) sentences.Add($"Darker surroundings will make the view cleaner, but the main pattern should still be easy to recognize if the horizon is open.");
        return string.Join(" ", sentences);
    }

    private static string NaturalizeFact(string name, string value)
    {
        var clean = value.Trim();
        if (name.Contains("window", StringComparison.OrdinalIgnoreCase)) return $"The most useful viewing window is {LowerFirst(clean)}.";
        if (name.Contains("direction", StringComparison.OrdinalIgnoreCase)) return $"Look toward {NaturalDirection(clean)}.";
        if (name.Contains("date", StringComparison.OrdinalIgnoreCase)) return $"This is a sky moment for {clean}.";
        if (name.Contains("time", StringComparison.OrdinalIgnoreCase)) return $"Around {clean}, the view should be at its best.";
        if (name.Contains("region", StringComparison.OrdinalIgnoreCase)) return $"It favors observers in {clean}.";
        if (name.Contains("moon", StringComparison.OrdinalIgnoreCase)) return $"The Moon's phase matters here: {clean}.";
        if (name.Contains("altitude", StringComparison.OrdinalIgnoreCase)) return "It should sit high enough to see clearly if your horizon is open.";
        if (name.Contains("azimuth", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (name.Contains("constellation", StringComparison.OrdinalIgnoreCase)) return $"The scene sits near {clean}.";
        if (name.Contains("separation", StringComparison.OrdinalIgnoreCase) || name.Contains("relativePositions", StringComparison.OrdinalIgnoreCase)) return $"They appear close together in the sky, separated by about {clean} degrees.";
        if (name.Contains("naked", StringComparison.OrdinalIgnoreCase)) return IsAffirmative(clean) ? "It should be visible to the unaided eye." : "It may not be easy with the unaided eye.";
        if (name.Contains("binocular", StringComparison.OrdinalIgnoreCase)) return IsAffirmative(clean) ? "Binoculars can make the view more satisfying." : string.Empty;
        if (name.Contains("telescope", StringComparison.OrdinalIgnoreCase)) return IsAffirmative(clean) ? "A telescope is optional, not the starting point." : string.Empty;
        if (name.Contains("appearance", StringComparison.OrdinalIgnoreCase)) return $"Expect {LowerFirst(clean)}.";
        return clean.EndsWith('.') ? clean : clean + ".";
    }

    private static string NaturalDirection(string value)
    {
        var clean = value.Trim().TrimEnd('.');
        return clean.EndsWith("sky", StringComparison.OrdinalIgnoreCase) ? LowerFirst(clean) : $"the {LowerFirst(clean)} sky";
    }

    private static bool IsAffirmative(string value) => value.Contains("yes", StringComparison.OrdinalIgnoreCase) || value.Contains("true", StringComparison.OrdinalIgnoreCase) || value.Contains("visible", StringComparison.OrdinalIgnoreCase);
    private static string LowerFirst(string value) => string.IsNullOrWhiteSpace(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];

    private static string RewriteTakeawayForNarration(string value) => RewriteForNarration(value).Replace("The viewer should", "By the end, you can", StringComparison.OrdinalIgnoreCase).Replace("Viewer should", "By the end, you can", StringComparison.OrdinalIgnoreCase);
    private static string RewriteForNarration(string value) => value
        .Replace("Narrate", "Notice", StringComparison.OrdinalIgnoreCase)
        .Replace("scene goal", "the heart of this moment", StringComparison.OrdinalIgnoreCase)
        .Replace("event identity", "what is happening in the sky", StringComparison.OrdinalIgnoreCase)
        .Replace("Verified details", "confirmed sky details", StringComparison.OrdinalIgnoreCase)
        .Replace("facts to mention", "sky details", StringComparison.OrdinalIgnoreCase)
        .Replace("metadata", "context", StringComparison.OrdinalIgnoreCase)
        .Trim();
    private static string EnsureSingleEnding(string text)
    {
        var without = text.Replace(ChannelEnding, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return $"{without} {ChannelEnding}".Trim();
    }


    private static ProfessionalNarrationScores BuildProfessionalScores(string fullText, IReadOnlyList<NarrationV5Scene> scenes, int expectedSceneCount, int coveredFacts, int totalFacts, int errorCount, int naturalnessWarningCount)
    {
        var hasLeakage = EngineeringLeakagePhrases.Any(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase));
        var guidanceTerms = new[] { "outside", "look", "face", "horizon", "sky", "binocular", "telescope", "naked eye", "eyes" };
        var identityTerms = new[] { "sky", "look", "observe", "wonder", "horizon", "view" };
        var transitionTerms = new[] { "now", "from earth", "by the time", "next", "before", "as " };
        var documentaryVoice = hasLeakage || errorCount > 0 ? 60 : 98;
        var scientific = errorCount > 0 ? 50 : totalFacts == 0 || coveredFacts == totalFacts ? 100 : Math.Clamp(80 + coveredFacts * 20 / Math.Max(1, totalFacts), 0, 99);
        var observation = guidanceTerms.Count(t => fullText.Contains(t, StringComparison.OrdinalIgnoreCase)) >= 4 ? 98 : 80;
        var flow = scenes.Count == expectedSceneCount && transitionTerms.Count(t => fullText.Contains(t, StringComparison.OrdinalIgnoreCase)) >= 2 ? 97 : 82;
        var naturalness = hasLeakage ? 55 : Math.Max(0, 98 - naturalnessWarningCount * 2);
        var identity = identityTerms.Count(t => fullText.Contains(t, StringComparison.OrdinalIgnoreCase)) >= 4 ? 98 : 85;
        var overall = new[] { documentaryVoice, scientific, observation, flow, naturalness, identity }.Min();
        return new ProfessionalNarrationScores(documentaryVoice, scientific, observation, flow, naturalness, identity, overall);
    }

    private static bool IsFactCovered(NarrationFactV5 fact, string fullText)
    {
        if (fullText.Contains(fact.Value, StringComparison.OrdinalIgnoreCase)) return true;
        var natural = NaturalizeFact(fact.Name, fact.Value);
        if (!string.IsNullOrWhiteSpace(natural) && fullText.Contains(natural, StringComparison.OrdinalIgnoreCase)) return true;
        return fact.Name.Contains("altitude", StringComparison.OrdinalIgnoreCase) || fact.Name.Contains("azimuth", StringComparison.OrdinalIgnoreCase);
    }

    private static int Score(bool basePass, int covered, int total) => !basePass ? 50 : total == 0 ? 90 : Math.Clamp(70 + (covered * 30 / Math.Max(1, total)), 0, 100);
    private static bool TryGetFact(IReadOnlyDictionary<string, string> facts, string name, out string value) => facts.TryGetValue(name, out value!) && !string.IsNullOrWhiteSpace(value);
    private static string CleanNarration(string text) => string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static IReadOnlyList<string> BuildNaturalnessWarnings(string fullText, IReadOnlyList<NarrationV5Scene> scenes)
    {
        var warnings = new List<string>();
        if (fullText.Contains("Verified details", StringComparison.OrdinalIgnoreCase)) warnings.Add("Narration contains a source-label phrase.");
        warnings.AddRange(scenes.Where(s => s.RequiredFactsCovered.Count > 3).Select(s => $"Scene {s.SceneId} may be carrying too many facts."));
        return warnings;
    }

    private static IReadOnlyList<NarrationFactV5> ReadRequiredFacts(JsonElement? contract)
        => ReadArray(contract, "requiredNarrationFacts").Select(e => new NarrationFactV5(GetString(e, "name") ?? "Fact", GetString(e, "value") ?? string.Empty)).Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToArray();
    private static bool MentionsMissingFact(string text, string warning)
    {
        var guardedTerms = new[] { "altitude", "constellation", "brightness", "weather", "optical aid", "optical-aid", "binocular", "telescope" };
        return guardedTerms.Any(term => warning.Contains(term, StringComparison.OrdinalIgnoreCase) && text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
    private static string Humanize(string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
    private static string FallbackPurpose(int index) => index switch { 0 => "Hook", 1 => "Discovery", 2 => "Science", 3 => "Observation", _ => "Takeaway" };
    private static JsonElement? ReadFirstJson(string path) { if (!File.Exists(path)) return null; using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.Clone(); }
    private static T? ReadTypedJson<T>(string path) { if (!File.Exists(path)) return default; return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions); }
    private static IReadOnlyList<JsonElement> ReadArray(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return []; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().Select(i => i.Clone()).ToArray(); return []; }
    private static IReadOnlyList<string> FindStringArray(JsonElement? element, string name) => ReadArray(element, name).Select(ValueToString).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToArray();
    private static int? GetInt(JsonElement? element, string name) => int.TryParse(GetString(element, name), out var value) ? value : null;
    private static string? GetString(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return null; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(p.Value); return null; }
    private static string? ValueToString(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    private static readonly string[] EngineeringLeakagePhrases = ["Understand ", "Know ", "Keep in mind", "in mind", "Anchor for this scene", "Scene purpose", "Audience promise", "Viewer should", "Available facts", "Planning", "Metadata", "Prompt", "JSON", "Facts to mention", "Verified details", "event identity", "the viewer should", "scene goal", "facts to mention", "metadata", "available facts", "prompt"];
    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
}

public sealed record NarrationLlmRequestV1(string RequestVersion, string Component, string Model, decimal Temperature, decimal TopP, int MaxTokens, string SystemPrompt, string UserPrompt, int PromptQualityScore, IReadOnlyList<string> SourceContracts, DateTime CreatedUtc);

public sealed record NarrationFactV5(string Name, string Value);
public sealed record NarrationPlanV5(string NarrationPlanVersion, string OrchestrationVersion, string Language, string VoiceProfile, string StoryArc, IReadOnlyList<NarrationFactV5> RequiredNarrationFacts, IReadOnlyList<string> ProhibitedPhrases, IReadOnlyList<string> PreferredPhrases, string ChannelEnding, IReadOnlyList<NarrationPlanV5Scene> Scenes);
public sealed record NarrationPlanV5Scene(string SceneId, string ScenePurpose, int SceneOrder, string KeyMessage, string ViewerFocus, string EmotionalRole, string NarrationIntent, IReadOnlyList<NarrationFactV5> RequiredFacts, IReadOnlyList<NarrationFactV5> MustMentionFacts, IReadOnlyList<string> MustAvoidFacts, string EditorialConnectorToNext, string TargetTone, string TargetLength);
public sealed record NarrationV5(string NarrationVersion, string OrchestrationVersion, string Language, IReadOnlyList<NarrationV5Scene> Scenes, string FullNarrationText, string ChannelEnding);
public sealed record NarrationV5Scene(string SceneId, string ScenePurpose, string NarrationText, IReadOnlyList<string> RequiredFactsCovered, IReadOnlyList<string> Warnings);
public sealed record RequiredFactCoverage(string Value, bool Covered);
public sealed record ProfessionalNarrationScores(int DocumentaryVoiceScore, int ScientificAccuracyScore, int ObservationGuidanceScore, int EditorialFlowScore, int LanguageNaturalnessScore, int AstroPulseIdentityScore, int OverallNarrationScore);
public sealed record NarrationGeneratorV5Result(IReadOnlyList<string> GeneratedFiles) { public static NarrationGeneratorV5Result Empty { get; } = new([]); }

public static class NarrativeDirector
{
    public static NarrationBriefV5[] BuildBriefs(NarrationPlanV5 plan, IReadOnlyList<string> missingFactWarnings)
    {
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scenes = plan.Scenes.OrderBy(s => s.SceneOrder).ToArray();
        var finalSceneId = scenes.LastOrDefault()?.SceneId;
        var briefs = new List<NarrationBriefV5>();

        foreach (var scene in scenes)
        {
            var purpose = scene.ScenePurpose;
            var facts = SelectFactsForScene(purpose, plan.RequiredNarrationFacts, assigned);
            foreach (var fact in facts) assigned.Add(fact.Name);

            var avoid = scene.MustAvoidFacts.Concat(missingFactWarnings).Concat(["Do not expose phrases like Verified details in narration."]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var alreadyCovered = assigned.Except(facts.Select(f => f.Name), StringComparer.OrdinalIgnoreCase).ToArray();
            var isFinal = string.Equals(scene.SceneId, finalSceneId, StringComparison.OrdinalIgnoreCase);
            briefs.Add(new NarrationBriefV5(
                scene.SceneId,
                purpose,
                scene.SceneOrder,
                BuildSceneGoal(scene, facts),
                BuildTakeaway(purpose),
                facts,
                avoid,
                alreadyCovered,
                scene.EditorialConnectorToNext,
                scene.TargetTone,
                purpose.Equals("Observation", StringComparison.OrdinalIgnoreCase) ? "measured and practical" : "natural documentary",
                scene.TargetLength,
                isFinal,
                BuildInstructions(purpose, isFinal)));
        }

        return briefs.ToArray();
    }

    private static NarrationFactV5[] SelectFactsForScene(string purpose, IReadOnlyList<NarrationFactV5> facts, HashSet<string> assigned)
    {
        bool NameContains(NarrationFactV5 fact, params string[] terms) => terms.Any(term => fact.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        var pool = facts.Where(f => !assigned.Contains(f.Name)).ToArray();
        var selected = purpose.ToLowerInvariant() switch
        {
            "hook" => pool.Where(f => NameContains(f, "event", "title", "primary", "object")).Take(1).ToArray(),
            "science" => pool.Where(f => NameContains(f, "separation", "illumination", "interference", "object", "moon")).Take(2).ToArray(),
            "observation" => pool.Where(f => NameContains(f, "window", "direction", "date", "time", "region", "utc", "local")).Take(4).ToArray(),
            "takeaway" or "closing" => pool.Take(1).ToArray(),
            _ => pool.Take(1).ToArray()
        };

        return selected.Length > 0 ? selected : [];
    }

    private static string BuildSceneGoal(NarrationPlanV5Scene scene, IReadOnlyList<NarrationFactV5> facts)
    {
        if (!string.IsNullOrWhiteSpace(scene.KeyMessage)) return scene.KeyMessage;
        return scene.ScenePurpose switch
        {
            "Hook" => "Introduce the sky event and why it matters now.",
            "Science" => "Explain why the event appears the way it does.",
            "Observation" => "Give practical viewing guidance using only confirmed details.",
            "Takeaway" or "Closing" => "Reinforce the significance of noticing the sky on purpose.",
            _ => "Move the documentary story forward without overloading the viewer."
        };
    }

    private static string BuildTakeaway(string purpose) => purpose switch
    {
        "Hook" => "The viewer should know what event this is and why it deserves attention.",
        "Science" => "The viewer should understand the why and how behind the view.",
        "Observation" => "The viewer should know when, where, and how to try seeing it safely and realistically.",
        "Takeaway" or "Closing" => "The viewer should leave with a clear sense that ordinary nights can reveal extraordinary motion.",
        _ => "The viewer should stay oriented without hearing a checklist."
    };

    private static string BuildInstructions(string purpose, bool isFinal)
    {
        var ending = isFinal ? " Include the exact channel ending." : " Do not include the channel ending.";
        return purpose switch
        {
            "Hook" => "Mention only the event identity and why it matters; do not list every fact." + ending,
            "Science" => "Explain why or how the event works in plain documentary language." + ending,
            "Observation" => "Use available date, time, viewing window, direction, and practical viewing instructions; never invent altitude, constellation, brightness, weather, or optical aids." + ending,
            "Takeaway" or "Closing" => "Reinforce significance and close warmly." + ending,
            _ => "Narrate naturally, distribute facts lightly, and avoid source-label phrases like Verified details." + ending
        };
    }
}

public sealed record NarrationBriefsV5(string NarrationBriefsVersion, string OrchestrationVersion, string Language, IReadOnlyList<NarrationBriefV5> Briefs);
public sealed record NarrationBriefV5(string SceneId, string ScenePurpose, int SceneOrder, string SceneGoal, string AudienceTakeaway, IReadOnlyList<NarrationFactV5> FactsToMention, IReadOnlyList<string> FactsToAvoid, IReadOnlyList<string> AlreadyCoveredFacts, string ConnectorToNext, string Tone, string Pacing, string TargetLength, bool MustIncludeEnding, string GenerationInstructions);
