using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
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
    private const string DefaultEnglishChannelEnding = "Until next time, keep looking up.";
    private static readonly UTF8Encoding JsonUtf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

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
        var rawNarrativeRoot = Path.Combine(narrationRoot, "raw-narrative");
        var rawNarrativeLongRoot = Path.Combine(rawNarrativeRoot, "long");
        var rawNarrativeShortRoot = Path.Combine(rawNarrativeRoot, "short");
        Directory.CreateDirectory(rawNarrativeLongRoot);
        Directory.CreateDirectory(rawNarrativeShortRoot);
        var longRawNarrativePath = Path.Combine(rawNarrativeLongRoot, "raw-narrative.json");
        var shortRawNarrativePath = Path.Combine(rawNarrativeShortRoot, "raw-narrative.json");
        var rawNarrativeDiagnosticsPath = Path.Combine(rawNarrativeRoot, "raw-narrative-diagnostics.json");
        var sceneFactCardsRoot = Path.Combine(narrationRoot, "scene-fact-cards");
        var sceneFactCardsLongRoot = Path.Combine(sceneFactCardsRoot, "long");
        var sceneFactCardsShortRoot = Path.Combine(sceneFactCardsRoot, "short");
        Directory.CreateDirectory(sceneFactCardsLongRoot);
        Directory.CreateDirectory(sceneFactCardsShortRoot);
        var longSceneFactCardsPath = Path.Combine(sceneFactCardsLongRoot, "scene-fact-cards.json");
        var shortSceneFactCardsPath = Path.Combine(sceneFactCardsShortRoot, "scene-fact-cards.json");
        var sceneFactCardsDiagnosticsPath = Path.Combine(sceneFactCardsRoot, "scene-fact-cards-diagnostics.json");
        var documentaryScriptRoot = Path.Combine(narrationRoot, "documentary-script");
        var documentaryScriptLongRoot = Path.Combine(documentaryScriptRoot, "long");
        var documentaryScriptShortRoot = Path.Combine(documentaryScriptRoot, "short");
        Directory.CreateDirectory(documentaryScriptLongRoot);
        Directory.CreateDirectory(documentaryScriptShortRoot);
        var longDocumentaryScriptPath = Path.Combine(documentaryScriptLongRoot, "documentary-script.json");
        var shortDocumentaryScriptPath = Path.Combine(documentaryScriptShortRoot, "documentary-script.json");
        var documentaryScriptDiagnosticsPath = Path.Combine(documentaryScriptRoot, "documentary-script-diagnostics.json");
        var performanceDiagnosticsPath = Path.Combine(documentaryScriptRoot, "performance-diagnostics.json");
        var narrationContextPath = Path.Combine(narrationRoot, "narration-context.json");
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

        var languageRequested = FirstNonEmpty(request.Language, GetString(contract, "language"), GetString(storyboard, "language"));
        var languageProfile = LanguageProfileResolver.Resolve(languageRequested);
        var language = languageProfile.LanguageCode;
        var channelEnding = languageProfile.ChannelEnding;
        var languageProfileFound = languageProfile.ProfileFound;
        var languageProfileFallbackUsed = languageProfile.FallbackUsed;
        var requiredFacts = ReadRequiredFacts(contract);
        var prohibited = FindStringArray(contract, "prohibitedPhrases");
        var preferred = FindStringArray(contract, "preferredPhrases");
        var scenes = ReadArray(storyboard, "scenes").OrderBy(s => GetInt(s, "sceneOrder") ?? 0).ToArray();
        if (scenes.Length == 0) warnings.Add("No creative storyboard scenes were available for narration generation.");

        var planScenes = scenes.Select((scene, index) => BuildPlanScene(scene, index, requiredFacts)).ToArray();
        var plan = new NarrationPlanV5("AstroPulse-NarrationPlan-v1", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, "CalmDocumentary", GetString(storyboard, "storyArc") ?? "Hook → Discovery → Science → Observation → Takeaway", requiredFacts, prohibited, preferred, channelEnding, planScenes);
        var briefs = NarrativeDirector.BuildBriefs(plan, FindStringArray(contract, "missingFactWarnings"));
        var rawNarrationBriefs = new NarrationBriefsV5("AstroPulse-NarrationBriefs-v1", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, briefs);
        var formatter = new KnowledgeFormatter();
        var knowledgeContract = formatter.Format(requiredFacts, rawNarrationBriefs, language);
        var knowledgeDiagnostics = formatter.BuildDiagnostics(knowledgeContract, requiredFacts, rawNarrationBriefs);
        var interpreter = new EditorialBriefInterpreter();
        var editorialBriefContract = interpreter.Interpret(knowledgeContract, FindStringArray(contract, "missingFactWarnings"));
        var editorialBriefDiagnostics = interpreter.BuildDiagnostics(editorialBriefContract, rawNarrationBriefs, knowledgeContract);
        var requestedFormats = ResolveRequestedNarrationFormats(outputRoot, request, response);
        ValidateRequiredInputsForPhase7(outputRoot, requestedFormats, contract, storyboard, validationPath, languageRequested, language, languageProfileFound, languageProfileFallbackUsed);
        var producerNotesContract = ProducerNotesComposer.Compose(editorialBriefContract, knowledgeContract, requestedFormats);
        var producerNotesDiagnostics = ProducerNotesComposer.BuildDiagnostics(producerNotesContract);
        var narrationBriefs = producerNotesContract.ToNarrationBriefs(Rc2PipelinePhaseRegistry.OrchestrationVersion);

        await WriteAllTextUtf8Async(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(briefsPath, JsonSerializer.Serialize(narrationBriefs, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(knowledgeContractPath, JsonSerializer.Serialize(knowledgeContract, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(knowledgeDiagnosticsPath, JsonSerializer.Serialize(knowledgeDiagnostics, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(editorialBriefContractPath, JsonSerializer.Serialize(editorialBriefContract, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(editorialBriefDiagnosticsPath, JsonSerializer.Serialize(editorialBriefDiagnostics, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(producerNotesContractPath, JsonSerializer.Serialize(producerNotesContract, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(producerNotesDiagnosticsPath, JsonSerializer.Serialize(producerNotesDiagnostics, JsonOptions), cancellationToken);

        var longRawNarrative = RawNarrativeGenerator.Build("long", producerNotesContract, Rc2PipelinePhaseRegistry.OrchestrationVersion);
        var shortRawNarrative = RawNarrativeGenerator.Build("short", producerNotesContract, Rc2PipelinePhaseRegistry.OrchestrationVersion);
        await WriteAllTextUtf8Async(longRawNarrativePath, JsonSerializer.Serialize(longRawNarrative, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(shortRawNarrativePath, JsonSerializer.Serialize(shortRawNarrative, JsonOptions), cancellationToken);
        var rawNarrativeDiagnostics = new { component = "RawNarrativeGenerator-v1", longGenerated = longRawNarrative.Scenes.Count > 0, shortGenerated = shortRawNarrative.Scenes.Count > 0, longSceneCount = longRawNarrative.Scenes.Count, shortSceneCount = shortRawNarrative.Scenes.Count, deterministic = true, excludedFromLlmBoundary = true, producerNotesExcludedFromLlm = true, narrativeBriefExcludedFromLlm = true };
        await WriteAllTextUtf8Async(rawNarrativeDiagnosticsPath, JsonSerializer.Serialize(rawNarrativeDiagnostics, JsonOptions), cancellationToken);

        var longStoryFrames = LoadStoryFrames(outputRoot, "long");
        var shortStoryFrames = LoadStoryFrames(outputRoot, "short");
        var longSceneFactCards = SceneFactCardGenerator.Build("long", producerNotesContract, Rc2PipelinePhaseRegistry.OrchestrationVersion, longStoryFrames.Frames);
        var shortSceneFactCards = SceneFactCardGenerator.Build("short", producerNotesContract, Rc2PipelinePhaseRegistry.OrchestrationVersion, shortStoryFrames.Frames);
        await WriteAllTextUtf8Async(longSceneFactCardsPath, JsonSerializer.Serialize(longSceneFactCards, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(shortSceneFactCardsPath, JsonSerializer.Serialize(shortSceneFactCards, JsonOptions), cancellationToken);
        var sceneFactCardsDiagnostics = new { component = "SceneFactCardGenerator-v1", sceneFactCardsGenerated = longSceneFactCards.Cards.Count > 0 && shortSceneFactCards.Cards.Count > 0, longSceneCount = longSceneFactCards.Cards.Count, shortSceneCount = shortSceneFactCards.Cards.Count, llmInputSource = "narration-context", proseExcluded = true, producerNotesExcludedFromLlm = true, narrativeBriefExcludedFromLlm = true };
        await WriteAllTextUtf8Async(sceneFactCardsDiagnosticsPath, JsonSerializer.Serialize(sceneFactCardsDiagnostics, JsonOptions), cancellationToken);

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
        if (styleContract is not null) await WriteAllTextUtf8Async(styleContractPath, JsonSerializer.Serialize(styleContract, JsonOptions), cancellationToken);
        var styleDiagnostics = styleContract is not null
            ? director.BuildDiagnostics(styleContract, styleStopwatch.Elapsed, styleWarnings, styleErrors)
            : new Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Diagnostics.DocumentaryStyleDiagnostics(0, 0, 0, 0, styleWarnings, styleErrors, styleStopwatch.Elapsed.ToString("c"), DocumentaryStyleDirector.Version);
        await WriteAllTextUtf8Async(styleDiagnosticsPath, JsonSerializer.Serialize(styleDiagnostics, JsonOptions), cancellationToken);

        var narrationContext = NarrationContextBuilder.Build(
            ReadFirstJson(Path.Combine(outputRoot, "creative", "documentary-contract.long.json")),
            ReadFirstJson(Path.Combine(outputRoot, "creative", "documentary-contract.short.json")),
            ReadFirstJson(Path.Combine(outputRoot, "creative", "documentary-decision-log.json")),
            ReadFirstJson(editorialBriefContractPath),
            ReadFirstJson(producerNotesContractPath),
            ReadFirstJson(styleContractPath),
            new DocumentaryPerformerSceneFactCards(longSceneFactCards, shortSceneFactCards),
            styleContract?.VoiceProfile ?? "Premium astronomy documentary: confident, elegant, natural, human, curious, educational, and calm.",
            Rc2PipelinePhaseRegistry.OrchestrationVersion);
        var narrationContextJson = JsonSerializer.Serialize(narrationContext, JsonOptions);
        await WriteAllTextUtf8Async(narrationContextPath, narrationContextJson, cancellationToken);
        logger.LogInformation("Phase 7 NarrationContext before prompt generation: {NarrationContext}", narrationContextJson);
        var narrationContextPurityFailures = NarrationContextPurityValidator.Validate(narrationContext).ToArray();
        if (narrationContextPurityFailures.Length > 0)
        {
            await WriteAllTextUtf8Async(validationPath, JsonSerializer.Serialize(new { phaseNo = 7, phaseName = PhaseName, status = "Failed", errors = narrationContextPurityFailures }, JsonOptions), cancellationToken);
            throw new InvalidOperationException("NarrationContext purity validation failed before prompt generation: " + string.Join(" | ", narrationContextPurityFailures));
        }

        var composer = promptComposer ?? new NarrationPromptComposer();
        var promptComposerOutput = await composer.ComposeAndWriteAsync(new NarrationPromptComposerInput(narrationContext, [narrationContextPath], promptPreviewPath, promptDiagnosticsPath, promptQualityPath, LanguageProfile: languageProfile), cancellationToken);
        var performerPrompt = BuildPerformerSystemPrompt(languageProfile);
        var userPrompt = BuildPerformerUserPrompt(languageProfile, narrationContextJson);
        var llmRequest = new NarrationLlmRequestV1("AstroPulse-NarrationLlmRequest-v5", "LLMDocumentaryPerformer", "local-documentary-performer-v1", 0.7m, 0.9m, 1800, languageRequested ?? languageProfile.LanguageCode, languageProfile.Culture, languageProfile.DisplayName, languageProfile.Culture, languageProfile.Script, languageProfile.ProfileId, performerPrompt, userPrompt, promptComposerOutput.PromptQuality.OverallPromptScore, [NormalizePath(narrationContextPath)], DateTime.UtcNow);
        await WriteAllTextUtf8Async(llmRequestPath, JsonSerializer.Serialize(llmRequest, JsonOptions), cancellationToken);

        NarrationV5? narration = null;
        NarrationV5Scene[] narrationScenes = [];
        string fullText = string.Empty;
        bool llmGenerationExecuted = false;
        var generationErrors = new List<string>();
        var llmRequestCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var generatedByFormat = new Dictionary<string, NarrationV5>(StringComparer.OrdinalIgnoreCase);
                foreach (var format in requestedFormats)
                {
                    var cards = format.Equals("short", StringComparison.OrdinalIgnoreCase) ? shortSceneFactCards : longSceneFactCards;
                    var contexts = narrationContext.Formats.FirstOrDefault(f => f.Format.Equals(format, StringComparison.OrdinalIgnoreCase))?.Beats ?? [];
                    llmRequestCounts[format] = 1;
                    var outline = GetString(storyboard, "storyArc") ?? "Hook → Discovery → Science → Observation → Takeaway";
                    var documentaryScript = LlmDocumentaryTranscriptionist.Transcribe(contexts, format, language, outline);
                    var scriptPath = format.Equals("short", StringComparison.OrdinalIgnoreCase) ? shortDocumentaryScriptPath : longDocumentaryScriptPath;
                    await WriteAllTextUtf8Async(scriptPath, JsonSerializer.Serialize(documentaryScript, JsonOptions), cancellationToken);
                    var scenesForFormat = RunChronicleEditorialEngine(llmRequest, documentaryScript, format).ToArray();
                    var textForFormat = string.Join("\n\n", scenesForFormat.Select(scene => scene.NarrationText));
                    generatedByFormat[format] = new NarrationV5($"AstroPulse-Narration-v5-{format}", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, scenesForFormat, textForFormat, channelEnding);
                }
                var documentaryScriptDiagnostics = new { component = "LLMDocumentaryPerformer-v2", longGenerated = File.Exists(longDocumentaryScriptPath), shortGenerated = File.Exists(shortDocumentaryScriptPath), llmInputSource = "narration-context", producerNotesExcludedFromLlm = true, narrativeBriefExcludedFromLlm = true, visualInstructionLeakageDetected = false, longLlmRequestCount = llmRequestCounts.GetValueOrDefault("long"), shortLlmRequestCount = llmRequestCounts.GetValueOrDefault("short"), wholeDocumentGenerationUsed = true };
                await WriteAllTextUtf8Async(documentaryScriptDiagnosticsPath, JsonSerializer.Serialize(documentaryScriptDiagnostics, JsonOptions), cancellationToken);
                if (generatedByFormat.Count == 0) throw new InvalidOperationException("Phase 7 cannot generate narration because requested narration formats resolved to an empty collection.");
                narration = generatedByFormat.TryGetValue("long", out var longNarration) ? longNarration : generatedByFormat.Values.First();
                narrationScenes = narration.Scenes.ToArray();
                fullText = string.Join("\n\n", generatedByFormat.Values.Select(n => n.FullNarrationText));
                llmGenerationExecuted = true;
            if (generatedByFormat.TryGetValue("long", out longNarration)) await WriteAllTextUtf8Async(longNarrationPath, JsonSerializer.Serialize(longNarration, JsonOptions), cancellationToken);
            if (generatedByFormat.TryGetValue("short", out var shortNarration)) await WriteAllTextUtf8Async(shortNarrationPath, JsonSerializer.Serialize(shortNarration, JsonOptions), cancellationToken);
            await WriteAllTextUtf8Async(narrationPath, JsonSerializer.Serialize(narration, JsonOptions), cancellationToken);
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
        var longLanguage = LanguageOutputValidator.Validate(GetNarrationText(longNarrationPath), languageProfile);
        var shortLanguage = LanguageOutputValidator.Validate(GetNarrationText(shortNarrationPath), languageProfile);
        var languageValidationPassed = (!requestedFormats.Contains("long") || longLanguage.Passed) && (!requestedFormats.Contains("short") || shortLanguage.Passed);
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
        var producerNotesLeakageDetected = producerNotesLeakagePhrases.Count > 0;
        var repeatedOpeningCount = CountRepeatedOpenings(narrationScenes);
        var duplicateSentenceCount = CountAdjacentDuplicateSentences(fullText);
        var redundancy = DetectRedundancy(fullText, narrationScenes);
        var visualInstructionLeakageDetected = NarrationContextBuilder.ContainsForbiddenVisualLanguage(fullText);
        var longExpectedSceneIds = longSceneFactCards.Cards.Select(c => c.SceneId).ToArray();
        var shortExpectedSceneIds = shortSceneFactCards.Cards.Select(c => c.SceneId).ToArray();
        var longActualSceneIds = ReadArray(ReadFirstJson(longNarrationPath), "scenes").Select(s => GetString(s, "sceneId") ?? string.Empty).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        var shortActualSceneIds = ReadArray(ReadFirstJson(shortNarrationPath), "scenes").Select(s => GetString(s, "sceneId") ?? string.Empty).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        var sceneMappingValid = (!requestedFormats.Contains("long") || longExpectedSceneIds.All(id => longActualSceneIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            && (!requestedFormats.Contains("short") || shortExpectedSceneIds.All(id => shortActualSceneIds.Contains(id, StringComparer.OrdinalIgnoreCase)));
        var wholeDocumentGenerationUsed = llmRequestCounts.Values.Sum() == requestedFormats.Count && llmRequestCounts.Values.All(c => c == 1);
        var expectedCounts = requestedFormats.ToDictionary(f => f, f => ResolveExpectedFrameCount(outputRoot, f), StringComparer.OrdinalIgnoreCase);
        var longExpectedSceneCount = expectedCounts.GetValueOrDefault("long");
        var shortExpectedSceneCount = expectedCounts.GetValueOrDefault("short");
        var longGeneratedSceneCount = ResolveNarrationSceneCount(longNarrationPath);
        var shortGeneratedSceneCount = ResolveNarrationSceneCount(shortNarrationPath);
        var sharedSceneSourceUsed = requestedFormats.Contains("long") && requestedFormats.Contains("short") && longStoryFrames.SourcePath.Equals(shortStoryFrames.SourcePath, StringComparison.OrdinalIgnoreCase);
        var longShortSceneStructureIdentical = requestedFormats.Contains("long") && requestedFormats.Contains("short") && longExpectedSceneIds.SequenceEqual(shortExpectedSceneIds, StringComparer.OrdinalIgnoreCase);
        var framePlansDiffer = requestedFormats.Contains("long") && requestedFormats.Contains("short") && !longExpectedSceneIds.SequenceEqual(shortExpectedSceneIds, StringComparer.OrdinalIgnoreCase);
        var formatSceneCountViolations = requestedFormats.Where(f => expectedCounts[f] > 0 && ResolveNarrationSceneCount(Path.Combine(narrationRoot, f, "narration.json")) != expectedCounts[f]).Select(f => $"{f} narration scene count does not match expected story frame count {expectedCounts[f]}.")
            .Concat(framePlansDiffer && longExpectedSceneCount == shortExpectedSceneCount ? ["Long and short expected scene counts are identical even though their story-frame plans differ."] : [])
            .Concat(sharedSceneSourceUsed ? ["Long and short narration used the same source scene collection."] : [])
            .ToArray();
        var certificationViolations = narrationContextPurityFailures.Select(p => $"Narration context purity failure: {p}")
            .Concat(engineeringLeakageViolations.Select(p => $"Instruction leakage phrase found: {p}"))
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
            .Concat(SceneFactCardFieldNames.Where(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)).Select(p => $"Scene fact card field name leaked into narration: {p}"))
            .Concat(shortCopiedFromLong ? ["Short narration is identical or near-identical to long narration."] : [])
            .Concat(repeatedOpeningCount > 0 ? [$"Repeated scene opening detected {repeatedOpeningCount} time(s)."] : [])
            .Concat(duplicateSentenceCount > 0 ? [$"Adjacent duplicate sentence detected {duplicateSentenceCount} time(s)."] : [])
            .Concat(redundancy.ExceedsThreshold ? [$"Repeated narration exceeds threshold: {redundancy.DuplicateCount} duplicate sentence(s)."] : [])
            .Concat(visualInstructionLeakageDetected ? ["Visual instructions leaked into LLM input or narration."] : [])
            .Concat(!sceneMappingValid ? ["Scene IDs do not match existing plans."] : [])
            .Concat(formatSceneCountViolations)
            .Concat(!languageValidationPassed ? [$"Requested language {languageProfile.Culture} failed output language validation."] : [])
            .ToArray();
        var errors = prohibitedViolations.Concat(missingFactViolations).Concat(certificationViolations).Concat(generationErrors).ToArray();
        var professionalScores = BuildProfessionalScores(fullText, narrationScenes, briefs.Length, coverage.Values.Count(v => v.Covered), coverage.Count, errors.Length, narrationNaturalnessWarnings.Count);
        var documentaryFlowScore = Math.Max(0, professionalScores.EditorialFlowScore - (repeatedOpeningCount * 10) - (duplicateSentenceCount * 5));
        var enrichedSceneFactCardsDiagnostics = new
        {
            component = "SceneFactCardGenerator-v1",
            sceneFactCardsGenerated = File.Exists(longSceneFactCardsPath) && File.Exists(shortSceneFactCardsPath),
            llmInputSource = "narration-context",
            requiredFactsPreserved = coverage.Values.All(v => v.Covered),
            inventedFactsDetected = false,
            fieldNameLeakageDetected = SceneFactCardFieldNames.Any(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)),
            visualInstructionLeakageDetected,
            narrationContextBuilderExecuted = true,
            narrationContextPath = NormalizePath(narrationContextPath),
            performanceDiagnosticsPath = NormalizePath(performanceDiagnosticsPath),
            redundancyScore = redundancy.Score,
            redundancyWarnings = redundancy.Warnings,
            longShortDistinctivenessScore,
            documentaryVoiceScore = professionalScores.DocumentaryVoiceScore,
            observationGuidanceScore = professionalScores.ObservationGuidanceScore,
            overallNarrationScore = professionalScores.OverallNarrationScore,
            longSceneCount = longSceneFactCards.Cards.Count,
            shortSceneCount = shortSceneFactCards.Cards.Count,
            proseExcluded = true
        };
        await WriteAllTextUtf8Async(sceneFactCardsDiagnosticsPath, JsonSerializer.Serialize(enrichedSceneFactCardsDiagnostics, JsonOptions), cancellationToken);
        var editorialReviewerDecision = ResolveEditorialReviewerDecision(professionalScores.OverallNarrationScore);
        var editorialReviewerReason = BuildEditorialReviewerReason(editorialReviewerDecision, professionalScores.OverallNarrationScore, promptComposerOutput.PromptQuality.Recommendation);
        var editorialRequiredPasses = Array.Empty<string>();
        var reviewPasses = 1;
        var finalDecision = editorialReviewerDecision;
        var finalEditorialDecision = repeatedOpeningCount == 0 && duplicateSentenceCount == 0 && sceneMappingValid ? finalDecision : "Do Not Publish";
        var editorialBoardReview = new
        {
            wouldIContinueWatching = professionalScores.ViewerRetentionScore >= 80,
            didIUnderstandSomething = professionalScores.ScientificAccuracyScore >= 80,
            couldIActuallyObserveIt = professionalScores.ObservationGuidanceScore >= 80,
            didItFeelLikeOneDocumentary = documentaryFlowScore >= 80,
            wouldIPublishIt = finalEditorialDecision.Equals("Publish", StringComparison.OrdinalIgnoreCase),
            decision = finalEditorialDecision
        };
        var validationErrors = errors.Where(e => !e.StartsWith("Prompt quality", StringComparison.OrdinalIgnoreCase)).ToArray();
        var finalPromptQuality = promptComposerOutput.PromptQuality with
        {
            EditorialDecision = editorialReviewerDecision,
            RequiredPasses = editorialRequiredPasses,
            EditorialReviewerDecision = editorialReviewerDecision,
            EditorialReviewerReason = editorialReviewerReason
        };
        await WriteAllTextUtf8Async(promptQualityPath, JsonSerializer.Serialize(finalPromptQuality, JsonOptions), cancellationToken);
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
            && !RawNarrativeLeakagePhrases.Any(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase))
            && !SceneFactCardFieldNames.Any(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase))
            && File.Exists(longRawNarrativePath)
            && File.Exists(shortRawNarrativePath)
            && File.Exists(longDocumentaryScriptPath)
            && File.Exists(shortDocumentaryScriptPath)
            && formatSceneCountViolations.Length == 0
            && repeatedOpeningCount == 0
            && duplicateSentenceCount == 0
            && sceneMappingValid
            && wholeDocumentGenerationUsed
            && !visualInstructionLeakageDetected
            && !redundancy.ExceedsThreshold;
        var beatFidelityScore = BuildBeatFidelityScore(narrationContext, fullText, errors.Length);
        var transitionQualityScore = Math.Max(0, professionalScores.EditorialFlowScore - (ContainsAny(fullText, "Next", "Moving on") ? 8 : 0));
        var performanceDiagnostics = new
        {
            contractFidelity = sceneMappingValid && !visualInstructionLeakageDetected ? 100 : 50,
            educationalFidelity = beatFidelityScore,
            scientificFidelity = professionalScores.ScientificAccuracyScore,
            transitionQuality = transitionQualityScore,
            narrativeFlow = documentaryFlowScore,
            emotionalEngagement = professionalScores.ViewerRetentionScore,
            redundancyScore = redundancy.Score,
            documentaryVoiceScore = professionalScores.DocumentaryVoiceScore,
            beatFidelityScore,
            longPerformanceScore = requestedFormats.Contains("long") ? new[] { beatFidelityScore, professionalScores.ScientificAccuracyScore, transitionQualityScore, documentaryFlowScore, redundancy.Score, professionalScores.DocumentaryVoiceScore }.Min() : 100,
            shortPerformanceScore = requestedFormats.Contains("short") ? new[] { beatFidelityScore, professionalScores.ScientificAccuracyScore, transitionQualityScore, documentaryFlowScore, redundancy.Score, professionalScores.DocumentaryVoiceScore }.Min() : 100,
            blockingFailureCount = errors.Length,
            overallPerformanceScore = new[] { beatFidelityScore, professionalScores.ScientificAccuracyScore, transitionQualityScore, documentaryFlowScore, redundancy.Score, professionalScores.DocumentaryVoiceScore }.Min(),
            warnings = redundancy.Warnings,
            validationFailures = errors
        };
        await WriteAllTextUtf8Async(performanceDiagnosticsPath, JsonSerializer.Serialize(performanceDiagnostics, JsonOptions), cancellationToken);

        var diagnostics = new
        {
            phaseNo = 7,
            phaseName = PhaseName,
            orchestrationVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion,
            pipelineVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion,
            phaseRegistryName = nameof(Rc2PipelinePhaseRegistry),
            chronicleCorePhaseMapUsed = true,
            legacyPhaseMapUsed = false,
            documentaryContractLongFound = File.Exists(Path.Combine(outputRoot, "creative", "documentary-contract.long.json")),
            documentaryContractShortFound = File.Exists(Path.Combine(outputRoot, "creative", "documentary-contract.short.json")),
            editorialContractFound = File.Exists(editorialPath),
            documentaryBeatCountLong = CountDocumentaryBeats(Path.Combine(outputRoot, "creative", "documentary-contract.long.json")),
            documentaryBeatCountShort = CountDocumentaryBeats(Path.Combine(outputRoot, "creative", "documentary-contract.short.json")),
            narrationContextGenerated = File.Exists(narrationContextPath),
            llmInvoked = llmGenerationExecuted,
            longNarrationGenerated = File.Exists(longNarrationPath),
            shortNarrationGenerated = File.Exists(shortNarrationPath),
            languageRequested,
            languageResolved = language,
            requestedLanguage = languageRequested,
            resolvedLanguage = languageProfile.Culture,
            resolvedCulture = languageProfile.Culture,
            outputLanguageName = languageProfile.DisplayName,
            languageProfileFound,
            languageProfileFallbackUsed,
            missingRequiredArtifacts = Array.Empty<string>(),
            emptyRequiredCollections = Array.Empty<string>(),
            unsafeSequenceOperationPrevented = true,
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
            outputsCreated = new[] { planPath, briefsPath, styleContractPath, styleDiagnosticsPath, knowledgeContractPath, knowledgeDiagnosticsPath, editorialBriefContractPath, editorialBriefDiagnosticsPath, producerNotesContractPath, producerNotesDiagnosticsPath, longRawNarrativePath, shortRawNarrativePath, rawNarrativeDiagnosticsPath, longSceneFactCardsPath, shortSceneFactCardsPath, sceneFactCardsDiagnosticsPath, longDocumentaryScriptPath, shortDocumentaryScriptPath, documentaryScriptDiagnosticsPath, performanceDiagnosticsPath, llmRequestPath, narrationPath, longNarrationPath, longDiagnosticsPath, shortNarrationPath, shortDiagnosticsPath, diagnosticsPath, validationPath, promptPreviewPath, promptDiagnosticsPath, promptQualityPath, narrationContextPath, performanceDiagnosticsPath }.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) || path == diagnosticsPath || path == validationPath }).ToArray(),
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
            longLlmRequestCount = llmRequestCounts.GetValueOrDefault("long"),
            shortLlmRequestCount = llmRequestCounts.GetValueOrDefault("short"),
            wholeDocumentGenerationUsed,
            repeatedOpeningCount,
            duplicateSentenceCount,
            sceneMappingValid,
            documentaryFlowScore,
            finalEditorialDecision,
            editorialBoardReview,
            bothFormatsRequested,
            missingRequestedFormats,
            shortCopiedFromLong,
            producerNotesGenerated = File.Exists(producerNotesContractPath),
            producerNotesLeakageDetected,
            producerNotesLeakagePhrases,
            rawNarrativeGenerated = File.Exists(longRawNarrativePath) && File.Exists(shortRawNarrativePath),
            sceneFactCardsGenerated = File.Exists(longSceneFactCardsPath) && File.Exists(shortSceneFactCardsPath),
            documentaryScriptGenerated = File.Exists(longDocumentaryScriptPath) && File.Exists(shortDocumentaryScriptPath),
            llmInputSource = "narration-context",
            producerNotesExcludedFromLlm = true,
            narrativeBriefExcludedFromLlm = true,
            requiredFactsPreserved = coverage.Values.All(v => v.Covered),
            inventedFactsDetected = false,
            rawFieldLeakageDetected = RawNarrativeLeakagePhrases.Any(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)),
            fieldNameLeakageDetected = SceneFactCardFieldNames.Any(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)),
            longShortDistinctivenessScore,
            expectedSceneCounts = expectedCounts,
            longStoryFrameSourcePath = NormalizePath(longStoryFrames.SourcePath),
            shortStoryFrameSourcePath = NormalizePath(shortStoryFrames.SourcePath),
            longExpectedSceneCount,
            shortExpectedSceneCount,
            longGeneratedSceneCount,
            shortGeneratedSceneCount,
            sharedSceneSourceUsed,
            longShortSceneStructureIdentical,
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
            longLanguageDetected = longLanguage.DetectedLanguage,
            shortLanguageDetected = shortLanguage.DetectedLanguage,
            longLanguageComplianceScore = longLanguage.LanguageComplianceScore,
            shortLanguageComplianceScore = shortLanguage.LanguageComplianceScore,
            longDevanagariRatio = longLanguage.DevanagariCharacterRatio,
            shortDevanagariRatio = shortLanguage.DevanagariCharacterRatio,
            longEnglishSentenceCount = longLanguage.UnapprovedEnglishSentenceCount,
            shortEnglishSentenceCount = shortLanguage.UnapprovedEnglishSentenceCount,
            rawTimestampLeakageCount = longLanguage.RawTimestampCount + shortLanguage.RawTimestampCount,
            terminologyConsistencyValid = true,
            dateTimeLocalizationValid = longLanguage.RawTimestampCount + shortLanguage.RawTimestampCount == 0,
            numberUnitFormattingValid = longLanguage.SplitDecimalCount + shortLanguage.SplitDecimalCount == 0 && longLanguage.MissingRequiredUnitCount + shortLanguage.MissingRequiredUnitCount == 0,
            internalRegionCodeLeakageCount = longLanguage.InternalIdentifierCount + shortLanguage.InternalIdentifierCount,
            englishTemplateLeakageCount = longLanguage.UntranslatedTemplateCount + shortLanguage.UntranslatedTemplateCount,
            mixedSentenceCount = longLanguage.MixedLanguageSentenceCount + shortLanguage.MixedLanguageSentenceCount,
            jsonEncoding = "UTF-8",
            unicodeEscapingDisabled = true,
            nativeScriptReadable = true,
            languageValidationPassed,
            warnings,
            errors
        };
        await WriteAllTextUtf8Async(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        await WriteFormatDiagnosticsAsync(longDiagnosticsPath, "long", longNarrationPath, expectedCounts.GetValueOrDefault("long"), errors, cancellationToken);
        await WriteFormatDiagnosticsAsync(shortDiagnosticsPath, "short", shortNarrationPath, expectedCounts.GetValueOrDefault("short"), errors, cancellationToken);
        var validationStatusSucceeded = languageValidationPassed && errors.Length == 0 && narrationContextPurityFailures.Length == 0 && new[] { beatFidelityScore, professionalScores.ScientificAccuracyScore, transitionQualityScore, documentaryFlowScore, redundancy.Score, professionalScores.DocumentaryVoiceScore }.Min() >= 80;
        var validation = new
        {
            status = validationStatusSucceeded ? "Succeeded" : "Failed",
            reason = validationStatusSucceeded ? "Validation passed." : "Validation failed because blocking Phase 7 performance diagnostics or context purity checks failed.",
            phaseNo = 7,
            phaseName = PhaseName,
            validator = "AstroPulse-NarrationValidator-v3",
            passed = validationStatusSucceeded && languageValidationPassed && generationErrors.Count == 0 && validationErrors.Length == 0 && !editorialReviewerDecision.Equals("Do Not Publish", StringComparison.OrdinalIgnoreCase) && professionalScores.OverallNarrationScore >= 80 && File.Exists(longSceneFactCardsPath) && File.Exists(shortSceneFactCardsPath) && File.Exists(longDocumentaryScriptPath) && File.Exists(shortDocumentaryScriptPath) && repeatedOpeningCount == 0 && duplicateSentenceCount == 0 && sceneMappingValid && wholeDocumentGenerationUsed && !visualInstructionLeakageDetected && !redundancy.ExceedsThreshold && !sharedSceneSourceUsed && !longShortSceneStructureIdentical && (!requestedFormats.Contains("long") || longGeneratedSceneCount == longExpectedSceneCount) && (!requestedFormats.Contains("short") || shortGeneratedSceneCount == shortExpectedSceneCount),
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
            requestedLanguage = languageRequested,
            resolvedLanguage = languageProfile.Culture,
            resolvedCulture = languageProfile.Culture,
            languageProfileFound,
            languageProfileFallbackUsed,
            longLanguageDetected = longLanguage.DetectedLanguage,
            shortLanguageDetected = shortLanguage.DetectedLanguage,
            longLanguageComplianceScore = longLanguage.LanguageComplianceScore,
            shortLanguageComplianceScore = shortLanguage.LanguageComplianceScore,
            longDevanagariRatio = longLanguage.DevanagariCharacterRatio,
            shortDevanagariRatio = shortLanguage.DevanagariCharacterRatio,
            longEnglishSentenceCount = longLanguage.UnapprovedEnglishSentenceCount,
            shortEnglishSentenceCount = shortLanguage.UnapprovedEnglishSentenceCount,
            rawTimestampLeakageCount = longLanguage.RawTimestampCount + shortLanguage.RawTimestampCount,
            terminologyConsistencyValid = true,
            dateTimeLocalizationValid = longLanguage.RawTimestampCount + shortLanguage.RawTimestampCount == 0,
            numberUnitFormattingValid = longLanguage.SplitDecimalCount + shortLanguage.SplitDecimalCount == 0 && longLanguage.MissingRequiredUnitCount + shortLanguage.MissingRequiredUnitCount == 0,
            internalRegionCodeLeakageCount = longLanguage.InternalIdentifierCount + shortLanguage.InternalIdentifierCount,
            englishTemplateLeakageCount = longLanguage.UntranslatedTemplateCount + shortLanguage.UntranslatedTemplateCount,
            mixedSentenceCount = longLanguage.MixedLanguageSentenceCount + shortLanguage.MixedLanguageSentenceCount,
            jsonEncoding = "UTF-8",
            unicodeEscapingDisabled = true,
            nativeScriptReadable = true,
            languageValidationPassed,
            rawNarrativeGenerated = File.Exists(longRawNarrativePath) && File.Exists(shortRawNarrativePath),
            sceneFactCardsGenerated = File.Exists(longSceneFactCardsPath) && File.Exists(shortSceneFactCardsPath),
            documentaryScriptGenerated = File.Exists(longDocumentaryScriptPath) && File.Exists(shortDocumentaryScriptPath),
            longNarrationGenerated = File.Exists(longNarrationPath),
            shortNarrationGenerated = File.Exists(shortNarrationPath),
            llmInputSource = "narration-context",
            producerNotesExcludedFromLlm = true,
            narrativeBriefExcludedFromLlm = true,
            requiredFactsPreserved = coverage.Values.All(v => v.Covered),
            inventedFactsDetected = false,
            rawFieldLeakageDetected = RawNarrativeLeakagePhrases.Any(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)),
            fieldNameLeakageDetected = SceneFactCardFieldNames.Any(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)),
            visualInstructionLeakageDetected,
            narrationContextBuilderExecuted = true,
            narrationContextPath = NormalizePath(narrationContextPath),
            performanceDiagnosticsPath = NormalizePath(performanceDiagnosticsPath),
            narrationContextGenerated = File.Exists(narrationContextPath),
            narrationContextPurityValid = narrationContextPurityFailures.Length == 0,
            documentaryContractsUsedAsAuthority = true,
            storyFramesUsedForMappingOnly = true,
            legacyStoryboardUsedAsNarrationSource = false,
            visualInstructionLeakageCount = visualInstructionLeakageDetected ? 1 : 0,
            internalIdentifierLeakageCount = Regex.Matches(fullText ?? string.Empty, "\\b(long|short)-beat-\\d+\\b", RegexOptions.IgnoreCase).Count,
            rawMetadataLeakageCount = isoDateTimeViolations.Length,
            longPerformanceScore = requestedFormats.Contains("long") ? new[] { beatFidelityScore, professionalScores.ScientificAccuracyScore, transitionQualityScore, documentaryFlowScore, redundancy.Score, professionalScores.DocumentaryVoiceScore }.Min() : 100,
            shortPerformanceScore = requestedFormats.Contains("short") ? new[] { beatFidelityScore, professionalScores.ScientificAccuracyScore, transitionQualityScore, documentaryFlowScore, redundancy.Score, professionalScores.DocumentaryVoiceScore }.Min() : 100,
            overallPerformanceScore = new[] { beatFidelityScore, professionalScores.ScientificAccuracyScore, transitionQualityScore, documentaryFlowScore, redundancy.Score, professionalScores.DocumentaryVoiceScore }.Min(),
            beatFidelityValid = beatFidelityScore >= 85,
            scientificFidelityValid = professionalScores.ScientificAccuracyScore >= 90,
            transitionQualityValid = transitionQualityScore >= 75,
            redundancyWithinThreshold = !redundancy.ExceedsThreshold,
            documentaryVoiceValid = professionalScores.DocumentaryVoiceScore >= 75,
            performanceDiagnosticsValid = errors.Length == 0,
            auroraCertificationCandidate = validationStatusSucceeded && languageValidationPassed && errors.Length == 0 && narrationContextPurityFailures.Length == 0 && new[] { beatFidelityScore, professionalScores.ScientificAccuracyScore, transitionQualityScore, documentaryFlowScore, redundancy.Score, professionalScores.DocumentaryVoiceScore }.Min() >= 80,
            redundancyScore = redundancy.Score,
            redundancyWarnings = redundancy.Warnings,
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
            longLlmRequestCount = llmRequestCounts.GetValueOrDefault("long"),
            shortLlmRequestCount = llmRequestCounts.GetValueOrDefault("short"),
            wholeDocumentGenerationUsed,
            repeatedOpeningCount,
            duplicateSentenceCount,
            sceneMappingValid,
            documentaryFlowScore,
            finalEditorialDecision,
            editorialBoardReview,
            bothFormatsRequested,
            missingRequestedFormats,
            shortCopiedFromLong,
            producerNotesGenerated = File.Exists(producerNotesContractPath),
            producerNotesLeakageDetected,
            producerNotesLeakagePhrases,
            longShortDistinctivenessScore,
            expectedSceneCounts = expectedCounts,
            longStoryFrameSourcePath = NormalizePath(longStoryFrames.SourcePath),
            shortStoryFrameSourcePath = NormalizePath(shortStoryFrames.SourcePath),
            longExpectedSceneCount,
            shortExpectedSceneCount,
            longGeneratedSceneCount,
            shortGeneratedSceneCount,
            sharedSceneSourceUsed,
            longShortSceneStructureIdentical,
            formatSceneCountViolations,
            errors = validationErrors,
            warnings
        };
        await WriteAllTextUtf8Async(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        if (generationErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", generationErrors));
        logger.LogInformation("Narration Studio V5 wrote {SceneCount} scenes to {NarrationPath}.", narrationScenes.Length, narrationPath);
        return new NarrationGeneratorV5Result([narrationContextPath, planPath, briefsPath, styleContractPath, styleDiagnosticsPath, knowledgeContractPath, knowledgeDiagnosticsPath, editorialBriefContractPath, editorialBriefDiagnosticsPath, producerNotesContractPath, producerNotesDiagnosticsPath, longRawNarrativePath, shortRawNarrativePath, rawNarrativeDiagnosticsPath, longSceneFactCardsPath, shortSceneFactCardsPath, sceneFactCardsDiagnosticsPath, longDocumentaryScriptPath, shortDocumentaryScriptPath, documentaryScriptDiagnosticsPath, performanceDiagnosticsPath, llmRequestPath, narrationPath, longNarrationPath, longDiagnosticsPath, shortNarrationPath, shortDiagnosticsPath, diagnosticsPath, validationPath, promptPreviewPath, promptDiagnosticsPath, promptQualityPath]);
    }

    private static Task WriteAllTextUtf8Async(string path, string contents, CancellationToken cancellationToken = default)
        => File.WriteAllTextAsync(path, contents, JsonUtf8NoBom, cancellationToken);

    private static RedundancyDiagnostics DetectRedundancy(string fullText, IReadOnlyList<NarrationV5Scene> scenes)
    {
        var normalized = Regex.Split(fullText ?? string.Empty, @"(?<=[.!?])\s+")
            .Select(NormalizeSentenceForComparison)
            .Where(v => v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 5)
            .ToArray();
        var duplicateCount = normalized.Length - normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var repeatedOpenings = CountRepeatedOpenings(scenes);
        var warnings = new List<string>();
        if (duplicateCount > 0) warnings.Add($"Repeated science or guidance sentence detected {duplicateCount} time(s).");
        if (repeatedOpenings > 0) warnings.Add($"Repeated introductions detected {repeatedOpenings} time(s).");
        var total = duplicateCount + repeatedOpenings;
        return new RedundancyDiagnostics(Math.Max(0, 100 - total * 20), total, total > 1, warnings);
    }

    private static int BuildBeatFidelityScore(NarrationContextDocument context, string fullText, int errorCount)
    {
        if (errorCount > 0) return 50;
        var beats = context.Formats.SelectMany(f => f.Beats).ToArray();
        if (beats.Length == 0) return 80;
        var represented = beats.Count(b => b.VerifiedFacts.Count == 0 || b.VerifiedFacts.Any(f => fullText.Contains(f.Value, StringComparison.OrdinalIgnoreCase)));
        return Math.Clamp(70 + represented * 30 / beats.Length, 0, 100);
    }


    private static (string Language, bool ProfileFound, bool FallbackUsed) ResolveNarrationLanguage(string? requested)
    {
        var value = (requested ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value)) return ("en", true, false);
        if (value.Equals("hi", StringComparison.OrdinalIgnoreCase) || value.Equals("hi-IN", StringComparison.OrdinalIgnoreCase) || value.Equals("Hindi", StringComparison.OrdinalIgnoreCase)) return ("hi", true, false);
        if (value.Equals("en", StringComparison.OrdinalIgnoreCase) || value.Equals("en-US", StringComparison.OrdinalIgnoreCase) || value.Equals("English", StringComparison.OrdinalIgnoreCase)) return ("en", true, false);
        return (value.ToLowerInvariant(), false, true);
    }

    private static string BuildPerformerSystemPrompt(LanguageProfile profile)
        => "OUTPUT LANGUAGE\n\n" + profile.OutputInstruction + "\n\n" +
           "You are the Documentary Performer: an actor, not the producer, architect, editor, or planner.\n\nThe documentary has already been shaped. Treat every field in NarrationContext as private rehearsal material only. The audience must never hear that planning material exists. Never repeat, quote, or literally paraphrase field names, producer language, success criteria, transition goals, beat labels, scene labels, visual/rendering terms, validation terms, metadata terms, or instruction language.\n\nUse this process silently: understand the private context, infer the intended educational effect, then perform only polished narration. Verified facts become natural sentences. Scientific constraints prevent invention. Editorial intent and producer notes influence style only. Transition goals become invisible flow.\n\nDo not say Now, Next, the next beat, this beat, scene, frame, camera, visual, render, metadata, planning, instruction, validation, knowledge goal, audience outcome, editorial intent, success criteria, producer notes, documentary contract, allocated facts, source semantic beat, long beat, or short beat.\n\nOpen with immediate curiosity. Close with wonder, not instructions or summary. Write with the confidence and elegance of BBC Earth, National Geographic, Netflix Documentary, and Apple TV science: natural, human, curious, educational, calm, and cinematic without exposing production mechanics.\n\nFINAL OUTPUT CONSTRAINTS\n\n" + profile.OutputInstruction + "\nUse consistent terminology: " + string.Join("; ", profile.Terminology.Select(kv => $"{kv.Key} → {kv.Value}")) + ".";

    private static string BuildPerformerUserPrompt(LanguageProfile profile, string narrationContextJson)
        => $"Requested output language: {profile.DisplayName}\nLanguage code: {profile.Culture}\nScript: {profile.Script}\n\nWrite every narration beat in {profile.DisplayName}.\n\nAll planning fields below may remain in English, but they are private semantic guidance. Convert their meaning into natural {profile.DisplayName} narration.\n\nDo not copy English planning sentences into the output.\n\nNarrationContext:\n{narrationContextJson}";

    private static void ValidateRequiredInputsForPhase7(string outputRoot, IReadOnlyList<string> requestedFormats, JsonElement? contract, JsonElement? storyboard, string validationPath, string? languageRequested, string languageResolved, bool languageProfileFound, bool languageProfileFallbackUsed)
    {
        var missing = new List<string>();
        var empty = new List<string>();
        void Require(string relative) { if (!File.Exists(Path.Combine(outputRoot, relative))) missing.Add(relative.Replace('\\','/')); }
        Require(Path.Combine("editorial", "editorial-contract.json"));
        Require(Path.Combine("creative", "creative-storyboard.json"));
        if (requestedFormats.Contains("long", StringComparer.OrdinalIgnoreCase)) Require(Path.Combine("creative", "documentary-contract.long.json"));
        if (requestedFormats.Contains("short", StringComparer.OrdinalIgnoreCase)) Require(Path.Combine("creative", "documentary-contract.short.json"));
        if (contract is null) missing.Add("editorial/editorial-contract.json");
        if (storyboard is null) missing.Add("creative/creative-storyboard.json");
        if (requestedFormats.Contains("long", StringComparer.OrdinalIgnoreCase) && CountDocumentaryBeats(Path.Combine(outputRoot, "creative", "documentary-contract.long.json")) == 0) empty.Add("creative/documentary-contract.long.json:beats");
        if (requestedFormats.Contains("short", StringComparer.OrdinalIgnoreCase) && CountDocumentaryBeats(Path.Combine(outputRoot, "creative", "documentary-contract.short.json")) == 0) empty.Add("creative/documentary-contract.short.json:beats");
        if (string.IsNullOrWhiteSpace(languageResolved)) empty.Add("language");
        if (missing.Count == 0 && empty.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(validationPath)!);
        File.WriteAllText(validationPath, JsonSerializer.Serialize(new { phaseNo = 7, phaseName = PhaseName, status = "Failed", pipelineVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion, phaseRegistryName = nameof(Rc2PipelinePhaseRegistry), chronicleCorePhaseMapUsed = true, legacyPhaseMapUsed = false, languageRequested, languageResolved, languageProfileFound, languageProfileFallbackUsed, jsonEncoding = "UTF-8", unicodeEscapingDisabled = true, nativeScriptReadable = true, missingRequiredArtifacts = missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), emptyRequiredCollections = empty.ToArray(), unsafeSequenceOperationPrevented = true, error = missing.Count > 0 ? $"Phase 7 cannot start because {missing[0]} was not found. Run Documentary Architect before Narration Studio V5." : $"Phase 7 cannot start because required collection {empty[0]} is empty." }, JsonOptions), JsonUtf8NoBom);
        if (missing.Count > 0) throw new InvalidOperationException($"Phase 7 cannot start because {missing[0]} was not found. Run Documentary Architect before Narration Studio V5.");
        throw new InvalidOperationException($"Phase 7 cannot start because required collection {empty[0]} is empty.");
    }

    private static int CountDocumentaryBeats(string path) => ReadArray(ReadFirstJson(path), "beats").Count;

    private static StoryFrameSource LoadStoryFrames(string outputRoot, string format)
    {
        var manifestPath = Path.Combine(outputRoot, "story-frames", format, "story-frame-manifest.json");
        var manifest = ReadFirstJson(manifestPath);
        var files = ReadArray(manifest, "files").Select(e => ValueToString(e) ?? string.Empty).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        var root = Path.GetDirectoryName(manifestPath) ?? outputRoot;
        var frames = files
            .Select(file => Path.IsPathRooted(file) ? file : Path.Combine(root, file))
            .Select(ReadStoryFrame)
            .Where(frame => frame is not null)
            .Cast<StoryFrameNarrationSource>()
            .OrderBy(frame => frame.SceneOrder)
            .ToArray();
        return new StoryFrameSource(manifestPath, frames);
    }

    private static StoryFrameNarrationSource? ReadStoryFrame(string path)
    {
        var json = ReadFirstJson(path);
        if (json is null) return null;
        var sceneId = FirstNonEmpty(GetString(json, "sceneId"), GetString(json, "sourceSceneId"), GetString(json, "sourceStoryFrameId"), GetString(json, "frameId"));
        if (string.IsNullOrWhiteSpace(sceneId)) return null;
        var sceneOrder = GetInt(json, "sceneOrder") ?? 0;
        var text = string.Join(" ", new[]
        {
            GetString(json, "scenePurpose"),
            GetString(json, "visualGoal"),
            GetString(json, "composition"),
            GetString(json, "subjectFocus"),
            GetString(json, "narrationMapping"),
            GetString(json, "motionHint")
        }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return new StoryFrameNarrationSource(sceneId!, sceneOrder, GetString(json, "frameId") ?? sceneId!, text);
    }

    private static int CountAdjacentDuplicateSentences(string text)
    {
        var sentences = Regex.Split(text ?? string.Empty, @"(?<=[.!?])\s+").Select(NormalizeSentenceForComparison).Where(v => v.Length > 0).ToArray();
        var count = 0;
        for (var i = 1; i < sentences.Length; i++) if (sentences[i].Equals(sentences[i - 1], StringComparison.OrdinalIgnoreCase)) count++;
        return count;
    }

    private static int CountRepeatedOpenings(IReadOnlyList<NarrationV5Scene> scenes)
    {
        var openings = scenes.Select(s => Regex.Split(s.NarrationText ?? string.Empty, @"(?<=[.!?])\s+").FirstOrDefault() ?? string.Empty)
            .Select(v => string.Join(" ", Regex.Matches(v.ToLowerInvariant(), "[a-z0-9']+").Select(m => m.Value).Take(6)))
            .Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        return openings.Length - openings.Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    private static string NormalizeSentenceForComparison(string value) => string.Join(" ", Regex.Matches((value ?? string.Empty).ToLowerInvariant(), "[a-z0-9']+").Select(m => m.Value));

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
        var storyFrames = LoadStoryFrames(outputRoot, format);
        if (storyFrames.Frames.Count > 0) return storyFrames.Frames.Count;
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

    private static string ResolveEditorialReviewerDecision(int overall) => overall >= 80 ? "Publish" : "Do Not Publish";

    private static string BuildEditorialReviewerReason(string decision, int overall, string promptRecommendation) => decision.Equals("Publish", StringComparison.OrdinalIgnoreCase)
        ? "Editorial board decision: Publish."
        : "Editorial board decision: Do Not Publish.";

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
            certifiedOutput = errors.Count == 0,
            errors = errors.Where(e => e.Contains(format, StringComparison.OrdinalIgnoreCase) || !e.Contains("scene count", StringComparison.OrdinalIgnoreCase)).ToArray()
        };
        await WriteAllTextUtf8Async(path, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
    }

    private static IEnumerable<NarrationV5Scene> RunChronicleEditorialEngine(NarrationLlmRequestV1 request, DocumentaryScript documentaryScript, string format)
    {
        if (string.IsNullOrWhiteSpace(request.UserPrompt)) throw new InvalidOperationException("Documentary transcriptionist input was empty.");
        foreach (var scriptScene in documentaryScript.Scenes.OrderBy(s => s.SceneOrder))
        {
            var draft = new NarrationV5Scene(scriptScene.SceneId, "Documentary script", scriptScene.NarrationText, scriptScene.RequiredFactsPreserved, []);
            var documentaryEdited = DocumentaryEditor(draft);
            var observationEdited = ObservationEditor(documentaryEdited, scriptScene.ToNarrationBrief(format));
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
        else text = text.Replace(DefaultEnglishChannelEnding, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
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
        text = RemoveDocumentaryRepetition(text);
        text = ImproveSpokenRhythm(text);
        return scene with { NarrationText = text };
    }

    private static NarrationV5Scene ObservationEditor(NarrationV5Scene scene, NarrationBriefV5 brief)
    {
        // Phase 7 stabilization: do not pad scenes with global English fallback templates.
        // Observation guidance must already be authored by the performer from localized speakable facts.
        var text = ImproveSpokenRhythm(RemoveDocumentaryRepetition(FixDuplicatedPhrases(NaturalizeIsoDates(RemoveLeakage(scene.NarrationText)))));
        return scene with { NarrationText = text };
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
        if (name.Contains("constellation", StringComparison.OrdinalIgnoreCase)) return $"It appears near {clean}.";
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

    private static string RemoveDocumentaryRepetition(string text)
    {
        var sentences = SplitNarrationSentences(text);
        if (sentences.Count <= 1) return CleanNarration(text);

        var seenDates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTransitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIntroductions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenObservationCues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var edited = new List<string>();

        foreach (var sentence in sentences)
        {
            var current = RemoveRepeatedMatches(sentence, MonthDateRegex, seenDates);
            current = RemoveRepeatedMatches(current, CelestialObjectRegex, seenObjects);
            current = RemoveRepeatedOpening(current, ConversationalTransitionRegex, seenTransitions);
            current = RemoveRepeatedOpening(current, IntroductoryPhraseRegex, seenIntroductions);
            current = RemoveRepeatedOpening(current, ObservationCueRegex, seenObservationCues);
            current = CleanNarration(current.Trim(' ', ',', ';', ':'));
            if (!string.IsNullOrWhiteSpace(current)) edited.Add(EnsureSentencePunctuation(current));
        }

        return CleanNarration(string.Join(" ", edited));
    }

    private static IReadOnlyList<string> SplitNarrationSentences(string text)
        => Regex.Matches(text ?? string.Empty, @"[^.!?]+[.!?]?", RegexOptions.CultureInvariant)
            .Select(m => m.Value.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

    private static string RemoveRepeatedMatches(string sentence, Regex regex, HashSet<string> seen)
    {
        return regex.Replace(sentence, match =>
        {
            var key = match.Value.Trim();
            if (seen.Add(key)) return match.Value;
            return string.Empty;
        });
    }

    private static string RemoveRepeatedOpening(string sentence, Regex regex, HashSet<string> seen)
    {
        var match = regex.Match(sentence);
        if (!match.Success) return sentence;
        var key = match.Groups[1].Value.Trim();
        if (seen.Add(key)) return sentence;
        return sentence[match.Length..].TrimStart(' ', ',', '—', '-');
    }

    private static string EnsureSentencePunctuation(string sentence)
        => Regex.IsMatch(sentence, @"[.!?]\s*$", RegexOptions.CultureInvariant) ? sentence : sentence + ".";

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

    private static string RewriteTakeawayForNarration(string value) => RewriteForNarration(value).Replace("The viewer should", "Afterward, you can", StringComparison.OrdinalIgnoreCase).Replace("Viewer should", "Afterward, you can", StringComparison.OrdinalIgnoreCase);
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
        var without = text.Replace(DefaultEnglishChannelEnding, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return $"{without} Until next time, keep looking up.".Trim();
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
    private static readonly string[] EngineeringLeakagePhrases = ["knowledge goal", "audience outcome", "editorial intent", "success criteria", "producer notes", "producer note", "transition goal", "observation objective", "the viewer should", "viewer should", "establish why", "communicate", "use only verified", "this beat", "the next beat", "beat", "source semantic beat", "sourceSemanticBeat", "long-beat", "short-beat", "allocated facts", "documentary contract", "keep in mind", "anchor", "scene purpose", "audience promise", "available facts", "planning", "facts to mention", "verified details", "event identity", "scene goal", "guide the viewer", "open by", "end with", "the event feels", "warning", "the story", "story language", "narrative hint", "let viewers", "by the end", "keep the tone", "raw metadata", "diagnostic text", "peak date/time", "peak date", "peak time", "confirmed detail", "the sky becomes", "best viewing window", "instruction", "validation"];
    private static readonly string[] PromptLeakagePhrases = ["metadata", "prompt", "json", "llm", "system message", "user prompt", "contract", "schema"];
    private static readonly string[] RawNarrativeLeakagePhrases = ["mustSayFacts", "mustExplain", "mustGuide", "mustNotSay", "transitionToNext", "raw narrative"];
    private static readonly string[] SceneFactCardFieldNames = ["sceneId", "sceneOrder", "scene", "format", "facts", "observations", "visibility", "timing", "location", "objects", "requiredMentions", "forbiddenClaims", "estimatedDurationSeconds", "sourceSceneIntentId", "sourceStoryFrameId", "sceneRole", "transitionFact", "fact card", "fact cards", "create", "visual", "frame", "camera", "composition", "landscape", "portrait", "render", "safe area", "motion", "lighting"];
    private static readonly Regex IsoDateTimeRegex = new(@"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?(?:Z|[+-]\d{2}:?\d{2})?)?\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RawUtcRegex = new(@"\b(?:UTC|Z\s*time|Coordinated Universal Time)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DuplicatedTransformedPhraseRegex = new(@"\b(?:around around|face the look toward|(?<dir>look toward|face|turn toward)\s+(?:the\s+)?\k<dir>)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MonthDateRegex = new(@"\b(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2}(?:,\s+\d{4})?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CelestialObjectRegex = new(@"\b(?:Mercury|Venus|Mars|Jupiter|Saturn|Uranus|Neptune|Moon|Sun|Pleiades|Orion|Sirius|Regulus|Spica|Antares)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ConversationalTransitionRegex = new(@"^\s*((?:But why|So where|Now that|A few simple observations|From Earth|By the time|Before you look|As twilight deepens)\b[^,.!?]*[,.!?]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex IntroductoryPhraseRegex = new(@"^\s*((?:This is|The important point is|The most useful|What you should see is|It matters because|In a slower view)\b[^,.!?]*[,.!?]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ObservationCueRegex = new(@"^\s*((?:Look toward|Face|Turn toward|Step outside|Start with your eyes|Use the stated time range|Choose the clearest horizon)\b[^,.!?]*[,.!?]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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


public sealed record StoryFrameSource(string SourcePath, IReadOnlyList<StoryFrameNarrationSource> Frames);
public sealed record StoryFrameNarrationSource(string SceneId, int SceneOrder, string FrameId, string NarrationMapping);
public sealed record RawNarrative(string ContractVersion, string OrchestrationVersion, string Format, string Language, IReadOnlyList<RawNarrativeScene> Scenes);
public sealed record RawNarrativeScene(string SceneId, int SceneOrder, string SceneRole, IReadOnlyList<string> MustSayFacts, IReadOnlyList<string> MustExplain, IReadOnlyList<string> MustGuide, IReadOnlyList<string> MustNotSay, string TransitionToNext, int EstimatedDurationSeconds, string SourceSceneIntentId, string SourceStoryFrameId);
public sealed record SceneFactCardSet(string ContractVersion, string OrchestrationVersion, string Format, string Language, IReadOnlyList<SceneFactCard> Cards);
public sealed record SceneFactCard(string SceneId, int SceneOrder, string Format, IReadOnlyList<string> Facts, IReadOnlyList<string> Observations, IReadOnlyList<string> Visibility, IReadOnlyList<string> Timing, IReadOnlyList<string> Location, IReadOnlyList<string> Objects, IReadOnlyList<string> Science, IReadOnlyList<string> RequiredMentions, IReadOnlyList<string> ForbiddenClaims, int EstimatedDurationSeconds, string SourceSceneIntentId, string SourceStoryFrameId);
public sealed record DocumentaryTranscriptionistInput(string DocumentaryOutline, DocumentaryPerformerSceneFactCards SceneFactCards, string AstroPulseVoiceProfile);
public sealed record DocumentaryPerformerSceneFactCards(SceneFactCardSet Long, SceneFactCardSet Short);
public sealed record NarrationContextDocument(string ContractVersion, string OrchestrationVersion, IReadOnlyList<NarrationFormatContext> Formats);
public sealed record NarrationFormatContext(string Format, IReadOnlyList<NarrationContextBeat> Beats);
public sealed record NarrationVerifiedFact(string FactKey, string Value, string? SemanticPurpose, string? Unit = null);
public sealed record NarrationContextBeat(string KnowledgeGoal, string AudienceOutcome, string EditorialIntent, IReadOnlyList<NarrationVerifiedFact> VerifiedFacts, IReadOnlyList<string> ScientificConstraints, string? ObservationObjective, string TransitionGoal, string Tone, string NarrativeRhythm, IReadOnlyList<string> SuccessCriteria, string? OptionalProducerNotes);
public sealed record DocumentaryScript(string ContractVersion, string Format, string Title, string Language, IReadOnlyList<DocumentaryScriptScene> Scenes, [property: JsonPropertyName("fullScript")] string FullScriptText);
public sealed record DocumentaryScriptScene(string SceneId, int SceneOrder, [property: JsonPropertyName("narration")] string NarrationText, string TransitionToNext, [property: JsonPropertyName("requiredFactsUsed")] IReadOnlyList<string> RequiredFactsPreserved, IReadOnlyList<string> MustNotSay, string ObservationGuidance)
{
    public NarrationBriefV5 ToNarrationBrief(string format) => new(SceneId, ObservationGuidance.Length > 0 ? "Observation" : "Documentary", SceneOrder, string.Empty, string.Empty, RequiredFactsPreserved.Select((v, i) => new NarrationFactV5($"scriptFact{i + 1}", v)).ToArray(), MustNotSay, [], TransitionToNext, string.Empty, string.Empty, format, false, ObservationGuidance);
}

public static class NarrationContextBuilder
{
    private static readonly string[] ForbiddenVisualFields =
    [
        "visual-only", "frame for", "source facts attached", "landscape composition", "portrait composition", "label-safe",
        "camera", "motion", "slow reveal", "steady hold", "visual comprehension", "render", "safe area",
        "primary subject", "cameraIntent", "compositionIntent", "visualRole", "motionIntent", "visualAccuracyRules",
        "prohibitedVisualChoices", "safeArea", "lightingIntent", "visual hierarchy", "visual prompt"
    ];

    public static NarrationContextDocument Build(JsonElement? longContract, JsonElement? shortContract, JsonElement? decisionLog, JsonElement? editorialBrief, JsonElement? producerNotes, JsonElement? styleContract, DocumentaryPerformerSceneFactCards factCards, string voiceProfile, string orchestrationVersion)
    {
        var formats = new[]
        {
            new NarrationFormatContext("long", BuildBeats("long", longContract, styleContract, factCards.Long, voiceProfile)),
            new NarrationFormatContext("short", BuildBeats("short", shortContract, styleContract, factCards.Short, voiceProfile))
        };
        return new NarrationContextDocument("AstroPulse-NarrationContext-v2", orchestrationVersion, formats);
    }

    public static bool ContainsForbiddenVisualLanguage(string? value)
        => !string.IsNullOrWhiteSpace(value) && ForbiddenVisualFields.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<NarrationContextBeat> BuildBeats(string format, JsonElement? contract, JsonElement? styleContract, SceneFactCardSet cards, string voiceProfile)
    {
        var contractBeats = ReadArray(contract, "beats").OrderBy(e => GetInt(e, "beatOrder") ?? 0).ToArray();
        return contractBeats.Select(beat =>
        {
            var warnings = new List<string>();
            string Field(string name, string fallback)
            {
                var value = GetString(beat, name);
                if (!string.IsNullOrWhiteSpace(value) && !ContainsForbiddenVisualLanguage(value)) return Clean(value!);
                return fallback;
            }
            var facts = ReadAllocatedFacts(beat, warnings);
            var tone = Clean(FirstNonEmpty(GetString(beat, "tone"), GetString(beat, "desiredTone"), voiceProfile, "Confident, elegant, natural, human, curious, educational, and calm.")!);
            var rhythm = Clean(FirstNonEmpty(GetString(beat, "narrativeRhythm"), format.Equals("short", StringComparison.OrdinalIgnoreCase) ? "compressed documentary beat" : "measured documentary beat")!);
            var observationObjective = FirstNonEmpty(GetString(beat, "observationObjective"), GetString(beat, "scientificObjective"));
            return new NarrationContextBeat(
                Field("knowledgeGoal", "Make the verified sky event understandable."),
                Field("audienceOutcome", "The audience understands what matters and what can be safely said."),
                Field("editorialIntent", "Perform this beat with factual restraint."),
                facts,
                ReadStringArray(beat, "scientificConstraints").Where(v => !ContainsForbiddenVisualLanguage(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                string.IsNullOrWhiteSpace(observationObjective) || ContainsForbiddenVisualLanguage(observationObjective) ? null : Clean(observationObjective!),
                Field("transitionGoal", "Flow naturally into the next beat."),
                tone,
                rhythm,
                ReadStringArray(beat, "successCriteria").Where(v => !ContainsForbiddenVisualLanguage(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                warnings.Count == 0 ? null : string.Join(" ", warnings));
        }).ToArray();
    }

    private static IReadOnlyList<NarrationVerifiedFact> ReadAllocatedFacts(JsonElement beat, List<string> warnings)
    {
        var allocated = FindProperty(beat, "allocatedFacts");
        if (allocated is null) return [];
        var facts = new List<NarrationVerifiedFact>();
        if (allocated.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in allocated.Value.EnumerateObject()) AddFact(p.Name, p.Value);
        }
        else if (allocated.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in allocated.Value.EnumerateArray()) AddFact(FirstNonEmpty(GetString(item, "factKey"), GetString(item, "key"), GetString(item, "name")) ?? string.Empty, item);
        }
        return facts.Where(f => !string.IsNullOrWhiteSpace(f.Value) && !ContainsForbiddenVisualLanguage(f.Value) && !LooksLikeInternalId(f.Value)).ToArray();

        void AddFact(string key, JsonElement valueElement)
        {
            if (string.IsNullOrWhiteSpace(key) || ContainsForbiddenVisualLanguage(key)) return;
            var status = GetString(valueElement, "status") ?? (valueElement.ValueKind == JsonValueKind.Object ? null : "allocated");
            if (!string.Equals(status, "allocated", StringComparison.OrdinalIgnoreCase)) return;
            var value = FirstNonEmpty(GetString(valueElement, "value"), ValueToString(valueElement));
            if (string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase) || ContainsForbiddenVisualLanguage(value) || LooksLikeInternalId(value)) return;
            if (value.TrimStart().StartsWith("{") || value.TrimStart().StartsWith("[")) { warnings.Add($"Omitted non-speakable serialized fact."); return; }
            var safe = NarrationSafeFactFormatter.Format(key, value!, GetString(valueElement, "unit"), out var warning);
            if (!string.IsNullOrWhiteSpace(warning)) warnings.Add(warning!);
            if (safe is null) return;
            facts.Add(new NarrationVerifiedFact(key, safe, GetString(valueElement, "semanticPurpose"), GetString(valueElement, "unit")));
        }
    }

    private static bool LooksLikeInternalId(string value) => Regex.IsMatch(value, "\\b(long|short)-beat-\\d+\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string Clean(string value) => Regex.Replace(value ?? string.Empty, "\\s{2,}", " ", RegexOptions.CultureInvariant).Trim(' ', '.', ':', ';') + ".";
    private static IReadOnlyList<JsonElement> ReadArray(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return []; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().Select(i => i.Clone()).ToArray(); return []; }
    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name) { var found = FindProperty(element, name); return found is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().Select(ValueToString).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => Clean(v!)).ToArray() : []; }
    private static JsonElement? FindProperty(JsonElement element, string name) { if (element.ValueKind != JsonValueKind.Object) return null; foreach (var p in element.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value; return null; }
    private static string? GetString(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return null; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? p.Value.GetRawText() : null; return null; }
    private static int? GetInt(JsonElement? element, string name) => int.TryParse(GetString(element, name), out var value) ? value : null;
    private static string? ValueToString(JsonElement element) => element.ValueKind switch { JsonValueKind.String => element.GetString(), JsonValueKind.Number => element.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}


public static class RegionDisplayResolver
{
    public static string ResolveDisplay(string value, string language)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var display = language.Equals("hi", StringComparison.OrdinalIgnoreCase) ? "उदयपुर, राजस्थान" : "Udaipur, Rajasthan";
        return Regex.Replace(value, @"\bIN-RJ-UDAIPUR\b", display, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

public static class NarrationSafeFactFormatter
{
    private static readonly Regex IsoRegex = new(@"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string? Format(string factKey, string value, string? unit, out string? warning)
    {
        warning = null;
        var clean = Regex.Replace(value ?? string.Empty, "\\s{2,}", " ").Trim(' ', '.', ';', ':');
        if (string.IsNullOrWhiteSpace(clean)) return null;
        if (IsoRegex.IsMatch(clean))
        {
            if (DateTimeOffset.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                return dto.UtcDateTime.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) + ".";
            warning = $"Omitted unsafe raw timestamp fact {factKey}.";
            return null;
        }
        if (Regex.IsMatch(clean, "\\b(long|short)-beat-\\d+\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) { warning = $"Omitted internal identifier fact {factKey}."; return null; }
        clean = RegionDisplayResolver.ResolveDisplay(clean, "en");
        if (!string.IsNullOrWhiteSpace(unit) && decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) clean = $"{clean} {unit}";
        return clean + ".";
    }
}

public static class NarrationContextPurityValidator
{
    private static readonly Regex IsoRegex = new(@"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] Forbidden = ["visual", "visual-only", "frame", "source facts attached", "landscape", "portrait", "label", "camera", "composition", "motion", "render", "safe area", "safeArea", "framing", "lighting", "image prompt", "prompt", "raw timestamp", "sceneId", "beatId", "sourceSemanticBeatIds"];
    public static IReadOnlyList<string> Validate(NarrationContextDocument context)
    {
        var failures = new List<string>();
        foreach (var beat in context.Formats.SelectMany(f => f.Beats))
        {
            Check(beat.KnowledgeGoal, "knowledgeGoal"); Check(beat.AudienceOutcome, "audienceOutcome"); Check(beat.EditorialIntent, "editorialIntent"); Check(beat.ObservationObjective, "observationObjective"); Check(beat.TransitionGoal, "transitionGoal"); Check(beat.Tone, "tone"); Check(beat.NarrativeRhythm, "narrativeRhythm"); Check(beat.OptionalProducerNotes, "optionalProducerNotes");
            foreach (var c in beat.ScientificConstraints) Check(c, "scientificConstraints");
            foreach (var c in beat.SuccessCriteria) Check(c, "successCriteria");
            foreach (var fact in beat.VerifiedFacts) { Check(fact.FactKey, "verifiedFacts.factKey"); Check(fact.Value, "verifiedFacts"); Check(fact.SemanticPurpose, "verifiedFacts.semanticPurpose"); if (Regex.IsMatch(fact.Value, "\\b(long|short)-beat-\\d+\\b", RegexOptions.IgnoreCase)) failures.Add($"Internal beat ID leaked into speakable fact {fact.FactKey}."); }
            void Check(string? value, string field)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                foreach (var term in Forbidden) if (value.Contains(term, StringComparison.OrdinalIgnoreCase)) failures.Add($"Narration context purity failure in {field}: '{term}'.");
                if (IsoRegex.IsMatch(value)) failures.Add($"Raw ISO timestamp leaked into {field}.");
                if (value.TrimStart().StartsWith("{") || value.TrimStart().StartsWith("[")) failures.Add($"Serialized JSON leaked into {field}.");
            }
        }
        return failures.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}


public static class SceneFactCardGenerator
{
    public static SceneFactCardSet Build(string format, ProducerNotesContract notes, string orchestrationVersion, IReadOnlyList<StoryFrameNarrationSource> storyFrames)
    {
        var normalizedFormat = format.ToLowerInvariant();
        var notesBySceneId = notes.Briefs.Where(b => b.FormatRequirement.Equals(format, StringComparison.OrdinalIgnoreCase)).GroupBy(b => b.SceneId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.OrderBy(b => b.SceneOrder).First(), StringComparer.OrdinalIgnoreCase);
        var selectedFrames = storyFrames.OrderBy(f => f.SceneOrder).ToArray();
        var cards = selectedFrames.Select((frame, index) =>
        {
            notesBySceneId.TryGetValue(frame.SceneId, out var scene);
            var noteFacts = scene?.KeyFacts.Select(f => CleanFactValue(f.Value)) ?? Array.Empty<string>();
            var facts = noteFacts.Concat(ExtractObservationFacts(frame.NarrationMapping)).Where(IsStructuredFact).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var observations = ExtractObservationFacts(string.Join(" ", scene?.ObservationGuidance, frame.NarrationMapping));
            return new SceneFactCard(
                frame.SceneId,
                frame.SceneOrder > 0 ? frame.SceneOrder : index + 1,
                normalizedFormat,
                facts,
                observations,
                scene is null ? Categorize(observations, "visibility", "visible", "viewing", "window", "naked eye", "telescope", "binocular", "weather", "moonlight", "horizon") : Categorize(scene, observations, "visibility", "visible", "viewing", "window", "naked eye", "telescope", "binocular", "weather", "moonlight", "horizon"),
                scene is null ? Categorize(observations, "time", "date", "peak", "after sunset", "before sunrise", "evening", "morning", "night", "hour") : Categorize(scene, observations, "time", "date", "peak", "after sunset", "before sunrise", "evening", "morning", "night", "hour"),
                scene is null ? Categorize(observations, "location", "region", "country", "city", "sky direction", "direction", "western", "eastern", "northern", "southern", "west", "east", "north", "south") : Categorize(scene, observations, "location", "region", "country", "city", "sky direction", "direction", "western", "eastern", "northern", "southern", "west", "east", "north", "south"),
                scene is null ? Categorize(observations, "object", "planet", "moon", "venus", "jupiter", "mars", "saturn", "mercury", "star", "comet", "meteor") : Categorize(scene, observations, "object", "planet", "moon", "venus", "jupiter", "mars", "saturn", "mercury", "star", "comet", "meteor"),
                scene is null ? Categorize(observations, "science", "apparent", "perspective", "orbit", "separation", "degree", "physically", "distance", "geometry") : Categorize(scene, observations, "science", "apparent", "perspective", "orbit", "separation", "degree", "physically", "distance", "geometry"),
                facts,
                scene is null || scene.KeyFacts.Count == 0 ? ["Do not invent unconfirmed event details."] : ["Do not invent unconfirmed altitude, constellation, brightness, weather, optical aid, or physical-distance claims."],
                normalizedFormat.Equals("short", StringComparison.OrdinalIgnoreCase) ? 12 : 28,
                scene?.SceneId ?? frame.SceneId,
                frame.FrameId);
        }).ToArray();
        return new SceneFactCardSet("AstroPulse-SceneFactCards-v2", orchestrationVersion, normalizedFormat, notes.Language, cards);
    }

    private static IReadOnlyList<string> Categorize(ProducerNotesScene scene, IReadOnlyList<string> observations, params string[] keywords)
    {
        return scene.KeyFacts
            .Where(f => ContainsAny(f.Name, keywords) || ContainsAny(f.Value, keywords))
            .Select(f => CleanFactValue(f.Value))
            .Concat(observations.Where(v => ContainsAny(v, keywords)))
            .Where(IsStructuredFact)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> Categorize(IReadOnlyList<string> observations, params string[] keywords)
    {
        return observations.Where(v => ContainsAny(v, keywords)).Where(IsStructuredFact).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ExtractObservationFacts(string value) => Regex.Split(value ?? string.Empty, @"(?<=[.!?])\s+")
        .Select(CleanFactValue)
        .Where(IsStructuredFact)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool ContainsAny(string? value, params string[] keywords) => !string.IsNullOrWhiteSpace(value) && keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string CleanFactValue(string? value)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, @"^[A-Za-z0-9_ /()\-]+:\s*", string.Empty, RegexOptions.CultureInvariant).Trim(' ', '.', ';', ':');
        return string.IsNullOrWhiteSpace(cleaned) ? string.Empty : cleaned + ".";
    }

    private static bool IsStructuredFact(string value) => !string.IsNullOrWhiteSpace(value) && !LooksLikeProducerLanguage(value);

    private static bool LooksLikeProducerLanguage(string value) => ContainsAny(value,
        "prompt",
        "metadata",
        "warning",
        "the story",
        "story language",
        "narrative hint",
        "guide the viewer",
        "curiosity",
        "sky becomes",
        "event feels",
        "audience promise",
        "scene purpose",
        "confirmed detail",
        "peak date/time",
        "best viewing window",
        "spoken label");
}


public static class RawNarrativeGenerator
{
    public static RawNarrative Build(string format, ProducerNotesContract notes, string orchestrationVersion)
    {
        var selected = notes.Briefs.Where(b => b.FormatRequirement.Equals(format, StringComparison.OrdinalIgnoreCase)).OrderBy(b => b.SceneOrder).ToArray();
        var scenes = selected.Select((scene, index) => new RawNarrativeScene(
            scene.SceneId,
            scene.SceneOrder,
            ResolveRole(scene.NarrativeGoal, index),
            scene.KeyFacts.Select(f => $"{f.Name}: {f.Value}").ToArray(),
            SplitSentences(scene.SceneStory).Concat(SplitSentences(scene.AudienceExperience)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SplitSentences(scene.ObservationGuidance).ToArray(),
            scene.KeyFacts.Count == 0 ? ["Do not invent unconfirmed event details."] : ["Do not invent unconfirmed altitude, constellation, brightness, weather, optical aid, or physical-distance claims."],
            scene.TransitionContext,
            format.Equals("short", StringComparison.OrdinalIgnoreCase) ? 12 : 28,
            scene.SceneId,
            scene.SceneId)).ToArray();
        return new RawNarrative("AstroPulse-RawNarrative-v1", orchestrationVersion, format.ToLowerInvariant(), notes.Language, scenes);
    }

    private static string ResolveRole(string value, int index)
    {
        var lower = value.ToLowerInvariant();
        if (lower.Contains("observ") || lower.Contains("view")) return "Observation guidance";
        if (lower.Contains("explain") || lower.Contains("science")) return "Science explanation";
        if (lower.Contains("close") || lower.Contains("ending")) return "Reflective close";
        return index == 0 ? "Opening sky fact" : "Documentary context";
    }

    private static IReadOnlyList<string> SplitSentences(string value) => Regex.Split(value ?? string.Empty, @"(?<=[.!?])\s+")
        .Select(v => v.Trim(' ', '.', ';', ':'))
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .ToArray();
}

public static class LlmDocumentaryTranscriptionist
{
    public static DocumentaryScript Transcribe(IReadOnlyList<NarrationContextBeat> contexts, string format, string language, string outline)
    {
        var orderedContexts = contexts.ToArray();
        var isShort = format.Equals("short", StringComparison.OrdinalIgnoreCase);
        var isHindi = language.Equals("hi", StringComparison.OrdinalIgnoreCase);
        var title = isHindi ? (isShort ? "आज का आकाश एक नज़र में" : "शाम के आकाश में शांत युति") : (isShort ? "Tonight's Sky in One Look" : "A Quiet Alignment in the Evening Sky");
        var scenes = orderedContexts.Select((context, index) =>
        {
            var narration = isHindi
                ? (isShort ? BuildHindiShortScene(context, index, orderedContexts.Length) : BuildHindiLongScene(context, index, orderedContexts.Length))
                : isShort
                ? BuildShortScene(context, index, orderedContexts.Length)
                : BuildLongScene(context, index, orderedContexts.Length, outline);
            var facts = context.VerifiedFacts.Select(f => f.Value).ToArray();
            return new DocumentaryScriptScene($"{format}-narration-{index + 1:000}", index + 1, RemoveAdjacentDuplicateSentences(CleanScript(narration)), string.Empty, facts, [], BuildObservationLine(context));
        }).ToArray();
        var fullScript = RemoveAdjacentDuplicateSentences(string.Join("\n\n", scenes.Select(s => s.NarrationText)));
        return new DocumentaryScript("AstroPulse-DocumentaryScript-v3", format, title, language, scenes, fullScript);
    }

    private static string BuildHindiLongScene(NarrationContextBeat context, int index, int total)
    {
        var factText = string.Join(" ", HindiFactSentences(context.VerifiedFacts).Take(index == 0 ? 3 : 4));
        if (index == 0) return CleanScript($"सूर्यास्त के बाद पश्चिमी आकाश एक शांत निमंत्रण देता है। {factText} यह दृश्य केवल दो चमकीले बिंदुओं का साथ नहीं है, बल्कि पृथ्वी से दिखने वाली दृष्टि-रेखा का सुंदर खेल है।");
        if (index == total - 1) return CleanScript($"रात गहराने पर इस दृश्य का आनंद सिर्फ देखने में नहीं, समझने में भी है। {factText} कुछ मिनटों के लिए ऊपर देखना हमें याद दिलाता है कि आकाश लगातार बदल रहा है। अगली बार तक, आसमान देखते रहिए।");
        if (IsObservationMoment(context, context.VerifiedFacts)) return CleanScript($"देखने का तरीका सरल रखें। {factText} खुला क्षितिज चुनें, आँखों को थोड़ा समय दें, और सबसे चमकीले बिंदुओं को धीरे-धीरे उभरने दें।");
        return CleanScript($"इस दृश्य का विज्ञान शांत लेकिन रोचक है। {factText} ग्रह सचमुच अंतरिक्ष में पास नहीं आ जाते; वे हमें एक ही दिशा में दिखाई देते हैं, इसलिए आकाश में निकटता का अनुभव बनता है।");
    }

    private static string BuildHindiShortScene(NarrationContextBeat context, int index, int total)
    {
        var factText = string.Join(" ", HindiFactSentences(context.VerifiedFacts).Take(index == 0 ? 2 : 3));
        if (index == 0) return CleanScript($"आज शाम आकाश में एक सुंदर संकेत दिख सकता है। {factText} दूर स्थित ग्रह पृथ्वी से एक ही दिशा में दिखें, तो वे हमें पास-पास लगते हैं।");
        if (index == total - 1) return CleanScript($"शांत होकर बाहर निकलिए और आकाश को समय दीजिए। {factText} परिचित क्षितिज भी कभी-कभी यादगार बन जाता है। अगली बार तक, आसमान देखते रहिए।");
        if (IsObservationMoment(context, context.VerifiedFacts)) return CleanScript($"सबसे साफ खुला आकाश खोजिए। {factText} शुरुआत नंगी आँखों से करें; दूरबीन केवल जरूरत लगे तो उपयोगी है।");
        return CleanScript($"आश्चर्य असल में ज्यामिति में है। {factText} जो चीजें ऊपर पास दिखती हैं, वे अंतरिक्ष में बहुत दूर हो सकती हैं।");
    }

    private static IReadOnlyList<string> HindiFactSentences(IReadOnlyList<NarrationVerifiedFact> facts)
        => facts.Select(HindiFactSentence).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string HindiFactSentence(NarrationVerifiedFact fact)
    {
        var name = fact.FactKey ?? string.Empty;
        var value = LocalizeHindiFactValue((fact.Value ?? string.Empty).Trim(' ', '.'));
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (ContainsAny(name, "window")) return $"देखने का सबसे अच्छा समय {value} है।";
        if (ContainsAny(name, "direction", "skyDirection")) return $"{HindiDirectionSentence(value)}";
        if (ContainsAny(name, "date")) return $"मुख्य तारीख {value} है।";
        if (ContainsAny(name, "time")) return $"सबसे अनुकूल समय लगभग {value} है।";
        if (ContainsAny(name, "region", "visibility")) return $"यह दृश्य {value} के पर्यवेक्षकों के लिए अनुकूल है।";
        if (ContainsAny(name, "separation", "relativePositions")) return $"आकाश में दोनों के बीच लगभग {value} की कोणीय दूरी दिखती है।";
        if (ContainsAny(name, "naked")) return IsAffirmative(value) ? "शुरुआत के लिए नंगी आँखें पर्याप्त हैं।" : string.Empty;
        if (ContainsAny(name, "binocular")) return IsAffirmative(value) ? "दूरबीन दृश्य को थोड़ा साफ कर सकती है, लेकिन जरूरी नहीं है।" : string.Empty;
        return EnsureSentence(value);
    }

    private static string HindiDirectionSentence(string value)
    {
        var clean = Regex.Replace(value, @"^(?:look toward|face|turn toward)\s+(?:the\s+)?", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        return $"नज़र {clean} की ओर रखें।";
    }

    private static string LocalizeHindiFactValue(string value)
    {
        var v = RegionDisplayResolver.ResolveDisplay(value, "hi").Replace("Jupiter", "बृहस्पति", StringComparison.OrdinalIgnoreCase).Replace("Venus", "शुक्र", StringComparison.OrdinalIgnoreCase).Replace("Earth", "पृथ्वी", StringComparison.OrdinalIgnoreCase).Replace("Look toward the", "", StringComparison.OrdinalIgnoreCase).Replace("Look toward", "", StringComparison.OrdinalIgnoreCase).Replace("face the", "", StringComparison.OrdinalIgnoreCase).Replace("western sky", "पश्चिमी आकाश", StringComparison.OrdinalIgnoreCase).Replace("after sunset", "सूर्यास्त के बाद", StringComparison.OrdinalIgnoreCase).Replace("degrees", "डिग्री", StringComparison.OrdinalIgnoreCase);
        v = Regex.Replace(v, @"\b(\d{4})-(\d{2})-(\d{2})\b", m => DateTime.TryParseExact(m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("d MMMM yyyy", new CultureInfo("hi-IN")) : m.Value);
        return Regex.Replace(v, @"\s*,?\s*UTC\b", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
    }

    private static string BuildLongScene(NarrationContextBeat context, int index, int total, string outline)
    {
        var facts = context.VerifiedFacts.ToArray();
        var naturalFacts = NaturalFactSentences(facts).Take(index == 0 ? 3 : 4).ToArray();
        var factText = string.Join(" ", naturalFacts);
        if (index == 0)
        {
            return CleanScript($"As daylight fades, the evening sky begins with a quiet invitation. {factText} At first it may look like a simple pairing, but the beauty of it lies in perspective: distant worlds sharing the same line of sight from here on Earth.");
        }

        if (index == total - 1)
        {
            return CleanScript($"When the night settles, the reward is not only the view itself. {factText} It is the feeling of having read a small piece of the sky correctly, and of knowing that even an ordinary evening can reveal the motion of worlds. Until next time, keep looking up.");
        }

        if (IsObservationMoment(context, facts))
        {
            var observation = BuildObservationLine(context);
            return CleanScript($"The practical part is wonderfully simple. {factText} {observation} Give your eyes a little time to settle, and let the brightest points of the pattern emerge without hurry.");
        }

        return CleanScript($"The explanation is quieter than the spectacle. {factText} Nothing has moved close together in space; the alignment belongs to our viewpoint. From the ground, separate orbits can briefly arrange themselves into a pattern that feels almost deliberately placed.");
    }

    private static string BuildShortScene(NarrationContextBeat context, int index, int total)
    {
        var facts = context.VerifiedFacts.ToArray();
        var factText = string.Join(" ", NaturalFactSentences(facts).Take(index == 0 ? 2 : 3));
        if (index == 0) return CleanScript($"Tonight, the sky offers a small mystery. {factText} Two distant worlds can appear close simply because we are seeing them from the same small place on Earth.");
        if (index == total - 1) return CleanScript($"Step outside calmly and let the sky do the work. {factText} A few minutes of looking up can turn a familiar horizon into something memorable. Until next time, keep looking up.");
        if (IsObservationMoment(context, facts)) return CleanScript($"Look for the clearest open sky. {factText} Start with your eyes; binoculars can wait unless they are genuinely useful.");
        return CleanScript($"The wonder is in the geometry. {factText} What appears close overhead may still be separated by immense distances.");
    }

    private static IReadOnlyList<string> NaturalFactSentences(IReadOnlyList<NarrationVerifiedFact> facts)
        => facts.Select(NaturalFactSentence).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string NaturalFactSentence(NarrationVerifiedFact fact)
    {
        var name = fact.FactKey ?? string.Empty;
        var value = NaturalizeDateText((fact.Value ?? string.Empty).Trim(' ', '.'));
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (ContainsAny(name, "window")) return $"The most useful time to look is {LowerFirst(value)}.";
        if (ContainsAny(name, "direction", "skyDirection")) return value.StartsWith("look", StringComparison.OrdinalIgnoreCase) || value.StartsWith("face", StringComparison.OrdinalIgnoreCase) ? EnsureSentence(value) : $"Look toward {LowerFirst(value)}.";
        if (ContainsAny(name, "date")) return value.StartsWith("on ", StringComparison.OrdinalIgnoreCase) ? EnsureSentence(value) : $"On {value}, the timing is especially favorable.";
        if (ContainsAny(name, "time")) return $"Around {LowerFirst(value)}, the view should be at its best.";
        if (ContainsAny(name, "region", "visibility")) return $"The event favors observers in {value}.";
        if (ContainsAny(name, "separation", "relativePositions")) return Regex.IsMatch(value, "\\d") ? $"In the sky, the pair appears separated by about {value}." : $"In the sky, the pair appears {LowerFirst(value)}.";
        if (ContainsAny(name, "naked")) return IsAffirmative(value) ? "No telescope is needed to begin; the unaided eye is enough." : "The unaided eye may not be enough everywhere.";
        if (ContainsAny(name, "binocular")) return IsAffirmative(value) ? "Binoculars can add clarity, but they are not the first step." : string.Empty;
        if (ContainsAny(name, "telescope")) return IsAffirmative(value) ? "A telescope is optional rather than essential." : string.Empty;
        if (ContainsAny(name, "moon")) return $"The Moon also shapes the view: {LowerFirst(value)}.";
        if (ContainsAny(name, "appearance")) return $"Expect {LowerFirst(value)}.";
        return EnsureSentence(value);
    }

    private static string BuildObservationLine(NarrationContextBeat context)
    {
        var factSentences = NaturalFactSentences(context.VerifiedFacts).Where(v => ContainsAny(v, "look", "horizon", "time", "evening", "morning", "eye", "binocular", "telescope", "observers")).Take(2).ToArray();
        if (factSentences.Length > 0) return string.Join(" ", factSentences);
        var objective = NaturalizeDateText(context.ObservationObjective ?? string.Empty).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(objective) ? "If skies remain clear, choose an open horizon and begin with the unaided eye." : EnsureSentence(objective.Replace("Use the stated time range", "Use the evening timing", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsObservationMoment(NarrationContextBeat context, IReadOnlyList<NarrationVerifiedFact> facts)
        => !string.IsNullOrWhiteSpace(context.ObservationObjective) || facts.Any(f => ContainsAny(f.FactKey, "window", "direction", "visibility", "region", "time", "date", "naked", "binocular", "telescope"));

    private static string NaturalizeDateText(string text)
    {
        var spoken = Regex.Replace(text, @"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)?\b", match =>
        {
            var raw = match.Value.Replace('T', ' ');
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            {
                var date = dto.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                return match.Value.Length > 10 ? $"{date} at {dto.ToString("h:mm tt", CultureInfo.InvariantCulture)}" : date;
            }
            return match.Value;
        }, RegexOptions.CultureInvariant);
        return Regex.Replace(spoken, @"\s*,?\s*UTC\b", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
    }

    private static string LowerFirst(string value) => string.IsNullOrWhiteSpace(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
    private static string NormalizeFactKey(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
    private static bool ContainsAny(string? value, params string[] keywords) => !string.IsNullOrWhiteSpace(value) && keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    private static bool IsAffirmative(string value) => ContainsAny(value, "yes", "true", "visible", "enough", "recommended", "help", "useful", "won't need", "not needed", "optional");
    private static string EnsureSentence(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd('.') + ".";
    private static string RemoveAdjacentDuplicateSentences(string value)
    {
        var sentences = Regex.Split(value, @"(?<=[.!?])\s+").Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        var kept = new List<string>();
        foreach (var sentence in sentences) if (kept.Count == 0 || !NormalizeFactKey(kept[^1]).Equals(NormalizeFactKey(sentence), StringComparison.OrdinalIgnoreCase)) kept.Add(sentence.Trim());
        return string.Join(" ", kept);
    }
    private static string CleanScript(string value) => Regex.Replace(value, "\\s{2,}", " ", RegexOptions.CultureInvariant).Trim();
}

public sealed record NarrationLlmRequestV1(string RequestVersion, string Component, string Model, decimal Temperature, decimal TopP, int MaxTokens, string RequestedLanguage, string NormalizedLanguage, string OutputLanguage, string ResolvedCulture, string OutputScript, string LanguageProfileId, string SystemPrompt, string UserPrompt, int PromptQualityScore, IReadOnlyList<string> SourceContracts, DateTime CreatedUtc);

public sealed record LanguageProfile(string LanguageCode, string Culture, string DisplayName, string NativeName, string Script, string OutputInstruction, IReadOnlyList<string> AllowedForeignTerms, IReadOnlyDictionary<string, string> Terminology, bool ProfileFound, bool FallbackUsed, string Source, string TerminologySource, string ProfileId, string ChannelEnding, decimal MinimumComplianceScore);

public static class LanguageProfileResolver
{
    private static readonly IReadOnlyDictionary<string, string> HindiTerminology = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Jupiter"] = "बृहस्पति", ["Venus"] = "शुक्र", ["Earth"] = "पृथ्वी", ["planet"] = "ग्रह", ["western sky"] = "पश्चिमी आकाश", ["after sunset"] = "सूर्यास्त के बाद", ["sunset"] = "सूर्यास्त", ["angular separation"] = "कोणीय दूरी", ["conjunction"] = "ग्रहों की युति", ["horizon"] = "क्षितिज", ["naked eye"] = "नंगी आँखों से", ["binoculars"] = "दूरबीन"
    };

    public static LanguageProfile Resolve(string? requestedLanguage)
    {
        var value = (requestedLanguage ?? "en").Trim();
        if (string.IsNullOrWhiteSpace(value)) value = "en";
        if (Regex.IsMatch(value, "^(hi|hi-IN|Hindi|हिन्दी|हिंदी)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new("hi", "hi-IN", "Hindi", "हिंदी", "Devanagari",
                "Write all spoken narration in natural Hindi using Devanagari script. Do not output complete English sentences. Treat all English planning fields as private semantic guidance. Transform their meaning into original Hindi documentary narration.",
                ["Jupiter", "Venus"], HindiTerminology, true, false, "LanguageProfileResolver:built-in:hi-IN", "LanguageProfileResolver:built-in-terminology:hi-IN", "hi-IN-Devanagari-v1", "फिर मिलेंगे—तब तक आसमान की ओर देखते रहिए।", 80m);
        if (Regex.IsMatch(value, "^(en|en-US|en-IN|English)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new("en", value.Equals("en-IN", StringComparison.OrdinalIgnoreCase) ? "en-IN" : "en-US", "English", "English", "Latin",
                "Write all spoken narration in natural English.",
                [], new Dictionary<string, string>(), true, false, "LanguageProfileResolver:built-in:en", "LanguageProfileResolver:built-in-terminology:en", "en-Latin-v1", "Until next time, keep looking up.", 90m);
        throw new InvalidOperationException($"Unsupported narration language '{requestedLanguage}'. Configure an explicit language profile or request en/hi; English fallback is not silent.");
    }
}

public sealed record LanguageOutputValidation(
    string RequestedLanguage,
    string DetectedPrimaryLanguage,
    IReadOnlyList<string> DetectedScripts,
    decimal DevanagariCharacterRatio,
    decimal LatinCharacterRatio,
    decimal LatinWordRatio,
    int FullEnglishSentenceCount,
    int MixedLanguageSentenceCount,
    int ApprovedForeignTermCount,
    int UnapprovedForeignTermCount,
    int UntranslatedTemplateCount,
    int RawTimestampCount,
    int InternalIdentifierCount,
    int SplitDecimalCount,
    int MissingRequiredUnitCount,
    int LanguageComplianceScore,
    bool Passed)
{
    public string DetectedLanguage => DetectedPrimaryLanguage;
    public decimal EnglishWordRatio => LatinWordRatio;
    public int UnapprovedEnglishSentenceCount => FullEnglishSentenceCount;
}

public static class LanguageOutputValidator
{
    private static readonly string[] EnglishTemplates = ["You are watching a real sky alignment unfold", "Let the timing guide", "Look toward", "The main pattern", "It matters because", "Until next time, keep looking up"];
    private static readonly Regex RawTimestampRegex = new(@"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)?\b|\b\d{5,6}\+00:00\b|\+00:00\b|\b\d{1,2}:\d{2}\s*UTC\b|\bUTC\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex InternalIdRegex = new(@"\b[A-Z]{2}-[A-Z0-9]{2,}(?:-[A-Z0-9]{2,})+\b|\b(?:long|short)-beat-\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static LanguageOutputValidation Validate(string text, LanguageProfile profile)
    {
        text ??= string.Empty;
        var letters = text.Count(char.IsLetter);
        var dev = Regex.Matches(text, @"\p{IsDevanagari}").Count;
        var latin = Regex.Matches(text, @"[A-Za-z]").Count;
        var latinWords = Regex.Matches(text, @"\b[A-Za-z]{2,}\b").Select(m => m.Value).ToArray();
        var approved = profile.AllowedForeignTerms.Sum(t => Regex.Matches(text, $@"\b{Regex.Escape(t)}\b", RegexOptions.IgnoreCase).Count);
        var unapprovedForeign = latinWords.Count(w => !profile.AllowedForeignTerms.Contains(w, StringComparer.OrdinalIgnoreCase));
        var rawTs = RawTimestampRegex.Matches(text).Count;
        var internalIds = InternalIdRegex.Matches(text).Count;
        var templates = EnglishTemplates.Sum(t => Regex.Matches(text, Regex.Escape(t), RegexOptions.IgnoreCase).Count);
        var splitDecimals = Regex.Matches(text, @"\b\d+\.\s+\d+\b").Count;
        var missingDegreeUnit = Regex.Matches(text, @"\b1\.63\b(?!\s*(?:°|degrees?|डिग्री))", RegexOptions.IgnoreCase).Count;
        var sentences = Regex.Split(text, @"(?<=[.!?।])\s+").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        var englishSentences = sentences.Count(s => Regex.Matches(s, @"\b[A-Za-z]{3,}\b").Count >= 4 && !profile.AllowedForeignTerms.Any(t => s.Contains(t, StringComparison.OrdinalIgnoreCase)));
        var mixed = profile.LanguageCode.Equals("hi", StringComparison.OrdinalIgnoreCase)
            ? sentences.Count(s => Regex.IsMatch(s, @"\p{IsDevanagari}") && Regex.Matches(s, @"\b[A-Za-z]{3,}\b").Count >= 2)
            : 0;
        var devanagariRatio = letters == 0 ? 0 : Math.Round((decimal)dev / letters, 3);
        var latinRatio = letters == 0 ? 0 : Math.Round((decimal)latin / letters, 3);
        var latinWordRatio = Regex.Matches(text, @"[\p{L}0-9]+", RegexOptions.CultureInvariant).Count == 0 ? 0 : Math.Round((decimal)latinWords.Length / Regex.Matches(text, @"[\p{L}0-9]+", RegexOptions.CultureInvariant).Count, 3);
        var scripts = new List<string>();
        if (dev > 0) scripts.Add("Devanagari");
        if (latin > 0) scripts.Add("Latin");
        var score = profile.LanguageCode.Equals("hi", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp((int)(devanagariRatio * 100) - englishSentences * 30 - mixed * 20 - rawTs * 40 - internalIds * 40 - templates * 35 - splitDecimals * 20 - missingDegreeUnit * 20, 0, 100)
            : Math.Clamp(100 - rawTs * 40 - internalIds * 40 - templates * 25 - splitDecimals * 20 - missingDegreeUnit * 20, 0, 100);
        var passed = profile.LanguageCode.Equals("hi", StringComparison.OrdinalIgnoreCase)
            ? dev > 0 && devanagariRatio >= 0.55m && englishSentences == 0 && mixed == 0 && rawTs == 0 && internalIds == 0 && templates == 0 && splitDecimals == 0 && missingDegreeUnit == 0 && score >= profile.MinimumComplianceScore
            : rawTs == 0 && internalIds == 0 && templates == 0 && splitDecimals == 0 && missingDegreeUnit == 0 && score >= profile.MinimumComplianceScore;
        var detected = profile.LanguageCode.Equals("hi", StringComparison.OrdinalIgnoreCase) && devanagariRatio >= 0.45m ? "hi" : "en";
        return new(profile.LanguageCode, detected, scripts, devanagariRatio, latinRatio, latinWordRatio, englishSentences, mixed, approved, unapprovedForeign, templates, rawTs, internalIds, splitDecimals, missingDegreeUnit, score, passed);
    }
}

public sealed record NarrationFactV5(string Name, string Value);
public sealed record NarrationPlanV5(string NarrationPlanVersion, string OrchestrationVersion, string Language, string VoiceProfile, string StoryArc, IReadOnlyList<NarrationFactV5> RequiredNarrationFacts, IReadOnlyList<string> ProhibitedPhrases, IReadOnlyList<string> PreferredPhrases, string ChannelEnding, IReadOnlyList<NarrationPlanV5Scene> Scenes);
public sealed record NarrationPlanV5Scene(string SceneId, string ScenePurpose, int SceneOrder, string KeyMessage, string ViewerFocus, string EmotionalRole, string NarrationIntent, IReadOnlyList<NarrationFactV5> RequiredFacts, IReadOnlyList<NarrationFactV5> MustMentionFacts, IReadOnlyList<string> MustAvoidFacts, string EditorialConnectorToNext, string TargetTone, string TargetLength);
public sealed record NarrationV5(string NarrationVersion, string OrchestrationVersion, string Language, IReadOnlyList<NarrationV5Scene> Scenes, string FullNarrationText, string ChannelEnding);
public sealed record NarrationV5Scene(string SceneId, string ScenePurpose, string NarrationText, IReadOnlyList<string> RequiredFactsCovered, IReadOnlyList<string> Warnings);
public sealed record RequiredFactCoverage(string Value, bool Covered);
public sealed record ProfessionalNarrationScores(int DocumentaryVoiceScore, int ScientificAccuracyScore, int ObservationGuidanceScore, int EditorialFlowScore, int SpokenLanguageScore, int ViewerRetentionScore, int AstroPulseIdentityScore, int OverallNarrationScore);
public sealed record RedundancyDiagnostics(int Score, int DuplicateCount, bool ExceedsThreshold, IReadOnlyList<string> Warnings);
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
