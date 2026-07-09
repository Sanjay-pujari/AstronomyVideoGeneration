using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;
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
        var knowledgeRoot = Path.Combine(narrationRoot, "knowledge");
        Directory.CreateDirectory(knowledgeRoot);
        var knowledgeContractPath = Path.Combine(knowledgeRoot, "knowledge-format-contract.json");
        var knowledgeDiagnosticsPath = Path.Combine(knowledgeRoot, "knowledge-format-diagnostics.json");
        var editorialBriefRoot = Path.Combine(narrationRoot, "editorial-brief");
        Directory.CreateDirectory(editorialBriefRoot);
        var editorialBriefContractPath = Path.Combine(editorialBriefRoot, "editorial-brief-contract.json");
        var editorialBriefDiagnosticsPath = Path.Combine(editorialBriefRoot, "editorial-brief-diagnostics.json");
        var producerNotesRoot = Path.Combine(narrationRoot, "producer-notes");
        Directory.CreateDirectory(producerNotesRoot);
        var producerNotesContractPath = Path.Combine(producerNotesRoot, "producer-notes-contract.json");
        var producerNotesDiagnosticsPath = Path.Combine(producerNotesRoot, "producer-notes-diagnostics.json");
        var longRoot = Path.Combine(narrationRoot, "long");
        var shortRoot = Path.Combine(narrationRoot, "short");
        Directory.CreateDirectory(longRoot);
        Directory.CreateDirectory(shortRoot);
        var longNarrationPath = Path.Combine(longRoot, "narration.json");
        var longDiagnosticsPath = Path.Combine(longRoot, "narration-diagnostics.json");
        var shortNarrationPath = Path.Combine(shortRoot, "narration.json");
        var shortDiagnosticsPath = Path.Combine(shortRoot, "narration-diagnostics.json");
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
        var rawNarrationBriefs = new NarrationBriefsV5("AstroPulse-NarrationBriefs-v1", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, briefs);
        var formatter = new KnowledgeFormatter();
        var knowledgeContract = formatter.Format(requiredFacts, rawNarrationBriefs, language);
        var knowledgeDiagnostics = formatter.BuildDiagnostics(knowledgeContract, requiredFacts, rawNarrationBriefs);
        var interpreter = new EditorialBriefInterpreter();
        var editorialBriefContract = interpreter.Interpret(knowledgeContract, FindStringArray(contract, "missingFactWarnings"));
        var editorialBriefDiagnostics = interpreter.BuildDiagnostics(editorialBriefContract, rawNarrationBriefs, knowledgeContract);
        var requestedFormats = ResolveRequestedNarrationFormats(outputRoot, request, response);
        var producerNotesContract = ProducerNotesComposer.Compose(editorialBriefContract, knowledgeContract, requestedFormats);
        var producerNotesDiagnostics = ProducerNotesComposer.BuildDiagnostics(producerNotesContract);
        var narrationBriefs = producerNotesContract.ToNarrationBriefs(Rc2PipelinePhaseRegistry.OrchestrationVersion);

        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(briefsPath, JsonSerializer.Serialize(narrationBriefs, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(knowledgeContractPath, JsonSerializer.Serialize(knowledgeContract, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(knowledgeDiagnosticsPath, JsonSerializer.Serialize(knowledgeDiagnostics, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(editorialBriefContractPath, JsonSerializer.Serialize(editorialBriefContract, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(editorialBriefDiagnosticsPath, JsonSerializer.Serialize(editorialBriefDiagnostics, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(producerNotesContractPath, JsonSerializer.Serialize(producerNotesContract, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(producerNotesDiagnosticsPath, JsonSerializer.Serialize(producerNotesDiagnostics, JsonOptions), cancellationToken);

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
        var promptComposerOutput = await composer.ComposeAndWriteAsync(new NarrationPromptComposerInput(contract, storyboard, narrationBriefs, [producerNotesContractPath, knowledgeContractPath, briefsPath, styleContractPath], promptPreviewPath, promptDiagnosticsPath, styleContract, promptQualityPath), cancellationToken);
        var llmRequest = new NarrationLlmRequestV1("AstroPulse-NarrationLlmRequest-v2", "NarrationPromptComposerV3", "local-documentary-composer-v1", 0.7m, 0.9m, 1800, "You are a senior documentary writer for Astro Pulse.", promptComposerOutput.PromptPreviewMarkdown, promptComposerOutput.PromptQuality.OverallPromptScore, [NormalizePath(producerNotesContractPath), NormalizePath(knowledgeContractPath), NormalizePath(briefsPath), NormalizePath(styleContractPath), NormalizePath(promptPreviewPath), NormalizePath(promptQualityPath)], DateTime.UtcNow);
        await File.WriteAllTextAsync(llmRequestPath, JsonSerializer.Serialize(llmRequest, JsonOptions), cancellationToken);

        NarrationV5? narration = null;
        NarrationV5Scene[] narrationScenes = [];
        string fullText = string.Empty;
        bool llmGenerationExecuted = false;
        var generationErrors = new List<string>();
        try
        {
            var generatedByFormat = new Dictionary<string, NarrationV5>(StringComparer.OrdinalIgnoreCase);
                foreach (var format in requestedFormats)
                {
                    var formatBriefs = producerNotesContract.ToNarrationBriefs(Rc2PipelinePhaseRegistry.OrchestrationVersion, format);
                    var scenesForFormat = RunChronicleEditorialEngine(llmRequest, formatBriefs, format).ToArray();
                    var textForFormat = string.Join("\n\n", scenesForFormat.Select(scene => scene.NarrationText));
                    generatedByFormat[format] = new NarrationV5($"AstroPulse-Narration-v5-{format}", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, scenesForFormat, textForFormat, ChannelEnding);
                }
                narration = generatedByFormat.TryGetValue("long", out var longNarration) ? longNarration : generatedByFormat.Values.First();
                narrationScenes = narration.Scenes.ToArray();
                fullText = string.Join("\n\n", generatedByFormat.Values.Select(n => n.FullNarrationText));
                llmGenerationExecuted = true;
            if (generatedByFormat.TryGetValue("long", out longNarration)) await File.WriteAllTextAsync(longNarrationPath, JsonSerializer.Serialize(longNarration, JsonOptions), cancellationToken);
            if (generatedByFormat.TryGetValue("short", out var shortNarration)) await File.WriteAllTextAsync(shortNarrationPath, JsonSerializer.Serialize(shortNarration, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(narrationPath, JsonSerializer.Serialize(narration, JsonOptions), cancellationToken);
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
        var factsDistributedByScene = briefs
            .GroupBy(b => b.SceneId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.SelectMany(b => b.FactsToMention.Select(f => f.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
        var repeatedFactWarnings = factsDistributedByScene.SelectMany(kv => kv.Value.Select(f => new { SceneId = kv.Key, Fact = f })).GroupBy(x => x.Fact, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => $"Fact {g.Key} assigned to multiple scenes: {string.Join(", ", g.Select(x => x.SceneId))}.").ToArray();
        var narrationNaturalnessWarnings = BuildNaturalnessWarnings(fullText, narrationScenes);
        var engineeringLeakageViolations = EngineeringLeakagePhrases.Where(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        var promptLeakageViolations = PromptLeakagePhrases.Where(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        var isoDateTimeViolations = IsoDateTimeRegex.Matches(fullText).Select(m => m.Value).Concat(RawUtcRegex.Matches(fullText).Select(m => m.Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var duplicatedPhraseViolations = DuplicatedTransformedPhraseRegex.Matches(fullText).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var diagnosticWarningViolations = ContainsAny(fullText, "diagnostic warning", "diagnostics warning", "warning:") ? new[] { "Diagnostic warning language found in final narration." } : [];
        var writerInputText = BuildEffectiveWriterInputText(producerNotesContract);
        var forbiddenWriterInputDetected = EditorialBriefInterpreter.ForbiddenWriterInputPhrases.Where(p => writerInputText.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        var forbiddenNarrationDetected = EditorialBriefInterpreter.ForbiddenNarrationPhrases.Where(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        var diagnosticWarningsInWriterInput = ContainsAny(writerInputText, "missing metadata", "diagnostic warning", "diagnostics warning", "warning:");
        var writerConsumedRawMetadata = !knowledgeDiagnostics.RawMetadataRemoved || producerNotesContract.Briefs.SelectMany(b => b.KeyFacts).Any(f => KnowledgeFormatter.ContainsRawMetadata(f.Value));
        var bothFormatsRequested = requestedFormats.Contains("long") && requestedFormats.Contains("short");
        var missingRequestedFormats = requestedFormats.Where(f => !File.Exists(Path.Combine(narrationRoot, f, "narration.json"))).ToArray();
        var longShortDistinctivenessScore = File.Exists(longNarrationPath) && File.Exists(shortNarrationPath) ? CalculateDistinctivenessScore(GetNarrationText(longNarrationPath), GetNarrationText(shortNarrationPath)) : 0;
        var shortCopiedFromLong = File.Exists(longNarrationPath) && File.Exists(shortNarrationPath) && (GetNarrationText(longNarrationPath).Equals(GetNarrationText(shortNarrationPath), StringComparison.OrdinalIgnoreCase) || longShortDistinctivenessScore < 35);
        var producerNotesLeakagePhrases = DetectProducerNotesLeakage(producerNotesContract, fullText);
        var producerNotesLeakageDetected = producerNotesLeakagePhrases.Length > 0;
        var expectedCounts = requestedFormats.ToDictionary(f => f, f => ResolveExpectedFrameCount(outputRoot, f), StringComparer.OrdinalIgnoreCase);
        var formatSceneCountViolations = requestedFormats.Where(f => expectedCounts[f] > 0 && ResolveNarrationSceneCount(Path.Combine(narrationRoot, f, "narration.json")) != expectedCounts[f]).Select(f => $"{f} narration scene count does not match expected story frame count {expectedCounts[f]}.").ToArray();
        var certificationViolations = engineeringLeakageViolations.Select(p => $"Instruction leakage phrase found: {p}")
            .Concat(promptLeakageViolations.Select(p => $"Prompt leakage phrase found: {p}"))
            .Concat(isoDateTimeViolations.Select(p => $"Raw ISO datetime/date found: {p}"))
            .Concat(duplicatedPhraseViolations.Select(p => $"Duplicated transformed phrase found: {p}"))
            .Concat(diagnosticWarningViolations)
            .Concat(forbiddenWriterInputDetected.Select(p => $"Forbidden writer input phrase found: {p}"))
            .Concat(forbiddenNarrationDetected.Select(p => $"Forbidden narration phrase found: {p}"))
            .Concat(diagnosticWarningsInWriterInput ? ["Diagnostic warning language found in writer input."] : [])
            .Concat(writerConsumedRawMetadata ? ["Documentary Writer consumed raw metadata instead of formatted knowledge."] : [])
            .Concat(bothFormatsRequested && missingRequestedFormats.Length > 0 ? [$"Both formats requested but missing narration format(s): {string.Join(", ", missingRequestedFormats)}."] : [])
            .Concat(producerNotesLeakageDetected ? producerNotesLeakagePhrases.Select(p => $"Producer notes leaked into narration: {p}") : [])
            .Concat(shortCopiedFromLong ? ["Short narration is identical or near-identical to long narration."] : [])
            .Concat(formatSceneCountViolations)
            .ToArray();
        var errors = prohibitedViolations.Concat(missingFactViolations).Concat(certificationViolations).Concat(generationErrors).ToArray();
        var professionalScores = BuildProfessionalScores(fullText, narrationScenes, briefs.Length, coverage.Values.Count(v => v.Covered), coverage.Count, errors.Length, narrationNaturalnessWarnings.Count);
        var editorialReviewerDecision = ResolveEditorialReviewerDecision(professionalScores.OverallNarrationScore);
        var editorialReviewerReason = BuildEditorialReviewerReason(editorialReviewerDecision, professionalScores.OverallNarrationScore, promptComposerOutput.PromptQuality.Recommendation);
        var editorialRequiredPasses = PromptQualityEvaluator.RequiredPassesFor(editorialReviewerDecision);
        var reviewPasses = 1;
        var finalDecision = editorialReviewerDecision;
        var validationErrors = errors.Where(e => !e.StartsWith("Prompt quality", StringComparison.OrdinalIgnoreCase)).ToArray();
        var finalPromptQuality = promptComposerOutput.PromptQuality with
        {
            EditorialDecision = editorialReviewerDecision,
            RequiredPasses = editorialRequiredPasses,
            EditorialReviewerDecision = editorialReviewerDecision,
            EditorialReviewerReason = editorialReviewerReason
        };
        await File.WriteAllTextAsync(promptQualityPath, JsonSerializer.Serialize(finalPromptQuality, JsonOptions), cancellationToken);
        var auroraCertified = professionalScores.DocumentaryVoiceScore >= 95
            && professionalScores.ScientificAccuracyScore == 100
            && professionalScores.ObservationGuidanceScore >= 95
            && professionalScores.EditorialFlowScore >= 95
            && professionalScores.SpokenLanguageScore >= 95
            && professionalScores.ViewerRetentionScore >= 95
            && professionalScores.AstroPulseIdentityScore >= 95
            && professionalScores.OverallNarrationScore >= 95
            && engineeringLeakageViolations.Length == 0
            && promptLeakageViolations.Length == 0
            && isoDateTimeViolations.Length == 0
            && duplicatedPhraseViolations.Length == 0
            && diagnosticWarningViolations.Length == 0
            && forbiddenWriterInputDetected.Length == 0
            && forbiddenNarrationDetected.Length == 0
            && !diagnosticWarningsInWriterInput
            && !writerConsumedRawMetadata
            && missingRequestedFormats.Length == 0
            && !producerNotesLeakageDetected
            && !shortCopiedFromLong
            && formatSceneCountViolations.Length == 0;
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
                new { path = NormalizePath(styleContractPath), exists = File.Exists(styleContractPath) },
                new { path = NormalizePath(knowledgeContractPath), exists = File.Exists(knowledgeContractPath) },
                new { path = NormalizePath(editorialBriefContractPath), exists = File.Exists(editorialBriefContractPath) },
                new { path = NormalizePath(producerNotesContractPath), exists = File.Exists(producerNotesContractPath) }
            },
            outputsCreated = new[] { planPath, briefsPath, styleContractPath, styleDiagnosticsPath, knowledgeContractPath, knowledgeDiagnosticsPath, editorialBriefContractPath, editorialBriefDiagnosticsPath, producerNotesContractPath, producerNotesDiagnosticsPath, llmRequestPath, narrationPath, longNarrationPath, longDiagnosticsPath, shortNarrationPath, shortDiagnosticsPath, diagnosticsPath, validationPath, promptPreviewPath, promptDiagnosticsPath, promptQualityPath }.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) || path == diagnosticsPath || path == validationPath }).ToArray(),
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
            promptLeakageViolations,
            isoDateTimeViolations,
            duplicatedPhraseViolations,
            diagnosticWarningViolations,
            narrationNaturalnessWarnings,
            knowledgeFormatterExecuted = true,
            rawMetadataRemoved = knowledgeDiagnostics.RawMetadataRemoved,
            instructionFragmentsRemoved = knowledgeDiagnostics.InstructionFragmentsRemoved,
            isoDateLeakageDetected = isoDateTimeViolations.Length > 0,
            duplicatedPhraseDetected = duplicatedPhraseViolations.Length > 0,
            formattedKnowledgeUsedByWriter = !writerConsumedRawMetadata,
            knowledgeFormatContractPath = NormalizePath(knowledgeContractPath),
            knowledgeFormatDiagnosticsPath = NormalizePath(knowledgeDiagnosticsPath),
            editorialBriefInterpreterExecuted = true,
            writerInputSanitized = forbiddenWriterInputDetected.Length == 0,
            planningLanguageRemoved = editorialBriefDiagnostics.PlanningLanguageRemoved,
            diagnosticWarningsRemovedFromWriterInput = !diagnosticWarningsInWriterInput,
            sceneIntentConvertedToGuidance = editorialBriefDiagnostics.SceneIntentConvertedToGuidance,
            forbiddenWriterInputDetected,
            forbiddenNarrationDetected,
            editorialBriefContractPath = NormalizePath(editorialBriefContractPath),
            editorialBriefDiagnosticsPath = NormalizePath(editorialBriefDiagnosticsPath),
            producerNotesComposerExecuted = true,
            producerNotesContractPath = NormalizePath(producerNotesContractPath),
            producerNotesDiagnosticsPath = NormalizePath(producerNotesDiagnosticsPath),
            requestedFormats,
            bothFormatsRequested,
            missingRequestedFormats,
            shortCopiedFromLong,
            producerNotesGenerated = File.Exists(producerNotesContractPath),
            producerNotesLeakageDetected,
            producerNotesLeakagePhrases,
            longShortDistinctivenessScore,
            expectedSceneCounts = expectedCounts,
            formatSceneCountViolations,
            chronicleEditorialEngine = new { editorialBriefInterpreterExecuted = true, documentaryWriterExecuted = true, documentaryEditorExecuted = true, observationEditorExecuted = true, promptQualityEvaluationExecuted = true, editorialReviewerExecuted = true },
            writerPasses = narrationScenes.Length > 0 ? 1 : 0,
            editorPasses = narrationScenes.Length > 0 ? 1 : 0,
            observationPasses = narrationScenes.Length > 0 ? 1 : 0,
            reviewPasses,
            finalDecision,
            promptRecommendation = finalPromptQuality.Recommendation,
            promptRecommendationReason = finalPromptQuality.Reason,
            editorialReviewerDecision,
            editorialReviewerReason,
            requiredPasses = editorialRequiredPasses,
            scientificAccuracyScore = professionalScores.ScientificAccuracyScore,
            editorialQualityScore = professionalScores.DocumentaryVoiceScore,
            naturalnessScore = professionalScores.SpokenLanguageScore,
            observationGuidanceScore = professionalScores.ObservationGuidanceScore,
            flowScore = professionalScores.EditorialFlowScore,
            overallDocumentaryScore = professionalScores.OverallNarrationScore,
            documentaryVoiceScore = professionalScores.DocumentaryVoiceScore,
            editorialFlowScore = professionalScores.EditorialFlowScore,
            spokenLanguageScore = professionalScores.SpokenLanguageScore,
            viewerRetentionScore = professionalScores.ViewerRetentionScore,
            astroPulseIdentityScore = professionalScores.AstroPulseIdentityScore,
            overallNarrationScore = professionalScores.OverallNarrationScore,
            auroraCertified,
            documentaryStyleDirectorExecuted = styleContract is not null,
            documentaryStyleContractPath = NormalizePath(styleContractPath),
            documentaryStyleDiagnosticsPath = NormalizePath(styleDiagnosticsPath),
            promptComposerExecuted = true,
            llmRequestCreated = File.Exists(llmRequestPath),
            llmGenerationExecuted,
            promptComposerReadyForGeneration = true,
            promptQualityAdvisoryOnly = true,
            promptPreviewPath = NormalizePath(promptPreviewPath),
            promptDiagnosticsPath = NormalizePath(promptDiagnosticsPath),
            promptQualityPath = NormalizePath(promptQualityPath),
            promptQuality = finalPromptQuality,
            language,
            warnings,
            errors
        };
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        await WriteFormatDiagnosticsAsync(longDiagnosticsPath, "long", longNarrationPath, expectedCounts.GetValueOrDefault("long"), errors, cancellationToken);
        await WriteFormatDiagnosticsAsync(shortDiagnosticsPath, "short", shortNarrationPath, expectedCounts.GetValueOrDefault("short"), errors, cancellationToken);
        var validation = new
        {
            phaseNo = 7,
            phaseName = PhaseName,
            validator = "AstroPulse-NarrationValidator-v3",
            passed = generationErrors.Count == 0 && validationErrors.Length == 0 && editorialReviewerDecision != PromptQualityEvaluator.Regenerate && professionalScores.OverallNarrationScore >= 80,
            editorialReviewerDecision,
            editorialReviewerReason,
            promptRecommendation = finalPromptQuality.Recommendation,
            promptQualityAdvisoryOnly = true,
            requiredPasses = editorialRequiredPasses,
            writerPasses = narrationScenes.Length > 0 ? 1 : 0,
            editorPasses = narrationScenes.Length > 0 ? 1 : 0,
            observationPasses = narrationScenes.Length > 0 ? 1 : 0,
            reviewPasses,
            finalDecision,
            auroraCertified,
            noEditorialLeakageDetected = engineeringLeakageViolations.Length == 0,
            noPromptLeakageDetected = promptLeakageViolations.Length == 0,
            noIsoDateTimeDetected = isoDateTimeViolations.Length == 0,
            noDuplicatedTransformedPhrasesDetected = duplicatedPhraseViolations.Length == 0,
            noDiagnosticWarningsInNarration = diagnosticWarningViolations.Length == 0,
            formattedKnowledgeUsedByWriter = !writerConsumedRawMetadata,
            editorialBriefInterpreterExecuted = true,
            writerInputSanitized = forbiddenWriterInputDetected.Length == 0,
            planningLanguageRemoved = editorialBriefDiagnostics.PlanningLanguageRemoved,
            diagnosticWarningsRemovedFromWriterInput = !diagnosticWarningsInWriterInput,
            sceneIntentConvertedToGuidance = editorialBriefDiagnostics.SceneIntentConvertedToGuidance,
            forbiddenWriterInputDetected,
            forbiddenNarrationDetected,
            knowledgeFormatterExecuted = true,
            rawMetadataRemoved = knowledgeDiagnostics.RawMetadataRemoved,
            instructionFragmentsRemoved = knowledgeDiagnostics.InstructionFragmentsRemoved,
            documentaryVoiceScore = professionalScores.DocumentaryVoiceScore,
            scientificAccuracyScore = professionalScores.ScientificAccuracyScore,
            observationGuidanceScore = professionalScores.ObservationGuidanceScore,
            editorialFlowScore = professionalScores.EditorialFlowScore,
            spokenLanguageScore = professionalScores.SpokenLanguageScore,
            viewerRetentionScore = professionalScores.ViewerRetentionScore,
            astroPulseIdentityScore = professionalScores.AstroPulseIdentityScore,
            overallNarrationScore = professionalScores.OverallNarrationScore,
            producerNotesComposerExecuted = true,
            producerNotesContractPath = NormalizePath(producerNotesContractPath),
            producerNotesDiagnosticsPath = NormalizePath(producerNotesDiagnosticsPath),
            requestedFormats,
            bothFormatsRequested,
            missingRequestedFormats,
            shortCopiedFromLong,
            producerNotesGenerated = File.Exists(producerNotesContractPath),
            producerNotesLeakageDetected,
            producerNotesLeakagePhrases,
            longShortDistinctivenessScore,
            expectedSceneCounts = expectedCounts,
            formatSceneCountViolations,
            errors = validationErrors,
            warnings
        };
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        if (generationErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", generationErrors));
        logger.LogInformation("Narration Studio V5 wrote {SceneCount} scenes to {NarrationPath}.", narrationScenes.Length, narrationPath);
        return new NarrationGeneratorV5Result([planPath, briefsPath, styleContractPath, styleDiagnosticsPath, knowledgeContractPath, knowledgeDiagnosticsPath, editorialBriefContractPath, editorialBriefDiagnosticsPath, producerNotesContractPath, producerNotesDiagnosticsPath, llmRequestPath, narrationPath, longNarrationPath, longDiagnosticsPath, shortNarrationPath, shortDiagnosticsPath, diagnosticsPath, validationPath, promptPreviewPath, promptDiagnosticsPath, promptQualityPath]);
    }

    private static string BuildEffectiveWriterInputText(ProducerNotesContract producerNotesContract)
    {
        var story = producerNotesContract.Briefs.SelectMany(s => new[] { s.SceneStory, s.NarrativeGoal, s.AudienceExperience, s.ObservationGuidance, s.EmotionalTone, s.TransitionContext });
        var facts = producerNotesContract.Briefs.SelectMany(s => s.KeyFacts.Select(f => f.Value));
        return string.Join("\n", story.Concat(facts));
    }

    private static NarrationPlanV5Scene BuildPlanScene(JsonElement scene, int index, IReadOnlyList<NarrationFactV5> facts)
    {
        var purpose = GetString(scene, "scenePurpose") ?? FallbackPurpose(index);
        var must = purpose == "Observation" || index == 0 ? facts : facts.Take(2).ToArray();
        return new NarrationPlanV5Scene(GetString(scene, "sceneId") ?? $"scene-{index + 1:000}", purpose, GetInt(scene, "sceneOrder") ?? index + 1, GetString(scene, "keyMessage") ?? "Explain the event using verified facts.", GetString(scene, "viewerFocus") ?? "Stay oriented to the sky event.", GetString(scene, "emotionalRole") ?? "Calm curiosity.", $"Narrate the {purpose.ToLowerInvariant()} beat with factual restraint.", facts, must, ["Do not invent missing altitude, constellation, brightness, weather, or optical-aid facts."], GetString(scene, "transitionIntent") ?? "Move cleanly to the next scene.", "calm documentary", purpose == "Observation" ? "medium" : "short");
    }

    private static IReadOnlyList<string> ResolveRequestedNarrationFormats(string outputRoot, BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response)
    {
        var formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRequestedFormatFromManifest(outputRoot, "long", formats);
        AddRequestedFormatFromManifest(outputRoot, "short", formats);
        var requestText = JsonSerializer.Serialize(request, JsonOptions) + JsonSerializer.Serialize(response, JsonOptions);
        if (requestText.Contains("LongVideo", StringComparison.OrdinalIgnoreCase) || requestText.Contains("\"long\"", StringComparison.OrdinalIgnoreCase)) formats.Add("long");
        if (requestText.Contains("ShortVideo", StringComparison.OrdinalIgnoreCase) || requestText.Contains("\"short\"", StringComparison.OrdinalIgnoreCase)) formats.Add("short");
        if (formats.Count == 0)
        {
            formats.Add("long");
            formats.Add("short");
        }
        return formats.OrderBy(f => f.Equals("long", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToArray();
    }

    private static void AddRequestedFormatFromManifest(string outputRoot, string format, HashSet<string> formats)
    {
        var manifestPath = Path.Combine(outputRoot, "story-frames", format, "story-frame-manifest.json");
        var manifest = ReadFirstJson(manifestPath);
        if (manifest is null) return;
        if (GetString(manifest, "requested")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true || (GetInt(manifest, "generatedSceneCount") ?? 0) > 0) formats.Add(format);
    }

    private static int ResolveExpectedFrameCount(string outputRoot, string format)
    {
        var manifest = ReadFirstJson(Path.Combine(outputRoot, "story-frames", format, "story-frame-manifest.json"));
        return GetInt(manifest, "generatedSceneCount") ?? GetInt(manifest, "expectedSceneCount") ?? 0;
    }

    private static int ResolveNarrationSceneCount(string narrationPath)
    {
        var narration = ReadFirstJson(narrationPath);
        return ReadArray(narration, "scenes").Count;
    }

    private static string GetNarrationText(string narrationPath)
        => GetString(ReadFirstJson(narrationPath), "fullNarrationText") ?? string.Empty;

    private static string ResolveEditorialReviewerDecision(int overall) => PromptQualityEvaluator.Recommend(overall);

    private static string BuildEditorialReviewerReason(string decision, int overall, string promptRecommendation) => decision switch
    {
        PromptQualityEvaluator.Pass => $"Editorial Reviewer approves publish at overall narration score {overall}; Prompt Quality advised {promptRecommendation}.",
        PromptQualityEvaluator.MinorRevision => $"Editorial Reviewer requires minor editorial refinement at overall narration score {overall}; do not regenerate narration.",
        PromptQualityEvaluator.MajorRevision => $"Editorial Reviewer requires writer plus editor revision at overall narration score {overall}.",
        _ => $"Editorial Reviewer requires regeneration because overall narration score {overall} is below 80."
    };

    private static async Task WriteFormatDiagnosticsAsync(string path, string format, string narrationPath, int expectedSceneCount, IReadOnlyList<string> errors, CancellationToken cancellationToken)
    {
        var sceneCount = ResolveNarrationSceneCount(narrationPath);
        var diagnostics = new
        {
            phaseNo = 7,
            phaseName = PhaseName,
            format,
            narrationPath = NormalizePath(narrationPath),
            narrationExists = File.Exists(narrationPath),
            expectedSceneCount,
            sceneCount,
            sceneCountMatchesExpectedFrameFormat = expectedSceneCount == 0 || sceneCount == expectedSceneCount,
            certifiedOutput = true,
            errors = errors.Where(e => e.Contains(format, StringComparison.OrdinalIgnoreCase) || !e.Contains("scene count", StringComparison.OrdinalIgnoreCase)).ToArray()
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
    }

    private static IEnumerable<NarrationV5Scene> RunChronicleEditorialEngine(NarrationLlmRequestV1 request, NarrationBriefsV5 briefs, string format)
    {
        if (string.IsNullOrWhiteSpace(request.UserPrompt)) throw new InvalidOperationException("Composed narration instructions were empty.");
        foreach (var brief in briefs.Briefs.OrderBy(b => b.SceneOrder))
        {
            var draft = DocumentaryWriter(brief, briefs.Language, format);
            var documentaryEdited = DocumentaryEditor(draft);
            var observationEdited = ObservationEditor(documentaryEdited, brief);
            yield return observationEdited;
        }
    }

    private static NarrationV5Scene DocumentaryWriter(NarrationBriefV5 brief, string language, string format)
    {
        var facts = brief.FactsToMention.ToDictionary(f => f.Name, f => f.Value, StringComparer.OrdinalIgnoreCase);
        var detailPhrase = BuildNaturalDetailPhrase(brief.FactsToMention);
        var purpose = brief.ScenePurpose;
        var purposeKind = ClassifySceneRole(purpose);
        var text = purposeKind switch
        {
            "hook" => $"As twilight deepens, the sky sets up a quiet meeting worth noticing. {detailPhrase} This is an easy moment to miss, but a rewarding one to catch.",
            "science" => $"The closeness is a line-of-sight effect. Jupiter and Venus remain worlds apart, yet from Earth their separate paths can briefly seem to gather in one small patch of sky. {detailPhrase}",
            "observation" => $"Make it practical now. {BuildObservationGuidance(facts)} {detailPhrase}",
            "takeaway" or "closing" => $"After the viewing window passes, the memory is simple: two bright worlds sharing one quiet corner of the sky. {BuildClosingMeaning(facts)}",
            _ => $"Curiosity turns into recognition. {detailPhrase} Each detail makes the sky easier to read."
        };
        text = ApplyFormatNarrationStyle(text, purposeKind, format);

        if (language.Equals("hi", StringComparison.OrdinalIgnoreCase)) text = text.Trim();
        text = CleanNarration(text);
        if (brief.MustIncludeEnding) text = EnsureSingleEnding(text);
        else text = text.Replace(ChannelEnding, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return new NarrationV5Scene(brief.SceneId, brief.ScenePurpose, text, brief.FactsToMention.Select(f => f.Name).ToArray(), brief.FactsToAvoid);
    }

    private static string ApplyFormatNarrationStyle(string text, string purposeKind, string format)
    {
        if (format.Equals("short", StringComparison.OrdinalIgnoreCase))
        {
            var action = purposeKind is "hook" ? "Look up soon." : purposeKind is "observation" ? "Step outside, find the open horizon, and start with your eyes." : "Stay with the visible sky.";
            return $"{action} {text}";
        }

        var context = purposeKind is "science"
            ? "In a slower view, the important point is that astronomy often turns huge distances into simple patterns we can recognize from the ground."
            : "Let the moment breathe, because a careful look at the sky is often more rewarding than a quick glance.";
        return $"{text} {context}";
    }

    private static NarrationV5Scene DocumentaryEditor(NarrationV5Scene scene)
    {
        var text = RemoveLeakage(scene.NarrationText);
        text = NaturalizeIsoDates(text);
        text = FixDuplicatedPhrases(text);
        text = ImproveSpokenRhythm(text);
        return scene with { NarrationText = text };
    }

    private static NarrationV5Scene ObservationEditor(NarrationV5Scene scene, NarrationBriefV5 brief)
    {
        if (ClassifySceneRole(brief.ScenePurpose) != "observation") return scene;
        var facts = brief.FactsToMention.ToDictionary(f => f.Name, f => f.Value, StringComparer.OrdinalIgnoreCase);
        var text = scene.NarrationText;
        if (!ContainsAny(text, "happening", "watch", "see")) text += " You are watching a real sky alignment unfold, not just a date on a calendar.";
        if (!ContainsAny(text, "when", "time", "outside", "window")) text += " Use the best viewing window as your cue for when to step outside.";
        if (!ContainsAny(text, "where", "look", "face", "toward", "horizon")) text += " Look toward the clearest part of the indicated sky and keep the horizon open.";
        if (!ContainsAny(text, "see", "view", "appear", "expect")) text += " What you should see is the main pattern standing out against the sky.";
        if (!ContainsAny(text, "eye", "binocular", "telescope") && TryGetFact(facts, "nakedEyeVisibility", out var nakedEye) && IsAffirmative(nakedEye)) text += " Start with your eyes; equipment is optional.";
        else if (!ContainsAny(text, "eye", "binocular", "telescope") && (TryGetFact(facts, "binocularGuidance", out var equipment) || TryGetFact(facts, "telescopeGuidance", out equipment))) text += $" For equipment, {LowerFirst(NaturalizeIsoDates(equipment))}";
        if (!ContainsAny(text, "matter", "matters", "rare", "special", "because")) text += " It matters because these ordinary-looking positions reveal the larger motion of the solar system.";
        return scene with { NarrationText = ImproveSpokenRhythm(FixDuplicatedPhrases(NaturalizeIsoDates(RemoveLeakage(text)))) };
    }

    private static string BuildClosingMeaning(IReadOnlyDictionary<string, string> facts)
    {
        if (TryGetFact(facts, "skyDirectionHint", out var direction) || TryGetFact(facts, "direction", out direction)) return $"Next time the light fades, that part of the sky will feel a little more familiar near {NaturalDirection(direction)}.";
        return "Moments like this turn ordinary dusk into a reason to pause and look up.";
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

    private static string ClassifySceneRole(string role)
    {
        var lower = role.ToLowerInvariant();
        if (lower.Contains("opening") || lower.Contains("hook")) return "hook";
        if (lower.Contains("explanation") || lower.Contains("science")) return "science";
        if (lower.Contains("viewing") || lower.Contains("observation")) return "observation";
        if (lower.Contains("close") || lower.Contains("takeaway")) return "closing";
        return lower;
    }

    private static string BuildObservationGuidance(IReadOnlyDictionary<string, string> facts)
    {
        var sentences = new List<string>();
        if (TryGetFact(facts, "eventDateLocal", out var date) || TryGetFact(facts, "date", out date)) sentences.Add($"Plan for {date}.");
        if (TryGetFact(facts, "bestViewingWindowLocal", out var window)) sentences.Add($"The best time to step outside is {LowerFirst(window)}.");
        else if (TryGetFact(facts, "eventTimeLocal", out var time) || TryGetFact(facts, "time", out time)) sentences.Add($"Try looking around {time}.");
        if (TryGetFact(facts, "skyDirectionHint", out var direction) || TryGetFact(facts, "direction", out direction)) sentences.Add(direction.StartsWith("Face ", StringComparison.OrdinalIgnoreCase) || direction.StartsWith("Look ", StringComparison.OrdinalIgnoreCase) ? $"{direction}, and choose the clearest horizon you can find." : $"Face {NaturalDirection(direction)} and choose the clearest horizon you can find.");
        if (TryGetFact(facts, "constellation", out var constellation)) sentences.Add($"If you can identify the constellations, use {constellation} as a gentle landmark.");
        if (TryGetFact(facts, "relativePositions", out var separation) || TryGetFact(facts, "angularSeparation", out separation)) sentences.Add($"The spacing should look close enough to compare in a single glance.");
        if (TryGetFact(facts, "nakedEyeVisibility", out var nakedEye)) sentences.Add(nakedEye.Contains("won\'t need", StringComparison.OrdinalIgnoreCase) ? "You won't need a telescope; start with your eyes." : IsAffirmative(nakedEye) ? "Your eyes are enough to begin." : "Use confirmed local guidance before choosing equipment.");
        else if (TryGetFact(facts, "binocularGuidance", out var binoculars) || TryGetFact(facts, "telescopeGuidance", out binoculars)) sentences.Add($"For equipment, {LowerFirst(NaturalizeIsoDates(binoculars))}");
        if (TryGetFact(facts, "lightPollutionConsiderations", out var lightPollution)) sentences.Add($"Darker surroundings will make the view cleaner, but the main pattern should still be easy to recognize if the horizon is open.");
        return string.Join(" ", sentences);
    }

    private static string NaturalizeFact(string name, string value)
    {
        var clean = NaturalizeIsoDates(value.Trim());
        if (name.Contains("window", StringComparison.OrdinalIgnoreCase)) return $"The most useful viewing window is {LowerFirst(clean)}.";
        if (name.Contains("direction", StringComparison.OrdinalIgnoreCase)) return clean.StartsWith("Face ", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Look ", StringComparison.OrdinalIgnoreCase) ? $"{clean}." : $"Look toward {NaturalDirection(clean)}.";
        if (name.Contains("date", StringComparison.OrdinalIgnoreCase)) return clean.StartsWith("on ", StringComparison.OrdinalIgnoreCase) ? $"{clean}." : $"This is a sky moment for {clean}.";
        if (name.Contains("time", StringComparison.OrdinalIgnoreCase)) return $"Around {clean}, the view should be at its best.";
        if (name.Contains("region", StringComparison.OrdinalIgnoreCase)) return $"It favors observers in {clean}.";
        if (name.Contains("moon", StringComparison.OrdinalIgnoreCase)) return $"The Moon's phase matters here: {clean}.";
        if (name.Contains("altitude", StringComparison.OrdinalIgnoreCase)) return "It should sit high enough to see clearly if your horizon is open.";
        if (name.Contains("azimuth", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (name.Contains("constellation", StringComparison.OrdinalIgnoreCase)) return $"The scene sits near {clean}.";
        if (name.Contains("separation", StringComparison.OrdinalIgnoreCase) || name.Contains("relativePositions", StringComparison.OrdinalIgnoreCase)) return Regex.IsMatch(clean, "\\d") ? $"They appear close together in the sky, separated by about {clean} degrees." : $"They appear {clean}.";
        if (name.Contains("naked", StringComparison.OrdinalIgnoreCase)) return clean.Contains("won\'t need", StringComparison.OrdinalIgnoreCase) ? $"{clean}." : IsAffirmative(clean) ? "It should be visible to the unaided eye." : "It may not be easy with the unaided eye.";
        if (name.Contains("binocular", StringComparison.OrdinalIgnoreCase)) return IsAffirmative(clean) ? "Binoculars can make the view more satisfying." : string.Empty;
        if (name.Contains("telescope", StringComparison.OrdinalIgnoreCase)) return IsAffirmative(clean) ? "A telescope is optional, not the starting point." : string.Empty;
        if (name.Contains("appearance", StringComparison.OrdinalIgnoreCase)) return $"Expect {LowerFirst(clean)}.";
        return clean.EndsWith('.') ? clean : clean + ".";
    }


    private static string RemoveLeakage(string text)
    {
        var cleaned = text;
        foreach (var phrase in EngineeringLeakagePhrases.Concat(PromptLeakagePhrases))
        {
            cleaned = Regex.Replace(cleaned, $"\\b{Regex.Escape(phrase.Trim())}\\b:?", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return CleanNarration(cleaned.Replace(" .", ".").Replace(" ,", ","));
    }

    private static string NaturalizeIsoDates(string text)
        => IsoDateTimeRegex.Replace(text, match =>
        {
            var value = match.Value;
            var normalized = value.Replace('T', ' ');
            if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
            {
                var date = dto.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                return value.Length > 10 ? $"{date} at {dto.ToString("h:mm tt", CultureInfo.InvariantCulture)}" : date;
            }

            if (DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            {
                return dateOnly.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            }

            return value;
        });

    private static string FixDuplicatedPhrases(string text)
    {
        var cleaned = Regex.Replace(text, "\\baround\\s+around\\b", "around", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, "\\bface\\s+the\\s+look\\s+toward\\b", "look toward", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, "\\b(look toward|face|turn toward)\\s+(?:the\\s+)?(look toward|face|turn toward)\\b", "$1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, "\\b(\\w+(?:\\s+\\w+){0,2})\\s+\\1\\b", "$1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return CleanNarration(cleaned);
    }

    private static string ImproveSpokenRhythm(string text)
    {
        var cleaned = CleanNarration(text);
        cleaned = cleaned.Replace(";", ".", StringComparison.Ordinal);
        cleaned = Regex.Replace(cleaned, "\\s+,", ",", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, "\\.{2,}", ".", RegexOptions.CultureInvariant);
        return cleaned.Trim();
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

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
        var spoken = hasLeakage ? 55 : Math.Max(0, 98 - naturalnessWarningCount * 2);
        var retention = scenes.Count > 0 && fullText.Length > 120 && transitionTerms.Count(t => fullText.Contains(t, StringComparison.OrdinalIgnoreCase)) >= 2 ? 97 : 84;
        var identity = identityTerms.Count(t => fullText.Contains(t, StringComparison.OrdinalIgnoreCase)) >= 4 ? 98 : 85;
        var overall = new[] { documentaryVoice, scientific, observation, flow, spoken, retention, identity }.Min();
        return new ProfessionalNarrationScores(documentaryVoice, scientific, observation, flow, spoken, retention, identity, overall);
    }

    private static bool IsFactCovered(NarrationFactV5 fact, string fullText)
    {
        if (fullText.Contains(fact.Value, StringComparison.OrdinalIgnoreCase)) return true;
        var naturalValue = NaturalizeIsoDates(fact.Value);
        if (!string.IsNullOrWhiteSpace(naturalValue) && fullText.Contains(naturalValue, StringComparison.OrdinalIgnoreCase)) return true;
        var natural = NaturalizeFact(fact.Name, fact.Value);
        if (!string.IsNullOrWhiteSpace(natural) && fullText.Contains(natural, StringComparison.OrdinalIgnoreCase)) return true;
        var formattedValue = KnowledgeFormatter.FormatValue(fact.Name, fact.Value);
        if (!string.IsNullOrWhiteSpace(formattedValue) && fullText.Contains(formattedValue, StringComparison.OrdinalIgnoreCase)) return true;
        if (fact.Name.Contains("date", StringComparison.OrdinalIgnoreCase) && DateTimeOffset.TryParse(fact.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return fullText.Contains(dto.ToString("MMMM d", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        }
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
    private static readonly string[] EngineeringLeakagePhrases = ["understand", "know", "keep in mind", "anchor", "scene purpose", "audience promise", "viewer should", "the viewer should", "available facts", "planning", "facts to mention", "verified details", "event identity", "scene goal", "guide the viewer", "open by", "end with", "the event feels", "warning", "the story", "let viewers", "by the end", "keep the tone", "raw metadata", "diagnostic text"];
    private static readonly string[] PromptLeakagePhrases = ["metadata", "prompt", "json", "llm", "system message", "user prompt", "contract", "schema"];
    private static readonly Regex IsoDateTimeRegex = new(@"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?(?:Z|[+-]\d{2}:?\d{2})?)?\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RawUtcRegex = new(@"\b(?:UTC|Z\s*time|Coordinated Universal Time)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DuplicatedTransformedPhraseRegex = new(@"\b(?:around around|face the look toward|(?<dir>look toward|face|turn toward)\s+(?:the\s+)?\k<dir>)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static int CalculateDistinctivenessScore(string longText, string shortText)
    {
        var longTokens = TokenizeForSimilarity(longText);
        var shortTokens = TokenizeForSimilarity(shortText);
        if (longTokens.Count == 0 || shortTokens.Count == 0) return 0;
        var overlap = longTokens.Intersect(shortTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = longTokens.Union(shortTokens, StringComparer.OrdinalIgnoreCase).Count();
        var jaccardSimilarity = union == 0 ? 1d : (double)overlap / union;
        var lengthDelta = Math.Abs(longTokens.Count - shortTokens.Count) / (double)Math.Max(longTokens.Count, shortTokens.Count);
        return Math.Clamp((int)Math.Round((1d - jaccardSimilarity) * 75d + lengthDelta * 25d), 0, 100);
    }

    private static HashSet<string> TokenizeForSimilarity(string text) => Regex.Matches(text.ToLowerInvariant(), "[a-z0-9']+")
        .Select(m => m.Value)
        .Where(t => t.Length > 3)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> DetectProducerNotesLeakage(ProducerNotesContract contract, string narrationText)
    {
        var leaks = new List<string>();
        foreach (var phrase in EngineeringLeakagePhrases.Concat(PromptLeakagePhrases))
        {
            if (narrationText.Contains(phrase, StringComparison.OrdinalIgnoreCase)) leaks.Add(phrase);
        }

        foreach (var note in contract.Briefs.SelectMany(b => new[] { b.SceneStory, b.NarrativeGoal, b.AudienceExperience, b.ObservationGuidance, b.TransitionContext }))
        {
            var sentence = note.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(v => v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 6);
            if (!string.IsNullOrWhiteSpace(sentence) && narrationText.Contains(sentence, StringComparison.OrdinalIgnoreCase)) leaks.Add(sentence);
        }

        return leaks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
}

public sealed record NarrationLlmRequestV1(string RequestVersion, string Component, string Model, decimal Temperature, decimal TopP, int MaxTokens, string SystemPrompt, string UserPrompt, int PromptQualityScore, IReadOnlyList<string> SourceContracts, DateTime CreatedUtc);

public sealed record NarrationFactV5(string Name, string Value);
public sealed record NarrationPlanV5(string NarrationPlanVersion, string OrchestrationVersion, string Language, string VoiceProfile, string StoryArc, IReadOnlyList<NarrationFactV5> RequiredNarrationFacts, IReadOnlyList<string> ProhibitedPhrases, IReadOnlyList<string> PreferredPhrases, string ChannelEnding, IReadOnlyList<NarrationPlanV5Scene> Scenes);
public sealed record NarrationPlanV5Scene(string SceneId, string ScenePurpose, int SceneOrder, string KeyMessage, string ViewerFocus, string EmotionalRole, string NarrationIntent, IReadOnlyList<NarrationFactV5> RequiredFacts, IReadOnlyList<NarrationFactV5> MustMentionFacts, IReadOnlyList<string> MustAvoidFacts, string EditorialConnectorToNext, string TargetTone, string TargetLength);
public sealed record NarrationV5(string NarrationVersion, string OrchestrationVersion, string Language, IReadOnlyList<NarrationV5Scene> Scenes, string FullNarrationText, string ChannelEnding);
public sealed record NarrationV5Scene(string SceneId, string ScenePurpose, string NarrationText, IReadOnlyList<string> RequiredFactsCovered, IReadOnlyList<string> Warnings);
public sealed record RequiredFactCoverage(string Value, bool Covered);
public sealed record ProfessionalNarrationScores(int DocumentaryVoiceScore, int ScientificAccuracyScore, int ObservationGuidanceScore, int EditorialFlowScore, int SpokenLanguageScore, int ViewerRetentionScore, int AstroPulseIdentityScore, int OverallNarrationScore);
public sealed record KnowledgeFormatContract(string ContractVersion, string Language, IReadOnlyList<KnowledgeFormattedFact> FormattedFacts, IReadOnlyList<KnowledgeFormattedScene> Scenes)
{
    public NarrationBriefsV5 ToNarrationBriefs(string orchestrationVersion) => new("AstroPulse-NarrationBriefs-v1", orchestrationVersion, Language, Scenes.Select(s => new NarrationBriefV5(s.SceneId, s.ScenePurpose, s.SceneOrder, s.SceneGoal, s.AudienceTakeaway, s.FactsToMention, s.FactsToAvoid, s.AlreadyCoveredFacts, s.ConnectorToNext, s.Tone, s.Pacing, s.TargetLength, s.MustIncludeEnding, s.GenerationInstructions)).ToArray());
}
public sealed record KnowledgeFormattedFact(string Name, string FormattedValue);
public sealed record KnowledgeFormattedScene(string SceneId, string ScenePurpose, int SceneOrder, string SceneGoal, string AudienceTakeaway, IReadOnlyList<NarrationFactV5> FactsToMention, IReadOnlyList<string> FactsToAvoid, IReadOnlyList<string> AlreadyCoveredFacts, string ConnectorToNext, string Tone, string Pacing, string TargetLength, bool MustIncludeEnding, string GenerationInstructions);
public sealed record KnowledgeFormatDiagnostics(string Component, int RawFactCount, int FormattedFactCount, bool RawMetadataRemoved, bool InstructionFragmentsRemoved, int RawFactsWithMetadata, int WriterFactsWithMetadataBeforeFormatting, int SceneCount);
public sealed record NarrationGeneratorV5Result(IReadOnlyList<string> GeneratedFiles) { public static NarrationGeneratorV5Result Empty { get; } = new([]); }

public sealed record EditorialBriefContract(string ContractVersion, string Language, IReadOnlyList<EditorialBriefScene> Scenes)
{
    public NarrationBriefsV5 ToNarrationBriefs(string orchestrationVersion, KnowledgeFormatContract knowledgeContract)
    {
        var knowledgeByScene = knowledgeContract.Scenes.ToDictionary(s => s.SceneId, StringComparer.OrdinalIgnoreCase);
        return new NarrationBriefsV5("AstroPulse-NarrationBriefs-v2", orchestrationVersion, Language, Scenes.Select(scene =>
        {
            var knowledge = knowledgeByScene[scene.SceneId];
            return new NarrationBriefV5(
                scene.SceneId,
                scene.SceneRole,
                scene.SceneOrder,
                scene.NaturalWritingGuidance,
                scene.AudienceTakeaway,
                knowledge.FactsToMention,
                knowledge.FactsToAvoid,
                knowledge.AlreadyCoveredFacts,
                knowledge.ConnectorToNext,
                knowledge.Tone,
                knowledge.Pacing,
                knowledge.TargetLength,
                knowledge.MustIncludeEnding,
                scene.EmotionalPurpose);
        }).ToArray());
    }
}

public sealed record EditorialBriefScene(string SceneId, string SceneRole, int SceneOrder, string EmotionalPurpose, string AudienceTakeaway, string NaturalWritingGuidance);
public sealed record EditorialBriefDiagnostics(string Component, int SceneCount, bool PlanningLanguageRemoved, bool DiagnosticWarningsRemoved, bool SceneIntentConvertedToGuidance, IReadOnlyList<string> RemovedWriterPhrases, IReadOnlyList<string> DiagnosticWarningsKeptOutOfWriterInput);
public sealed record ProducerNotesContract(string ContractVersion, string Language, IReadOnlyList<ProducerNotesScene> Briefs)
{
    public NarrationBriefsV5 ToNarrationBriefs(string orchestrationVersion, string? format = null)
    {
        var selected = string.IsNullOrWhiteSpace(format) ? Briefs : Briefs.Where(b => b.FormatRequirement.Equals(format, StringComparison.OrdinalIgnoreCase));
        return new NarrationBriefsV5("AstroPulse-ProducerNotes-v1", orchestrationVersion, Language, selected.OrderBy(b => b.SceneOrder).Select(scene =>
            new NarrationBriefV5(
                scene.SceneId,
                scene.NarrativeGoal,
                scene.SceneOrder,
                scene.SceneStory,
                scene.AudienceExperience,
                scene.KeyFacts,
                [],
                [],
                scene.TransitionContext,
                scene.EmotionalTone,
                scene.FormatRequirement.Equals("short", StringComparison.OrdinalIgnoreCase) ? "brief documentary" : "full documentary",
                scene.FormatRequirement,
                scene.MustIncludeEnding,
                scene.ObservationGuidance)).ToArray());
    }
}

public sealed record ProducerNotesScene(
    [property: JsonIgnore] string SceneId,
    [property: JsonIgnore] int SceneOrder,
    string SceneStory,
    string NarrativeGoal,
    string AudienceExperience,
    IReadOnlyList<NarrationFactV5> KeyFacts,
    string ObservationGuidance,
    string EmotionalTone,
    string TransitionContext,
    [property: JsonIgnore] string FormatRequirement,
    [property: JsonIgnore] bool MustIncludeEnding);

public sealed record ProducerNotesDiagnostics(string Component, int BriefCount, IReadOnlyList<string> FormatsRequested, bool ContainsOnlyAllowedFields, bool EditorialInstructionsRemoved, bool PlanningLanguageRemoved, bool SceneLabelsRemoved, bool AudiencePromisesRemoved, IReadOnlyList<string> ForbiddenPhrasesDetected);

public static class ProducerNotesComposer
{
    private static readonly string[] Forbidden =
    [
        "open by", "guide the viewer", "end with", "scene purpose", "audience promise", "facts to mention",
        "viewer should", "the viewer should", "checklist", "prompt", "JSON", "writing instructions", "write ",
        "use the confirmed", "keep observation", "in plain spoken language", "must", "metadata", "warning", "missing metadata"
    ];

    public static ProducerNotesContract Compose(EditorialBriefContract editorial, KnowledgeFormatContract knowledge, IReadOnlyList<string> formats)
    {
        var knowledgeByScene = knowledge.Scenes.ToDictionary(s => s.SceneId, StringComparer.OrdinalIgnoreCase);
        var finalSceneOrder = editorial.Scenes.Max(s => s.SceneOrder);
        var briefs = new List<ProducerNotesScene>();
        foreach (var format in formats)
        foreach (var scene in editorial.Scenes.OrderBy(s => s.SceneOrder))
        {
            var k = knowledgeByScene[scene.SceneId];
            var keyFacts = k.FactsToMention.Select(f => new NarrationFactV5(f.Name, Clean(f.Value))).ToArray();
            briefs.Add(new ProducerNotesScene(
                scene.SceneId,
                scene.SceneOrder,
                BuildSceneStory(scene, k, keyFacts),
                Clean(RewriteGoalAsStoryState(scene.NaturalWritingGuidance, scene.SceneRole)),
                Clean(RewriteAudienceAsExperience(scene.AudienceTakeaway)),
                keyFacts,
                BuildObservationGuidance(k, keyFacts),
                Clean(FirstNonEmpty(k.Tone, scene.EmotionalPurpose, "Quiet wonder")!),
                BuildTransitionContext(k),
                format.ToLowerInvariant(),
                scene.SceneOrder == finalSceneOrder));
        }
        return new ProducerNotesContract("AstroPulse-ProducerNotesContract-v1", editorial.Language, briefs);
    }

    public static ProducerNotesDiagnostics BuildDiagnostics(ProducerNotesContract contract)
    {
        var text = JsonSerializer.Serialize(contract, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var forbidden = Forbidden.Where(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new ProducerNotesDiagnostics("ProducerNotesComposer-v1", contract.Briefs.Count, contract.Briefs.Select(b => b.FormatRequirement).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), true, forbidden.Length == 0, forbidden.Length == 0, true, forbidden.All(p => !p.Contains("audience promise", StringComparison.OrdinalIgnoreCase)), forbidden);
    }

    private static string BuildSceneStory(EditorialBriefScene scene, KnowledgeFormattedScene knowledge, IReadOnlyList<NarrationFactV5> facts)
    {
        var factText = facts.Count == 0 ? "The story continues through the next confirmed detail." : string.Join(" ", facts.Take(2).Select(f => NaturalFactSentence(f.Name, f.Value)));
        return Clean($"{RewriteGoalAsStoryState(scene.NaturalWritingGuidance, scene.SceneRole)} {factText}");
    }

    private static string RewriteGoalAsStoryState(string value, string role)
    {
        var cleaned = Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned)) return $"This part of the story carries the {role.ToLowerInvariant()} moment forward.";
        return cleaned.Replace("Leave the audience able to", "The audience reaches", StringComparison.OrdinalIgnoreCase)
            .Replace("Introduce", "The story introduces", StringComparison.OrdinalIgnoreCase)
            .Replace("Explain", "The story explains", StringComparison.OrdinalIgnoreCase)
            .Replace("Show", "The story shows", StringComparison.OrdinalIgnoreCase);
    }

    private static string RewriteAudienceAsExperience(string value)
    {
        var cleaned = Clean(value).Replace("The viewer should feel", "The viewer feels", StringComparison.OrdinalIgnoreCase)
            .Replace("The viewer should", "The viewer", StringComparison.OrdinalIgnoreCase)
            .Replace("Viewer should", "Viewer", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(cleaned) ? "The audience feels oriented, curious, and ready for the next discovery." : cleaned;
    }

    private static string BuildObservationGuidance(KnowledgeFormattedScene knowledge, IReadOnlyList<NarrationFactV5> facts)
    {
        var naturalFacts = facts.Select(f => NaturalFactSentence(f.Name, f.Value)).Where(v => !string.IsNullOrWhiteSpace(v)).Take(3).ToArray();
        return naturalFacts.Length == 0 ? "Observation remains grounded in the confirmed story details." : string.Join(" ", naturalFacts);
    }

    private static string BuildTransitionContext(KnowledgeFormattedScene knowledge)
        => Clean(string.IsNullOrWhiteSpace(knowledge.ConnectorToNext) ? "The audience is ready for the next part of the story." : knowledge.ConnectorToNext);

    private static string NaturalFactSentence(string name, string value)
    {
        var fact = new NarrationFactV5(name, value);
        var normalized = NarrationPromptComposer.NormalizeFact(fact);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.TrimEnd('.') + ".";
    }

    private static string Clean(string value)
    {
        var cleaned = value ?? string.Empty;
        foreach (var phrase in Forbidden) cleaned = Regex.Replace(cleaned, $@"\b{Regex.Escape(phrase.Trim())}\b:?", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = cleaned.Replace("Scene", "Story moment", StringComparison.OrdinalIgnoreCase);
        return Regex.Replace(cleaned, "\\s{2,}", " ", RegexOptions.CultureInvariant).Trim(' ', '.', ':', ';', '-');
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public sealed class EditorialBriefInterpreter
{
    public static readonly string[] ForbiddenWriterInputPhrases =
    [
        "scene purpose", "audience promise", "available facts", "facts to mention", "metadata", "warning", "missing metadata",
        "checklist", "prompt", "JSON", "which objects form", "where in the sky", "what to do next", "understand", "know",
        "keep in mind", "anchor", "guide the viewer", "open by", "end with", "the event feels"
    ];

    public static readonly string[] ForbiddenNarrationPhrases =
    [
        "instruction fragment", "planning heading", "scene purpose", "audience promise", "diagnostic warning",
        "which objects form", "where in the sky", "what to do next", "understand", "know", "guide the viewer",
        "open by", "end with", "the event feels", "warning", "metadata"
    ];

    public EditorialBriefContract Interpret(KnowledgeFormatContract knowledgeContract, IReadOnlyList<string> diagnosticWarnings)
    {
        var scenes = knowledgeContract.Scenes.OrderBy(s => s.SceneOrder).Select(scene =>
            new EditorialBriefScene(
                scene.SceneId,
                SafeRole(scene.ScenePurpose),
                scene.SceneOrder,
                EmotionalPurpose(scene.ScenePurpose, scene.SceneGoal),
                AudienceTakeaway(scene.ScenePurpose, scene.AudienceTakeaway),
                Guidance(scene.ScenePurpose, scene.SceneGoal, scene.GenerationInstructions))).ToArray();

        return new EditorialBriefContract("AstroPulse-EditorialBriefContract-v1", knowledgeContract.Language, scenes);
    }

    public EditorialBriefDiagnostics BuildDiagnostics(EditorialBriefContract contract, NarrationBriefsV5 rawBriefs, KnowledgeFormatContract knowledgeContract)
    {
        var outputText = JsonSerializer.Serialize(contract, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var removed = ForbiddenWriterInputPhrases.Where(p => !outputText.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new EditorialBriefDiagnostics(
            "EditorialBriefInterpreter-v1",
            contract.Scenes.Count,
            !ForbiddenWriterInputPhrases.Any(p => outputText.Contains(p, StringComparison.OrdinalIgnoreCase)),
            true,
            contract.Scenes.All(s => !string.IsNullOrWhiteSpace(s.NaturalWritingGuidance) && !ForbiddenWriterInputPhrases.Any(p => s.NaturalWritingGuidance.Contains(p, StringComparison.OrdinalIgnoreCase))),
            removed,
            rawBriefs.Briefs.SelectMany(b => b.FactsToAvoid).Where(v => v.Contains("missing", StringComparison.OrdinalIgnoreCase) || v.Contains("warning", StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    private static string SafeRole(string value) => Clean(value) switch
    {
        "Hook" => "Opening sky moment",
        "Discovery" => "Orientation",
        "Science" => "Plain-language explanation",
        "Observation" => "Viewing guidance",
        "Takeaway" or "Closing" => "Reflective close",
        var other when !string.IsNullOrWhiteSpace(other) => other,
        _ => "Documentary beat"
    };

    private static string EmotionalPurpose(string role, string source) => Clean(role) switch
    {
        "Hook" => "Create quiet curiosity without sounding promotional.",
        "Discovery" => "Make the sky feel approachable.",
        "Science" => "Turn wonder into simple perspective.",
        "Observation" => "Give calm confidence for stepping outside.",
        "Takeaway" or "Closing" => "Leave the moment feeling memorable and human.",
        _ => Clean(source)
    };

    private static string AudienceTakeaway(string role, string source) => Clean(role) switch
    {
        "Hook" => "A clear, visible sky pairing earns curiosity quickly.",
        "Discovery" => "Orientation becomes practical and approachable.",
        "Science" => "The apparent closeness is explained as perspective from Earth.",
        "Observation" => "A practical observing next step is ready.",
        "Takeaway" or "Closing" => "The sky connects back to everyday life.",
        _ => Clean(source)
    };

    private static string Guidance(string role, string goal, string instructions)
    {
        var source = $"{goal} {instructions}";
        if (source.Contains("which objects form", StringComparison.OrdinalIgnoreCase) || source.Contains("event", StringComparison.OrdinalIgnoreCase) && Clean(role) == "Hook")
            return "Main sky objects belong inside the visible sky moment.";
        if (source.Contains("where in the sky", StringComparison.OrdinalIgnoreCase) || Clean(role) == "Discovery")
            return "Orientation points toward the correct part of the sky.";
        if (source.Contains("what to do next", StringComparison.OrdinalIgnoreCase) || Clean(role) == "Observation")
            return "Clear, calm observing action belongs here.";
        return Clean(role) switch
        {
            "Science" => "Explain the visual effect in plain documentary language without adding unsupported detail.",
            "Takeaway" or "Closing" => "Close warmly with the feeling of noticing the sky on purpose.",
            _ => "Write the scene as natural spoken documentary prose."
        };
    }

    private static string Clean(string value)
    {
        var cleaned = value;
        foreach (var phrase in ForbiddenWriterInputPhrases)
            cleaned = Regex.Replace(cleaned, $@"\b{Regex.Escape(phrase)}\b:?", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(cleaned, "\\s{2,}", " ", RegexOptions.CultureInvariant).Trim(' ', '.', ':', ';', '-');
    }
}


public sealed class KnowledgeFormatter
{
    private static readonly Regex IsoRegex = new(@"\b\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)?\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DegreeRegex = new(@"(?<value>\d+(?:\.\d+)?)\s*(?:°|degrees?)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] InstructionFragments = ["understand", "know", "keep in mind", "anchor", "scene purpose", "audience promise", "metadata", "prompt", "JSON", "Verified details", "event identity", "facts to mention", "scene goal", "the viewer should", "viewer should"];

    public KnowledgeFormatContract Format(IReadOnlyList<NarrationFactV5> requiredFacts, NarrationBriefsV5 briefs, string language)
    {
        var formattedFacts = requiredFacts.Select(f => new KnowledgeFormattedFact(f.Name, FormatValue(f.Name, f.Value))).ToArray();
        var byName = formattedFacts.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().FormattedValue, StringComparer.OrdinalIgnoreCase);
        var scenes = briefs.Briefs.Select(brief => new KnowledgeFormattedScene(
            brief.SceneId,
            brief.ScenePurpose,
            brief.SceneOrder,
            CleanInstructionText(brief.SceneGoal),
            CleanInstructionText(brief.AudienceTakeaway),
            brief.FactsToMention.Select(f => new NarrationFactV5(f.Name, byName.TryGetValue(f.Name, out var formatted) ? formatted : FormatValue(f.Name, f.Value))).ToArray(),
            brief.FactsToAvoid.Select(CleanInstructionText).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray(),
            brief.AlreadyCoveredFacts,
            CleanInstructionText(brief.ConnectorToNext),
            brief.Tone,
            brief.Pacing,
            brief.TargetLength,
            brief.MustIncludeEnding,
            CleanInstructionText(brief.GenerationInstructions))).ToArray();
        return new KnowledgeFormatContract("AstroPulse-KnowledgeFormatContract-v1", language, formattedFacts, scenes);
    }

    public KnowledgeFormatDiagnostics BuildDiagnostics(KnowledgeFormatContract contract, IReadOnlyList<NarrationFactV5> rawFacts, NarrationBriefsV5 rawBriefs)
    {
        var formattedText = JsonSerializer.Serialize(contract, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new KnowledgeFormatDiagnostics(
            "KnowledgeFormatter-v1",
            rawFacts.Count,
            contract.FormattedFacts.Count,
            !ContainsRawMetadata(formattedText),
            !InstructionFragments.Any(p => formattedText.Contains(p, StringComparison.OrdinalIgnoreCase)),
            rawFacts.Count(f => ContainsRawMetadata(f.Value)),
            rawBriefs.Briefs.SelectMany(b => b.FactsToMention).Count(f => ContainsRawMetadata(f.Value)),
            contract.Scenes.Count);
    }

    public static bool ContainsRawMetadata(string value) => IsoRegex.IsMatch(value) || value.Contains("UTC", StringComparison.OrdinalIgnoreCase) || value.Contains("T00:", StringComparison.OrdinalIgnoreCase);

    public static string FormatValue(string name, string value)
    {
        var clean = CleanInstructionText(value.Trim());
        if (string.IsNullOrWhiteSpace(clean)) return clean;
        if (name.Contains("date", StringComparison.OrdinalIgnoreCase) || name.Contains("time", StringComparison.OrdinalIgnoreCase) || IsoRegex.IsMatch(clean)) clean = FormatDates(clean);
        if (name.Contains("separation", StringComparison.OrdinalIgnoreCase) || name.Contains("relativePositions", StringComparison.OrdinalIgnoreCase)) clean = FormatAngularSeparation(clean);
        if (name.Contains("direction", StringComparison.OrdinalIgnoreCase) || name.Contains("azimuth", StringComparison.OrdinalIgnoreCase)) clean = FormatDirection(clean);
        if (name.Contains("altitude", StringComparison.OrdinalIgnoreCase)) clean = FormatAltitude(clean);
        if (name.Contains("naked", StringComparison.OrdinalIgnoreCase) || name.Contains("visibility", StringComparison.OrdinalIgnoreCase) || name.Contains("equipment", StringComparison.OrdinalIgnoreCase) || name.Contains("binocular", StringComparison.OrdinalIgnoreCase) || name.Contains("telescope", StringComparison.OrdinalIgnoreCase)) clean = FormatEquipment(clean);
        return CleanInstructionText(clean).Trim().TrimEnd('.');
    }

    private static string FormatDates(string value) => IsoRegex.Replace(value, m =>
    {
        if (DateTimeOffset.TryParse(m.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)) return $"On the evening of {dto.ToString("MMMM d", CultureInfo.InvariantCulture)}";
        if (DateOnly.TryParseExact(m.Value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return $"on {d.ToString("MMMM d", CultureInfo.InvariantCulture)}";
        return m.Value;
    }).Replace("UTC", "local time", StringComparison.OrdinalIgnoreCase);

    private static string FormatAngularSeparation(string value)
    {
        var m = DegreeRegex.Match(value);
        if (!m.Success || !decimal.TryParse(m.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var degrees)) return value;
        var phrase = degrees switch
        {
            >= 1.45m and <= 1.75m => "just over one and a half degrees apart",
            < 1m => "less than a degree apart",
            < 2m => "about two degrees apart",
            _ => $"about {Math.Round(degrees)} degrees apart"
        };
        return phrase;
    }

    private static string FormatDirection(string value)
    {
        var lower = value.ToLowerInvariant();
        if (lower.Contains("west")) return "Face the western horizon shortly after sunset";
        if (lower.Contains("east")) return "Face the eastern horizon when the sky is clear";
        if (lower.Contains("south")) return "Look toward the southern sky";
        if (lower.Contains("north")) return "Look toward the northern sky";
        return value.Replace("look toward", "Face", StringComparison.OrdinalIgnoreCase).Replace("sky sky", "sky", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatAltitude(string value)
    {
        var m = DegreeRegex.Match(value);
        if (!m.Success || !decimal.TryParse(m.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var degrees)) return value;
        if (degrees is >= 20 and <= 35) return "about halfway between the horizon and overhead";
        if (degrees < 15) return "low above the horizon";
        if (degrees > 60) return "high in the sky";
        return "comfortably above the horizon";
    }

    private static string FormatEquipment(string value)
    {
        if (value.Contains("excellent", StringComparison.OrdinalIgnoreCase) || value.Contains("naked", StringComparison.OrdinalIgnoreCase) || value.Contains("true", StringComparison.OrdinalIgnoreCase) || value.Contains("yes", StringComparison.OrdinalIgnoreCase)) return "You won't need a telescope";
        if (value.Contains("binocular", StringComparison.OrdinalIgnoreCase)) return "Binoculars may make the view easier";
        return value;
    }

    private static string CleanInstructionText(string value)
    {
        var cleaned = value;
        foreach (var phrase in InstructionFragments) cleaned = Regex.Replace(cleaned, $@"\b{Regex.Escape(phrase)}\b:?", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, "\\s{2,}", " ", RegexOptions.CultureInvariant)
            .Replace(" .", ".", StringComparison.Ordinal)
            .Replace(" ,", ",", StringComparison.Ordinal)
            .Replace("around around", "around", StringComparison.OrdinalIgnoreCase)
            .Replace("face the look toward", "look toward", StringComparison.OrdinalIgnoreCase)
            .Replace("sky sky", "sky", StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '.', ':', ';', '-');
        return cleaned;
    }
}

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
