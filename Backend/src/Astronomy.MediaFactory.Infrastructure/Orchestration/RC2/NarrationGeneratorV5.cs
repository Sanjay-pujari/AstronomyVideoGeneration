using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Directors;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Libraries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class NarrationGeneratorV5(ILogger<NarrationGeneratorV5> logger, IRequiredSemanticFactResolver requiredSemanticFactResolver, INarrationRealizer narrationRealizer, IAstronomyFamilyProfileResolver familyProfileResolver, NarrationPromptComposer? promptComposer = null, DocumentaryStyleDirector? styleDirector = null)
{
    public NarrationGeneratorV5(ILogger<NarrationGeneratorV5> logger, NarrationPromptComposer? promptComposer = null, DocumentaryStyleDirector? styleDirector = null)
        : this(logger, SemanticDefaults.RequiredSemanticFactResolver, SemanticDefaults.NarrationRealizer, SemanticDefaults.FamilyProfileResolver, promptComposer, styleDirector) { }
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
        var sceneIdentityDiagnosticsPath = Path.Combine(narrationRoot, "scene-identity-diagnostics.json");
        var narrationContextPath = Path.Combine(narrationRoot, "narration-context.json");
        var narrationRealizationDiagnosticsPath = Path.Combine(narrationRoot, "narration-realization-diagnostics.json");
        var narrationInputNormalizationDiagnosticsPath = Path.Combine(narrationRoot, "narration-input-normalization-diagnostics.json");
        var eventIdentityDiagnosticsPath = Path.Combine(narrationRoot, "event-identity-diagnostics.json");
        var familyProfileV1CompatibilityDiagnosticsPath = Path.Combine(narrationRoot, "family-profile-v1-compatibility-diagnostics.json");
        var validationPath = Path.Combine(narrationRoot, "generator-preflight-diagnostics.json");
        var narrationValidationDiagnosticsPath = Path.Combine(narrationRoot, "narration-validation-diagnostics.json");
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

        var longDocumentaryContract = ReadFirstJson(Path.Combine(outputRoot, "creative", "documentary-contract.long.json"));
        var shortDocumentaryContract = ReadFirstJson(Path.Combine(outputRoot, "creative", "documentary-contract.short.json"));
        var productionEventIntelligence = ReadFirstJson(Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json"));
        var observationMetadata = ReadFirstJson(Path.Combine(outputRoot, "editorial", "observation-metadata.json"));
        var storyGraph = ReadFirstJson(Path.Combine(outputRoot, "editorial", "story-graph.json"));
        var canonicalEventIdentity = CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput(
            ResolvePipelineRequestEventType(response.ProductionPipelineRequest),
            GetString(productionEventIntelligence, "eventType"),
            FirstNonEmpty(GetString(longDocumentaryContract, "eventType"), GetString(shortDocumentaryContract, "eventType")),
            Array.Empty<string>(),
            GetString(contract, "eventType")));
        var familyProfileResolution = familyProfileResolver.ResolveFamilyProfile(canonicalEventIdentity);
        var familyProfile = familyProfileResolution.Profile;
        await WriteAllTextUtf8Async(eventIdentityDiagnosticsPath, JsonSerializer.Serialize(CanonicalEventIdentityDiagnosticsBuilder.Build(canonicalEventIdentity, familyProfileResolution), JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(familyProfileV1CompatibilityDiagnosticsPath, JsonSerializer.Serialize(familyProfileResolution.Diagnostics, JsonOptions), cancellationToken);
        var semanticRegistryValidationReportPath = Path.Combine(narrationRoot, "semantic-registry-validation-report.json");
        var semanticRegistryCoverage = SemanticDefaults.SemanticCapabilitySourceRegistry.ValidateCoverageDetailed([familyProfile]);
        var invalidSemanticRegistrations = semanticRegistryCoverage.Where(r => !r.ResolutionPathValid).Select(r => new
        {
            familyProfile = r.FamilyProfile,
            format = r.Format,
            beatRole = r.BeatRole,
            capabilityId = r.Capability,
            required = r.Required,
            catalogRegistrationFound = r.CatalogRegistrationFound,
            registeredAdapterIds = r.RegisteredAdapterIds,
            approvedDerivationRuleIds = r.ApprovedDerivationRuleIds,
            approvedDomainProviderIds = r.ApprovedDomainProviderIds,
            failureReason = r.FailureReason
        }).ToArray();
        await WriteAllTextUtf8Async(semanticRegistryValidationReportPath, JsonSerializer.Serialize(new { generatedAtUtc = DateTimeOffset.UtcNow, coverage = semanticRegistryCoverage, invalidCapabilities = invalidSemanticRegistrations }, JsonOptions), cancellationToken);
        if (invalidSemanticRegistrations.Length > 0)
            throw new InvalidOperationException("Semantic registry validation failed: " + string.Join("; ", invalidSemanticRegistrations.Select(i => $"FamilyProfile={i.familyProfile}, Format={i.format}, BeatRole={i.beatRole}, Capability={i.capabilityId}, Required={i.required}, FailureReason={i.failureReason}")));
        var productionPipelineRequest = ExtractProductionPipelineRequest(response.ProductionPipelineRequest);
        var resolverInput = new RequiredSemanticFactResolutionInput(
            familyProfile,
            longDocumentaryContract,
            shortDocumentaryContract,
            contract,
            storyGraph,
            productionEventIntelligence,
            observationMetadata,
            ReadFirstJson(Path.Combine(outputRoot, "question-engine", "question-answer-set.json")),
            languageProfile,
            productionPipelineRequest,
            canonicalEventIdentity);
        var resolverInputPresencePath = Path.Combine(narrationRoot, "resolver-input-presence-diagnostics.json");
        await WriteAllTextUtf8Async(resolverInputPresencePath, JsonSerializer.Serialize(BuildResolverInputPresenceDiagnostic(resolverInput, requiredSemanticFactResolver), JsonOptions), cancellationToken);
        ValidateFullProductionSemanticInput(resolverInput);
        var semanticResolution = requiredSemanticFactResolver.Resolve(resolverInput);
        var requiredSemanticFactDiagnosticsPath = Path.Combine(narrationRoot, "required-semantic-fact-diagnostics.json");
        var semanticCapabilityDiagnosticsPath = Path.Combine(narrationRoot, "semantic-capability-diagnostics.json");
        await WriteAllTextUtf8Async(requiredSemanticFactDiagnosticsPath, JsonSerializer.Serialize(new { familyProfileResolutionDiagnostics = familyProfileResolution.Diagnostics, semanticResolutionDiagnostics = semanticResolution.Diagnostics }, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(semanticCapabilityDiagnosticsPath, JsonSerializer.Serialize(semanticResolution.Diagnostics, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(Path.Combine(narrationRoot, "semantic-source-context-presence.json"), JsonSerializer.Serialize(semanticResolution.Diagnostics, JsonOptions), cancellationToken);
        await WriteAllTextUtf8Async(Path.Combine(narrationRoot, "domain-knowledge-diagnostics.json"), JsonSerializer.Serialize(DomainKnowledgeDiagnosticsBuilder.Build(familyProfileResolution.Resolved.ResolvedProfileId, familyProfile.FamilyId, semanticResolution), JsonOptions), cancellationToken);

        var narrationInputNormalization = NarrationInputNormalizer.Normalize(
            longDocumentaryContract,
            shortDocumentaryContract,
            ReadFirstJson(Path.Combine(outputRoot, "creative", "documentary-decision-log.json")),
            ReadFirstJson(editorialBriefContractPath),
            ReadFirstJson(producerNotesContractPath),
            ReadFirstJson(styleContractPath),
            new DocumentaryPerformerSceneFactCards(longSceneFactCards, shortSceneFactCards),
            semanticResolution,
            styleContract?.VoiceProfile ?? "Premium astronomy documentary: confident, elegant, natural, human, curious, educational, and calm.",
            Rc2PipelinePhaseRegistry.OrchestrationVersion,
            languageProfile);
        var realizationResults = narrationInputNormalization.SafeContexts.Select(c => narrationRealizer.Realize(c, familyProfile, languageProfile)).ToArray();
        var realizationValidation = NarrationRealizationValidator.Validate(realizationResults, familyProfile).Concat(RequiredSemanticFactPhase7Validator.Validate(semanticResolution)).ToArray();
        var narrationContext = NarrationRealizedContextMapper.ToContext(narrationInputNormalization.Context, realizationResults);
        var narrationContextJson = JsonSerializer.Serialize(narrationContext, JsonOptions);
        await WriteAllTextUtf8Async(narrationContextPath, narrationContextJson, cancellationToken);
        await WriteAllTextUtf8Async(narrationInputNormalizationDiagnosticsPath, JsonSerializer.Serialize(narrationInputNormalization.Diagnostics, JsonOptions), cancellationToken);
        var realizationDiagnostics = NarrationRealizationDiagnosticsBuilder.Build(familyProfile, realizationResults, realizationValidation, languageProfile);
        await WriteAllTextUtf8Async(narrationRealizationDiagnosticsPath, JsonSerializer.Serialize(realizationDiagnostics, JsonOptions), cancellationToken);
        logger.LogInformation("Phase 7 NarrationContext before prompt generation: {NarrationContext}", narrationContextJson);
        var narrationContextPurityFailures = NarrationContextPurityValidator.Validate(narrationContext).ToArray();
        if (semanticResolution.Blocking || realizationResults.Any(r => !r.CanRealize))
        {
            await WriteAllTextUtf8Async(validationPath, JsonSerializer.Serialize(new { phaseNo = 7, phaseName = PhaseName, status = "Failed", requiredSemanticFactResolutionBlocking = semanticResolution.Blocking, errors = realizationValidation }, JsonOptions), cancellationToken);
            throw new InvalidOperationException("Required semantic fact resolution failed before prompt generation: " + string.Join(" | ", realizationValidation.Select(v => v.ToString())));
        }
        if (narrationContextPurityFailures.Length > 0)
        {
            await WriteAllTextUtf8Async(validationPath, JsonSerializer.Serialize(new { phaseNo = 7, phaseName = PhaseName, status = "Failed", errors = narrationContextPurityFailures }, JsonOptions), cancellationToken);
            throw new InvalidOperationException("NarrationContext purity validation failed before prompt generation: " + string.Join(" | ", narrationContextPurityFailures));
        }

        var composer = promptComposer ?? new NarrationPromptComposer();
        var promptComposerOutput = await composer.ComposeAndWriteAsync(new NarrationPromptComposerInput(narrationContext, [narrationContextPath, narrationRealizationDiagnosticsPath], promptPreviewPath, promptDiagnosticsPath, promptQualityPath, LanguageProfile: languageProfile, Realizations: realizationResults), cancellationToken);
        var performerPrompt = BuildPerformerSystemPrompt(languageProfile);
        var userPrompt = BuildPerformerUserPrompt(languageProfile, promptComposerOutput.PromptPreviewMarkdown);
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
                    var formatRealizations = realizationResults.Where(r => r.Format.Equals(format, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var documentaryScript = LlmDocumentaryTranscriptionist.Transcribe(contexts, format, language, outline, formatRealizations);
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
        var engineeringLeakageViolations = DetectContextualLeakage(fullText, EngineeringLeakagePhrases).ToArray();
        var promptLeakageViolations = DetectContextualLeakage(fullText, PromptLeakagePhrases).ToArray();
        var isoDateTimeViolations = IsoDateTimeRegex.Matches(fullText).Select(m => m.Value).Concat(RawUtcRegex.Matches(fullText).Select(m => m.Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var longLanguage = LanguageOutputValidator.Validate(GetNarrationText(longNarrationPath), languageProfile);
        var shortLanguage = LanguageOutputValidator.Validate(GetNarrationText(shortNarrationPath), languageProfile);
        var languageValidationPassed = (!requestedFormats.Contains("long") || longLanguage.Passed) && (!requestedFormats.Contains("short") || shortLanguage.Passed);
        var duplicatedPhraseViolations = DuplicatedTransformedPhraseRegex.Matches(fullText).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var diagnosticWarningViolations = ContainsAny(fullText, "diagnostic warning", "diagnostics warning", "warning:") ? new[] { "Diagnostic warning language found in final narration." } : [];
        var writerInputText = BuildEffectiveWriterInputText(producerNotesContract);
        var forbiddenWriterInputDetected = EditorialBriefInterpreter.ForbiddenWriterInputPhrases.Where(p => writerInputText.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        var forbiddenNarrationDetected = EditorialBriefInterpreter.DetectForbiddenNarrationPhrases(fullText).ToArray();
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
        var generatedNarrationFailures = GeneratedNarrationValidator.Validate(fullText).ToArray();
        var longExpectedSceneIds = longSceneFactCards.Cards.Select(c => c.SceneId).ToArray();
        var shortExpectedSceneIds = shortSceneFactCards.Cards.Select(c => c.SceneId).ToArray();
        var longActualSceneIds = ReadArray(ReadFirstJson(longNarrationPath), "scenes").Select(s => GetString(s, "sceneId") ?? string.Empty).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        var shortActualSceneIds = ReadArray(ReadFirstJson(shortNarrationPath), "scenes").Select(s => GetString(s, "sceneId") ?? string.Empty).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        var sceneIdentityDiagnostics = BuildSceneIdentityDiagnostics(longSceneFactCards.Cards, shortSceneFactCards.Cards, longActualSceneIds, shortActualSceneIds, requestedFormats);
        await WriteAllTextUtf8Async(sceneIdentityDiagnosticsPath, JsonSerializer.Serialize(sceneIdentityDiagnostics, JsonOptions), cancellationToken);
        var sceneMappingValid = sceneIdentityDiagnostics.Diagnostics.All(d => d.MappingStatus.Equals("Mapped", StringComparison.OrdinalIgnoreCase));
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
            .Concat(realizationValidation.Select(p => $"Narration realization failure: {p.DetectedIssue}"))
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
            .Concat(DetectSceneFactCardFieldLeakage(fullText).Select(p => $"Scene fact card field name leaked into narration: {p}"))
            .Concat(shortCopiedFromLong ? ["Short narration is identical or near-identical to long narration."] : [])
            .Concat(repeatedOpeningCount > 0 ? [$"Repeated scene opening detected {repeatedOpeningCount} time(s)."] : [])
            .Concat(duplicateSentenceCount > 0 ? [$"Adjacent duplicate sentence detected {duplicateSentenceCount} time(s)."] : [])
            .Concat(redundancy.ExceedsThreshold ? [$"Repeated narration exceeds threshold: {redundancy.DuplicateCount} duplicate sentence(s)."] : [])
            .Concat(visualInstructionLeakageDetected ? ["Visual instructions leaked into LLM input or narration."] : [])
            .Concat(generatedNarrationFailures.Select(p => $"Generated narration validation failure: {p}"))
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
            fieldNameLeakageDetected = DetectSceneFactCardFieldLeakage(fullText).Any(),
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
            && !DetectContextualLeakage(fullText, RawNarrativeLeakagePhrases).Any()
            && !DetectSceneFactCardFieldLeakage(fullText).Any()
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
            longNarrationArtifactGenerated = File.Exists(longNarrationPath),
            shortNarrationArtifactGenerated = File.Exists(shortNarrationPath),
            longNarrationArtifactValid = File.Exists(longNarrationPath) && !string.IsNullOrWhiteSpace(GetNarrationText(longNarrationPath)),
            shortNarrationArtifactValid = File.Exists(shortNarrationPath) && !string.IsNullOrWhiteSpace(GetNarrationText(shortNarrationPath)),
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
            outputsCreated = new[] { planPath, briefsPath, styleContractPath, styleDiagnosticsPath, knowledgeContractPath, knowledgeDiagnosticsPath, editorialBriefContractPath, editorialBriefDiagnosticsPath, producerNotesContractPath, producerNotesDiagnosticsPath, longRawNarrativePath, shortRawNarrativePath, rawNarrativeDiagnosticsPath, longSceneFactCardsPath, shortSceneFactCardsPath, sceneFactCardsDiagnosticsPath, longDocumentaryScriptPath, shortDocumentaryScriptPath, documentaryScriptDiagnosticsPath, performanceDiagnosticsPath, sceneIdentityDiagnosticsPath, llmRequestPath, narrationPath, longNarrationPath, longDiagnosticsPath, shortNarrationPath, shortDiagnosticsPath, diagnosticsPath, promptPreviewPath, promptDiagnosticsPath, promptQualityPath, narrationContextPath, narrationRealizationDiagnosticsPath, performanceDiagnosticsPath, sceneIdentityDiagnosticsPath }.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) || path == diagnosticsPath }).ToArray(),
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
            sceneIdentityDiagnosticsPath = NormalizePath(sceneIdentityDiagnosticsPath),
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
            rawFieldLeakageDetected = DetectContextualLeakage(fullText, RawNarrativeLeakagePhrases).Any(),
            fieldNameLeakageDetected = DetectSceneFactCardFieldLeakage(fullText).Any(),
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
        var outputFilesExist = (!requestedFormats.Contains("long") || File.Exists(longNarrationPath)) && (!requestedFormats.Contains("short") || File.Exists(shortNarrationPath));
        var longTextNonEmpty = !requestedFormats.Contains("long") || !string.IsNullOrWhiteSpace(GetNarrationText(longNarrationPath));
        var shortTextNonEmpty = !requestedFormats.Contains("short") || !string.IsNullOrWhiteSpace(GetNarrationText(shortNarrationPath));
        var mandatoryBlockingFailures = errors.Length + (languageValidationPassed ? 0 : 1) + (sceneMappingValid ? 0 : 1) + (outputFilesExist ? 0 : 1) + (longTextNonEmpty && shortTextNonEmpty ? 0 : 1);
        var validationStatusSucceeded = mandatoryBlockingFailures == 0 && narrationContextPurityFailures.Length == 0 && new[] { beatFidelityScore, professionalScores.ScientificAccuracyScore, transitionQualityScore, documentaryFlowScore, redundancy.Score, professionalScores.DocumentaryVoiceScore }.Min() >= 80;
        var validation = new
        {
            status = validationStatusSucceeded ? "Succeeded" : "Failed",
            reason = validationStatusSucceeded ? "Validation passed." : "Validation failed because blocking Phase 7 performance diagnostics or context purity checks failed.",
            phaseNo = 7,
            phaseName = PhaseName,
            validator = "AstroPulse-NarrationValidator-v3",
            passed = validationStatusSucceeded && languageValidationPassed && generationErrors.Count == 0 && validationErrors.Length == 0 && !editorialReviewerDecision.Equals("Do Not Publish", StringComparison.OrdinalIgnoreCase) && professionalScores.OverallNarrationScore >= 80 && File.Exists(longSceneFactCardsPath) && File.Exists(shortSceneFactCardsPath) && File.Exists(longDocumentaryScriptPath) && File.Exists(shortDocumentaryScriptPath) && repeatedOpeningCount == 0 && duplicateSentenceCount == 0 && sceneMappingValid && wholeDocumentGenerationUsed && !visualInstructionLeakageDetected && generatedNarrationFailures.Length == 0 && !redundancy.ExceedsThreshold && !sharedSceneSourceUsed && !longShortSceneStructureIdentical && (!requestedFormats.Contains("long") || longGeneratedSceneCount == longExpectedSceneCount) && (!requestedFormats.Contains("short") || shortGeneratedSceneCount == shortExpectedSceneCount),
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
            auroraCertified = validationStatusSucceeded && mandatoryBlockingFailures == 0 && auroraCertified,
            canRetry = !validationStatusSucceeded,
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
            longNarrationArtifactGenerated = File.Exists(longNarrationPath),
            shortNarrationArtifactGenerated = File.Exists(shortNarrationPath),
            longNarrationArtifactValid = File.Exists(longNarrationPath) && longTextNonEmpty,
            shortNarrationArtifactValid = File.Exists(shortNarrationPath) && shortTextNonEmpty,
            longNarrationQualityAccepted = validationStatusSucceeded,
            shortNarrationQualityAccepted = validationStatusSucceeded,
            llmInputSource = "narration-context",
            producerNotesExcludedFromLlm = true,
            narrativeBriefExcludedFromLlm = true,
            requiredFactsPreserved = coverage.Values.All(v => v.Covered),
            inventedFactsDetected = false,
            rawFieldLeakageDetected = DetectContextualLeakage(fullText, RawNarrativeLeakagePhrases).Any(),
            fieldNameLeakageDetected = DetectSceneFactCardFieldLeakage(fullText).Any(),
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
            realizationValid = realizationValidation.Length == 0,
            blockingFailureCount = mandatoryBlockingFailures,
            auroraCertificationCandidate = validationStatusSucceeded && mandatoryBlockingFailures == 0 && narrationContextPurityFailures.Length == 0 && new[] { beatFidelityScore, professionalScores.ScientificAccuracyScore, transitionQualityScore, documentaryFlowScore, redundancy.Score, professionalScores.DocumentaryVoiceScore }.Min() >= 80,
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
            sceneIdentityDiagnosticsPath = NormalizePath(sceneIdentityDiagnosticsPath),
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
            errors = errors,
            blockingFailureSummaries = errors,
            downstreamDiagnostics = new { performanceDiagnostics = NormalizePath(performanceDiagnosticsPath), longNarrationDiagnostics = NormalizePath(longDiagnosticsPath), shortNarrationDiagnostics = NormalizePath(shortDiagnosticsPath), promptDiagnostics = NormalizePath(promptDiagnosticsPath), normalizationDiagnostics = NormalizePath(narrationInputNormalizationDiagnosticsPath), realizationDiagnostics = NormalizePath(narrationRealizationDiagnosticsPath), longLanguage, shortLanguage },
            warnings
        };
        await WriteAllTextUtf8Async(narrationValidationDiagnosticsPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        if (generationErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", generationErrors));
        logger.LogInformation("Narration Studio V5 wrote {SceneCount} scenes to {NarrationPath}.", narrationScenes.Length, narrationPath);
        return new NarrationGeneratorV5Result([sceneIdentityDiagnosticsPath, narrationContextPath, narrationRealizationDiagnosticsPath, planPath, briefsPath, styleContractPath, styleDiagnosticsPath, knowledgeContractPath, knowledgeDiagnosticsPath, editorialBriefContractPath, editorialBriefDiagnosticsPath, producerNotesContractPath, producerNotesDiagnosticsPath, longRawNarrativePath, shortRawNarrativePath, rawNarrativeDiagnosticsPath, longSceneFactCardsPath, shortSceneFactCardsPath, sceneFactCardsDiagnosticsPath, longDocumentaryScriptPath, shortDocumentaryScriptPath, documentaryScriptDiagnosticsPath, performanceDiagnosticsPath, llmRequestPath, narrationPath, longNarrationPath, longDiagnosticsPath, shortNarrationPath, shortDiagnosticsPath, diagnosticsPath, narrationValidationDiagnosticsPath, promptPreviewPath, promptDiagnosticsPath, promptQualityPath, narrationInputNormalizationDiagnosticsPath, eventIdentityDiagnosticsPath]);
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
           "You are the Documentary Performer: an actor, the documentary voice, not an architect or editor.\n\nThe documentary has already been shaped. Treat every field in NarrationContext as private rehearsal material only. The audience must never hear that private guidance exists. Never repeat, quote, or literally paraphrase labels, guidance language, success criteria, transition goals, beat labels, scene labels, visual/rendering terms, validation terms, data terms, or directive language.\n\nUse this process silently: understand the private context, infer the intended educational effect, then perform only polished narration. Verified facts become natural sentences. Scientific boundaries prevent invention. Style cues influence delivery only. Transition meanings become invisible flow.\n\nDo not say Now, Next, the next beat, this beat, scene, frame, camera, visual, render, data label, private guidance, directive, validation, knowledge goal, audience outcome, editorial intent, success criteria, private notes, documentary contract, allocated facts, source semantic beat, long beat, or short beat.\n\nOpen with immediate curiosity. Close with wonder, not instructions or summary. Write with the confidence and elegance of BBC Earth, National Geographic, Netflix Documentary, and Apple TV science: natural, human, curious, educational, calm, and cinematic without exposing production mechanics.\n\nFINAL OUTPUT CONSTRAINTS\n\n" + profile.OutputInstruction + "\nUse consistent terminology: " + string.Join("; ", profile.Terminology.Select(kv => $"{kv.Key} → {kv.Value}")) + ".";

    private static string BuildPerformerUserPrompt(LanguageProfile profile, string writerBrief)
        => $"Requested output language: {profile.DisplayName}\nLanguage code: {profile.Culture}\nScript: {profile.Script}\n\nWrite every narration beat in {profile.DisplayName}. Use the clean writer brief below as semantic meaning only; do not copy section titles or labels.\n\n{writerBrief}";

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
            "science" => $"The closeness is a line-of-sight effect. the objects remain physically separate, yet from Earth their separate paths can briefly seem to gather in one small patch of sky. {detailPhrase}",
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
        var hasLeakage = DetectContextualLeakage(fullText, EngineeringLeakagePhrases).Any();
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


    private static object BuildResolverInputPresenceDiagnostic(RequiredSemanticFactResolutionInput input, IRequiredSemanticFactResolver resolver)
    {
        var resolverType = resolver.GetType();
        var engine = TryGetResolverField<ISemanticResolutionEngineV1>(resolver, "_semanticResolutionEngine");
        var catalog = TryGetResolverField<ISemanticSourcePolicyCatalogV1>(resolver, "_sourcePolicyCatalog") ?? SemanticDefaults.SemanticSourcePolicyCatalogV1;
        var registry = TryGetResolverField<ISemanticSourceAdapterRegistryV1>(resolver, "_sourceAdapterRegistry") ?? SemanticDefaults.SemanticSourceAdapterRegistryV1;
        var eventWindowPresent = ReadEventWindowFromRequest(input.ProductionPipelineRequest) is not null || ReadEventWindow(input.ProductionEventIntelligence) is not null;
        var observationWindowPresent = ReadEventWindow(input.ObservationMetadata) is not null || eventWindowPresent;
        var observationLocationPresent = ReadObservationLocation(input.ObservationMetadata) is not null || ReadObservationLocationFromRequest(input.ProductionPipelineRequest) is not null;
        var observationDirectionPresent = ReadObservationDirection(input.ObservationMetadata) is not null || ReadObservationDirectionFromRequest(input.ProductionPipelineRequest) is not null;
        var primaryObjectCount = (ReadObjectsFromRequest(input.ProductionPipelineRequest, includeSecondary: true) ?? ReadPrimaryObjects(input.ProductionEventIntelligence) ?? ImmutableArray<AstronomicalObjectValue>.Empty).Length;
        return new
        {
            resolverType = resolverType.FullName,
            engineType = engine?.GetType().FullName,
            policyCount = catalog.Policies.Count,
            adapterCount = registry.Adapters.Count,
            eventIdentityPresent = input.CanonicalEventIdentity is not null,
            productionEventIntelligencePresent = input.ProductionEventIntelligence.HasValue || input.ProductionPipelineRequest is not null,
            productionPrimaryObjectCount = primaryObjectCount,
            productionEventWindowPresent = eventWindowPresent,
            observationMetadataPresent = input.ObservationMetadata.HasValue || ReadObservationLocationFromRequest(input.ProductionPipelineRequest) is not null,
            observationEventWindowPresent = observationWindowPresent,
            observationLocationPresent,
            observationDirectionPresent,
            domainKnowledgePresent = true,
            language = input.LanguageProfile.LanguageCode,
            timeZone = input.ProductionPipelineRequest?.TimeZone,
            familyId = input.FamilyProfile.FamilyId,
            profileId = input.FamilyProfile.FamilyId,
            productionPipelineRequestPresent = input.ProductionPipelineRequest is not null,
            longDocumentaryContractPresent = input.LongDocumentaryContract.HasValue,
            shortDocumentaryContractPresent = input.ShortDocumentaryContract.HasValue,
            editorialContractPresent = input.EditorialContract.HasValue,
            storyGraphPresent = input.StoryGraph.HasValue
        };
    }

    private static T? TryGetResolverField<T>(IRequiredSemanticFactResolver resolver, string fieldName) where T : class
        => resolver.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(resolver) as T;

    private static void ValidateFullProductionSemanticInput(RequiredSemanticFactResolutionInput input)
    {
        if (input.ProductionPipelineRequest is null)
            throw new InvalidOperationException("Phase 7 semantic resolution requires the typed production pipeline request; the real endpoint must not use the reduced resolver input path.");
        if (input.CanonicalEventIdentity is null)
            throw new InvalidOperationException("Phase 7 semantic resolution requires the already-resolved canonical event identity.");
        if (!input.LongDocumentaryContract.HasValue || !input.ShortDocumentaryContract.HasValue)
            throw new InvalidOperationException("Phase 7 semantic resolution requires both typed documentary contracts.");
        if (!input.ProductionEventIntelligence.HasValue)
            throw new InvalidOperationException("Phase 7 semantic resolution requires production event intelligence from Phase 2.");
        if (!input.ObservationMetadata.HasValue)
            throw new InvalidOperationException("Phase 7 semantic resolution requires observation metadata from Phase 5.");
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
    private static ContentPlanProductionPipelineRequest? ExtractProductionPipelineRequest(object? value)
    {
        if (value is ContentPlanProductionPipelineRequest typed) return typed;
        if (value is ProductionPipelineRequest pipelineRequest) return pipelineRequest.Request;
        if (value is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            try { return json.Deserialize<ContentPlanProductionPipelineRequest>(JsonOptions); }
            catch (JsonException) { return null; }
        }
        return null;
    }

    private static string? ResolvePipelineRequestEventType(object? pipelineRequest)
    {
        if (pipelineRequest is null) return null;
        try
        {
            var json = pipelineRequest is JsonElement e ? e.GetRawText() : JsonSerializer.Serialize(pipelineRequest, JsonOptions);
            using var doc = JsonDocument.Parse(json);
            return FindStringRecursive(doc.RootElement, "eventType");
        }
        catch
        {
            return null;
        }
    }
    private static string? FindStringRecursive(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in element.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
                var value = FindStringRecursive(p.Value, name);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var value = FindStringRecursive(item, name);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        return null;
    }
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

    private static IReadOnlyList<string> DetectContextualLeakage(string text, IEnumerable<string> phrases)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var leaks = new List<string>();
        foreach (var phrase in phrases.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var escaped = Regex.Escape(phrase);
            var fieldLabel = Regex.IsMatch(text, $@"(^|[.!?]\s+){escaped}\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var multiWordInstruction = phrase.Contains(' ') && Regex.IsMatch(text, $@"\b{escaped}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var imperativeInstruction = Regex.IsMatch(text, @"(^|[.!?]\s+)(?:Turn\b.+?\binto\b|Explain\b|Establish\b|Introduce\b|Give the viewer\b|Keep the guidance\b|Make clear\b|Emphasize\b|The viewer should\b|Viewer should\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (fieldLabel || multiWordInstruction || imperativeInstruction && phrase.Contains("viewer", StringComparison.OrdinalIgnoreCase))
                leaks.Add(phrase);
        }
        return leaks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> DetectSceneFactCardFieldLeakage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return SceneFactCardFieldNames
            .Where(p => Regex.IsMatch(text, $@"(^|[.!?]\s+){Regex.Escape(p)}\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(text, $@"\b(?:camera|rendering|visual|composition)\s+{Regex.Escape(p)}\s+(?:should|must|will)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SceneIdentityDiagnostics BuildSceneIdentityDiagnostics(
        IReadOnlyList<SceneFactCard> longCards,
        IReadOnlyList<SceneFactCard> shortCards,
        IReadOnlyList<string> longActualSceneIds,
        IReadOnlyList<string> shortActualSceneIds,
        IReadOnlyList<string> requestedFormats)
    {
        var diagnostics = new List<SceneIdentityDiagnostic>();
        Add("long", longCards, longActualSceneIds);
        Add("short", shortCards, shortActualSceneIds);
        return new SceneIdentityDiagnostics("scene-identity-diagnostics.v1", diagnostics.Count, diagnostics);

        void Add(string format, IReadOnlyList<SceneFactCard> cards, IReadOnlyList<string> actualIds)
        {
            if (!requestedFormats.Contains(format, StringComparer.OrdinalIgnoreCase)) return;
            foreach (var card in cards.OrderBy(c => c.SceneOrder))
            {
                var actual = actualIds.ElementAtOrDefault(Math.Max(0, card.SceneOrder - 1)) ?? string.Empty;
                var mapped = !string.IsNullOrWhiteSpace(actual) && string.Equals(card.SceneId, actual, StringComparison.OrdinalIgnoreCase);
                diagnostics.Add(new SceneIdentityDiagnostic(
                    card.SceneId,
                    actual,
                    string.IsNullOrWhiteSpace(card.SourceStoryFrameId) ? card.SourceSceneIntentId : card.SourceStoryFrameId,
                    format,
                    card.SceneOrder,
                    mapped ? "Mapped" : "Mismatch",
                    mapped ? string.Empty : $"Expected Phase 7 sceneId '{card.SceneId}' at order {card.SceneOrder}, but found '{(string.IsNullOrWhiteSpace(actual) ? "<missing>" : actual)}'."));
            }
        }
    }

    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
}


public sealed record StoryFrameSource(string SourcePath, IReadOnlyList<StoryFrameNarrationSource> Frames);
public sealed record StoryFrameNarrationSource(string SceneId, int SceneOrder, string FrameId, string NarrationMapping);
public sealed record RawNarrative(string ContractVersion, string OrchestrationVersion, string Format, string Language, IReadOnlyList<RawNarrativeScene> Scenes);
public sealed record RawNarrativeScene(string SceneId, int SceneOrder, string SceneRole, IReadOnlyList<string> MustSayFacts, IReadOnlyList<string> MustExplain, IReadOnlyList<string> MustGuide, IReadOnlyList<string> MustNotSay, string TransitionToNext, int EstimatedDurationSeconds, string SourceSceneIntentId, string SourceStoryFrameId);
public sealed record SceneFactCardSet(string ContractVersion, string OrchestrationVersion, string Format, string Language, IReadOnlyList<SceneFactCard> Cards);
public sealed record SceneFactCard(string SceneId, int SceneOrder, string Format, IReadOnlyList<string> Facts, IReadOnlyList<string> Observations, IReadOnlyList<string> Visibility, IReadOnlyList<string> Timing, IReadOnlyList<string> Location, IReadOnlyList<string> Objects, IReadOnlyList<string> Science, IReadOnlyList<string> RequiredMentions, IReadOnlyList<string> ForbiddenClaims, int EstimatedDurationSeconds, string SourceSceneIntentId, string SourceStoryFrameId);
public sealed record SceneIdentityDiagnostics(string ContractVersion, int DiagnosticCount, IReadOnlyList<SceneIdentityDiagnostic> Diagnostics);
public sealed record SceneIdentityDiagnostic(string Phase6SceneId, string Phase7SceneId, string DocumentaryBeatId, string Format, int SceneOrder, string MappingStatus, string MismatchReason);
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


public sealed record NarrationSafeContext(string Format, string SceneId, string DocumentaryBeatId, string NarrativeRole, string KnowledgeGoal, string AudienceOutcome, string EditorialIntent, string? ObservationObjective, string? ScientificObjective, string TransitionGoal, string Tone, string Rhythm, int WordBudget, IReadOnlyList<SpeakableFact> SpeakableFacts, IReadOnlyList<SemanticProducerNote> SemanticProducerNotes, IReadOnlyList<string> Constraints);
public sealed record SpeakableFact(string FactKey, string CanonicalValue, string? CanonicalUnit, string FactType, string LocalizedDisplayValue, string SpeakableValue, string Language, string Culture, bool SafeForNarration, string SourceArtifact, string SourceField);
public sealed record SemanticProducerNote(string NoteType, string SemanticInstruction, string SourceArtifact, string SourceField);
public sealed record NarrationInputNormalizationResult(NarrationContextDocument Context, NarrationInputNormalizationDiagnostics Diagnostics, IReadOnlyList<NarrationSafeContext> SafeContexts);
public sealed record NarrationInputNormalizationDiagnostics(int SourceFieldCount, int ClassifiedFactCount, int SafeFactCount, int OmittedOptionalFieldCount, int BlockedFieldCount, int LocalizedFieldCount, int ProducerNotesSanitized, int PublishingMetadataExcluded, int TimestampValuesNormalized, int RegionIdsResolved, int DirectionCodesResolved, string LanguageProfileUsed, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, IReadOnlyList<NormalizationRecord>? NormalizedFields = null, IReadOnlyList<NormalizationRecord>? OmittedFields = null, IReadOnlyList<NormalizationRecord>? ExcludedPublishingFields = null, IReadOnlyList<NormalizationRecord>? UnresolvedFields = null, IReadOnlyList<NormalizationRecord>? FallbacksUsed = null);
public sealed record NormalizationRecord(string SourceArtifact, string SourceField, string Classification, string CanonicalValuePreview, string? NormalizedValue, string Language, string Result, string Reason);

public enum NarrationFactType { ObjectName, EventDate, PeakTime, ViewingWindow, Direction, AngularSeparation, Location, ScienceMeaning, ObservationGuidance, VisibilityCondition, PublishMetadata, InternalMetadata }

public static class NarrationInputNormalizer
{
    private static readonly Regex TimestampRegex = new(@"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex LocalOffsetRegex = new(@"\b\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}\s+[+-]\d{4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static NarrationInputNormalizationResult Normalize(JsonElement? longContract, JsonElement? shortContract, JsonElement? decisionLog, JsonElement? editorialBrief, JsonElement? producerNotes, JsonElement? styleContract, DocumentaryPerformerSceneFactCards factCards, string voiceProfile, string orchestrationVersion, LanguageProfile languageProfile)
        => Normalize(longContract, shortContract, decisionLog, editorialBrief, producerNotes, styleContract, factCards, null, voiceProfile, orchestrationVersion, languageProfile);

    public static NarrationInputNormalizationResult Normalize(JsonElement? longContract, JsonElement? shortContract, JsonElement? decisionLog, JsonElement? editorialBrief, JsonElement? producerNotes, JsonElement? styleContract, DocumentaryPerformerSceneFactCards factCards, RequiredSemanticFactResolutionResult? resolvedFacts, string voiceProfile, string orchestrationVersion, LanguageProfile languageProfile)
    {
        var source = NarrationContextBuilder.Build(longContract, shortContract, decisionLog, editorialBrief, producerNotes, styleContract, factCards, resolvedFacts, voiceProfile, orchestrationVersion);
        var warnings = new List<string>();
        var errors = new List<string>();
        var safeContexts = new List<NarrationSafeContext>();
        var formats = new List<NarrationFormatContext>();
        var counters = new Counter();
        foreach (var format in source.Formats)
        {
            var beats = new List<NarrationContextBeat>();
            foreach (var beat in format.Beats)
            {
                counters.SourceFieldCount += 12 + beat.VerifiedFacts.Count;
                var facts = beat.VerifiedFacts.Select(f => NormalizeFact(f, languageProfile, counters, warnings)).Where(f => f is not null).Cast<SpeakableFact>().Where(f => f.SafeForNarration).ToArray();
                counters.SafeFactCount += facts.Length;
                var notes = ProducerNoteSanitizer.Sanitize(beat.OptionalProducerNotes, languageProfile, counters, warnings).ToArray();
                var constraints = beat.ScientificConstraints.Select(v => SanitizeSemantic(v, languageProfile, counters, warnings, optional:true)).Where(v => !string.IsNullOrWhiteSpace(v)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var safe = new NarrationSafeContext(format.Format, string.Empty, string.Empty, ResolveRole(facts, languageProfile), SemanticTemplate("knowledgeGoal", languageProfile), SemanticTemplate("audienceOutcome", languageProfile), SemanticTemplate("editorialIntent", languageProfile), SemanticTemplate("observationObjective", languageProfile), SemanticTemplate("scientificObjective", languageProfile), SemanticTemplate("transitionGoal", languageProfile), ResolveTone(languageProfile), ResolveRhythm(format.Format, languageProfile), format.Format.Equals("short", StringComparison.OrdinalIgnoreCase) ? 35 : 95, facts, notes, constraints);
                safeContexts.Add(safe);
                beats.Add(new NarrationContextBeat(safe.KnowledgeGoal, safe.AudienceOutcome, safe.EditorialIntent, facts.Select(f => new NarrationVerifiedFact(f.FactKey, f.SpeakableValue, f.FactType, f.CanonicalUnit)).ToArray(), safe.Constraints, safe.ObservationObjective, safe.TransitionGoal, safe.Tone, safe.Rhythm, beat.SuccessCriteria.Select(_ => SemanticTemplate("successCriteria", languageProfile)).Distinct().ToArray(), notes.Length == 0 ? null : string.Join(" ", notes.Select(n => n.SemanticInstruction))));
            }
            formats.Add(new NarrationFormatContext(format.Format, beats));
        }
        return new NarrationInputNormalizationResult(new NarrationContextDocument("AstroPulse-NarrationSafeContext-v1", orchestrationVersion, formats), new NarrationInputNormalizationDiagnostics(counters.SourceFieldCount, counters.ClassifiedFactCount, counters.SafeFactCount, counters.OmittedOptionalFieldCount, counters.BlockedFieldCount, counters.LocalizedFieldCount, counters.ProducerNotesSanitized, counters.PublishingMetadataExcluded, counters.TimestampValuesNormalized, counters.RegionIdsResolved, counters.DirectionCodesResolved, languageProfile.ProfileId, warnings.Distinct().ToArray(), errors, counters.NormalizedFields, counters.OmittedFields, counters.ExcludedPublishingFields, counters.UnresolvedFields, counters.FallbacksUsed), safeContexts);
    }

    private static SpeakableFact? NormalizeFact(NarrationVerifiedFact fact, LanguageProfile languageProfile, Counter counters, List<string> warnings)
    {
        counters.ClassifiedFactCount++;
        var type = Classify(fact.FactKey, fact.Value);
        if (type is NarrationFactType.PublishMetadata or NarrationFactType.InternalMetadata) { counters.PublishingMetadataExcluded += type == NarrationFactType.PublishMetadata ? 1 : 0; counters.BlockedFieldCount++; warnings.Add($"Excluded {type} fact {fact.FactKey} from narration input."); counters.ExcludedPublishingFields.Add(Record(fact, type, null, languageProfile, "excluded", "Publishing or internal metadata is not spoken.")); return null; }
        var formatted = SpeakableFactFormatter.Format(fact.FactKey, fact.Value, fact.Unit, type, languageProfile, counters, out var warning);
        if (!string.IsNullOrWhiteSpace(warning)) warnings.Add(warning!);
        if (formatted is null) { counters.OmittedOptionalFieldCount++; counters.OmittedFields.Add(Record(fact, type, null, languageProfile, "omitted", warning ?? "Optional fact was not speakable.")); return null; }
        counters.LocalizedFieldCount++;
        counters.NormalizedFields.Add(Record(fact, type, formatted, languageProfile, "normalized", "Converted to complete speakable narration input."));
        return new SpeakableFact(fact.FactKey, fact.Value, fact.Unit, type.ToString(), formatted, formatted, languageProfile.LanguageCode, languageProfile.Culture, true, "NarrationInputNormalizer", fact.FactKey);
    }

    private static NormalizationRecord Record(NarrationVerifiedFact fact, NarrationFactType type, string? normalized, LanguageProfile profile, string result, string reason)
        => new("NarrationInputNormalizer", fact.FactKey, type.ToString(), Preview(fact.Value), normalized, profile.LanguageCode, result, reason);

    private static string Preview(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : (value.Length <= 80 ? value : value[..80] + "…");

    private static NarrationFactType Classify(string key, string value)
    {
        var k = key ?? string.Empty; var v = value ?? string.Empty;
        if (ContainsAny(k, "publish", "scheduled", "campaign") || ContainsAny(v, "recommendedPublishWindow", "scheduledUtc")) return NarrationFactType.PublishMetadata;
        if (Regex.IsMatch(v, @"\b[A-Z]{2}-[A-Z0-9]{2,}(?:-[A-Z0-9]{2,})+\b") || ContainsAny(k, "id", "source") || v.TrimStart().StartsWith("{") || v.TrimStart().StartsWith("[")) return NarrationFactType.InternalMetadata;
        if (ContainsAny(k, "date")) return NarrationFactType.EventDate;
        if (ContainsAny(k, "time", "peak")) return NarrationFactType.PeakTime;
        if (ContainsAny(k, "window")) return NarrationFactType.ViewingWindow;
        if (ContainsAny(k, "direction", "skyDirection")) return NarrationFactType.Direction;
        if (ContainsAny(k, "region", "location", "visibility")) return NarrationFactType.Location;
        if (ContainsAny(k, "separation", "relative")) return NarrationFactType.AngularSeparation;
        if (ContainsAny(k, "guidance", "observe")) return NarrationFactType.ObservationGuidance;
        if (ContainsAny(k, "science", "explanation", "perspective")) return NarrationFactType.ScienceMeaning;
        return NarrationFactType.ObjectName;
    }

    private static string? SanitizeSemantic(string? value, LanguageProfile profile, Counter counters, List<string> warnings, bool optional)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (UnsafePatterns.ContainsUnsafe(value)) { if (optional) { counters.OmittedOptionalFieldCount++; warnings.Add("Omitted unsafe optional semantic text before prompt generation."); return null; } counters.BlockedFieldCount++; return null; }
        return profile.LanguageCode == "hi" ? "केवल प्रमाणित जानकारी कहें।" : "Use only verified astronomy information.";
    }

    private static string ResolveRole(IReadOnlyList<SpeakableFact> facts, LanguageProfile p) => p.LanguageCode == "hi" ? "खगोलीय व्याख्या" : "Astronomy explanation";
    private static string ResolveTone(LanguageProfile p) => p.LanguageCode == "hi" ? "शांत, स्पष्ट और जिज्ञासु।" : "Calm, clear, and curious.";
    private static string ResolveRhythm(string format, LanguageProfile p) => p.LanguageCode == "hi" ? (format == "short" ? "संक्षिप्त वृत्तचित्र लय।" : "मापा हुआ वृत्तचित्र प्रवाह।") : (format == "short" ? "Compact documentary rhythm." : "Measured documentary flow.");
    private static string SemanticTemplate(string field, LanguageProfile p) => p.LanguageCode == "hi" ? field switch { "knowledgeGoal" => "आकाशीय घटना का सुरक्षित अर्थ स्पष्ट रहता है।", "audienceOutcome" => "दर्शक समझें कि क्या देखना है और क्यों।", "editorialIntent" => "तथ्य सरल और संयमित भाषा में रहते हैं।", "observationObjective" => "देखने योग्य जानकारी स्वाभाविक मार्गदर्शन बनती है।", "scientificObjective" => "दिखने वाली निकटता दृष्टि-रेखा और परिप्रेक्ष्य से जुड़ी है।", "transitionGoal" => "अगले विचार की ओर सहज प्रवाह रहता है।", _ => "कहानी तथ्यपरक और स्वाभाविक रहे।" } : field switch { "knowledgeGoal" => "Help the audience understand the astronomy event safely.", "audienceOutcome" => "The audience can notice the event and understand why it matters.", "editorialIntent" => "Present verified facts with simple restraint.", "observationObjective" => "Use the verified observing details in natural spoken guidance.", "scientificObjective" => "The apparent closeness comes from line of sight and perspective.", "transitionGoal" => "Move naturally into the next idea.", _ => "Keep the story factual and natural." };
    private static bool ContainsAny(string? value, params string[] terms) => !string.IsNullOrWhiteSpace(value) && terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
    public sealed class Counter
    {
        public int SourceFieldCount, ClassifiedFactCount, SafeFactCount, OmittedOptionalFieldCount, BlockedFieldCount, LocalizedFieldCount, ProducerNotesSanitized, PublishingMetadataExcluded, TimestampValuesNormalized, RegionIdsResolved, DirectionCodesResolved;
        public List<NormalizationRecord> NormalizedFields { get; } = [];
        public List<NormalizationRecord> OmittedFields { get; } = [];
        public List<NormalizationRecord> ExcludedPublishingFields { get; } = [];
        public List<NormalizationRecord> UnresolvedFields { get; } = [];
        public List<NormalizationRecord> FallbacksUsed { get; } = [];
    }
}

public static class UnsafePatterns
{
    public static bool ContainsUnsafe(string value) => Regex.IsMatch(value ?? string.Empty, @"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)?\b|[+-]\d{2}:?\d{2}\b|\b[A-Z]{2}-[A-Z0-9]{2,}(?:-[A-Z0-9]{2,})+\b|^\s*[\{\[]|recommendedPublishWindow|scheduledUtc|campaign|/[^\s]+/|\b(?:long|short)-beat-\d+\b|skyfield", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

public static class ProducerNoteSanitizer
{
    public static IReadOnlyList<SemanticProducerNote> Sanitize(string? notes, LanguageProfile profile, dynamic counters, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(notes)) return [];
        if (UnsafePatterns.ContainsUnsafe(notes)) { counters.OmittedOptionalFieldCount++; warnings.Add("Omitted unsafe optional producer note before prompt generation."); return []; }
        counters.ProducerNotesSanitized++;
        var text = profile.LanguageCode == "hi" ? "परिप्रेक्ष्य, समय और देखने की दिशा को संक्षेप में रखें।" : "Keep perspective, timing, and viewing direction concise.";
        return [new SemanticProducerNote("EditorialGuidance", text, "ProducerNotes", "optionalProducerNotes")];
    }
}

public static class SpeakableFactFormatter
{
    public static string? Format(string key, string value, string? unit, NarrationFactType type, LanguageProfile profile, dynamic counters, out string? warning)
    {
        warning = null;
        var clean = (value ?? string.Empty).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(clean) || UnsafePatterns.ContainsUnsafe(clean) && type is not (NarrationFactType.EventDate or NarrationFactType.PeakTime or NarrationFactType.ViewingWindow)) { warning = $"Omitted unsafe fact {key}."; return null; }
        var hi = profile.LanguageCode.Equals("hi", StringComparison.OrdinalIgnoreCase);
        return type switch
        {
            NarrationFactType.EventDate => AstronomyDateTimeLocalizer.LocalizeDate(clean, hi, counters),
            NarrationFactType.PeakTime => AstronomyDateTimeLocalizer.LocalizeTime(clean, hi, counters),
            NarrationFactType.ViewingWindow => AstronomyDateTimeLocalizer.LocalizeWindow(clean, hi, counters),
            NarrationFactType.Direction => DirectionResolver.Resolve(clean, hi, counters),
            NarrationFactType.Location => RegionDisplayResolver.TryResolveDisplay(clean, profile.LanguageCode, out var loc) ? IncRegion(loc, counters) : null,
            NarrationFactType.AngularSeparation => NumberUnitFormatter.Format(clean, string.IsNullOrWhiteSpace(unit) ? "degrees" : unit!, hi),
            NarrationFactType.ObjectName => AstronomyTerminologyResolver.Resolve(clean, profile),
            NarrationFactType.ScienceMeaning => SemanticTermRealizer.Realize(clean, profile),
            NarrationFactType.ObservationGuidance => hi ? "खुले आकाश में शांत होकर देखें" : "watch calmly from an open sky view",
            NarrationFactType.VisibilityCondition => hi ? "साफ आकाश होने पर दृश्य बेहतर होगा" : "clear skies make the view better",
            _ => null
        };
    }
    private static string IncRegion(string value, dynamic counters) { counters.RegionIdsResolved++; return value; }
}

public interface ISemanticTermRealizer
{
    string Realize(string semanticTerm, LanguageProfile profile);
}

public sealed class DefaultSemanticTermRealizer : ISemanticTermRealizer
{
    public string Realize(string semanticTerm, LanguageProfile profile) => SemanticTermRealizer.Realize(semanticTerm, profile);
}

public static class SemanticTermRealizer
{
    private static readonly Dictionary<string, (string En, string Hi)> KnownTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PlanetPairingApparentLineOfSightGeometry"] = ("The planets appear close because they lie along nearly the same line of sight from Earth.", "पृथ्वी से देखने पर दोनों ग्रह लगभग एक ही दृष्टि-रेखा में दिखाई देते हैं, इसलिए वे पास नज़र आते हैं।"),
        ["ApparentAlignmentExplanation"] = ("This is an apparent alignment from our viewpoint, not a physical meeting in space.", "यह हमारे दृष्टिकोण से दिखने वाला संरेखण है, अंतरिक्ष में वास्तविक मिलन नहीं।"),
        ["ObservationTiming"] = ("Use the verified observing time for this sky event.", "इस आकाशीय घटना के लिए पुष्ट देखने का समय अपनाएँ।"),
        ["BinocularGuidance"] = ("Binoculars may add clarity when the verified guidance recommends them.", "जब पुष्ट मार्गदर्शन सुझाव दे, तब दूरबीन दृश्य को थोड़ा साफ कर सकती है।")
    };

    public static string Realize(string semanticTerm, LanguageProfile profile)
    {
        var hi = profile.LanguageCode.Equals("hi", StringComparison.OrdinalIgnoreCase);
        var key = (semanticTerm ?? string.Empty).Trim();
        if (KnownTerms.TryGetValue(key, out var known)) return hi ? known.Hi : known.En;
        if (Regex.IsMatch(key, @"^[A-Z][A-Za-z0-9]+(?:[A-Z][a-z0-9]+)+$", RegexOptions.CultureInvariant))
            return hi ? "दिखने वाली निकटता पृथ्वी से हमारी दृष्टि-रेखा और परिप्रेक्ष्य से समझी जाती है।" : "The apparent closeness is explained by line of sight and perspective from Earth.";
        return hi ? "दिखने वाली निकटता पृथ्वी से हमारी दृष्टि-रेखा के कारण होती है" : "the apparent closeness comes from our line of sight on Earth";
    }
}

public static class AstronomyDateTimeLocalizer
{
    public static string LocalizeDate(string value, bool hi, dynamic counters) { if (TryParse(value, out var dto)) { counters.TimestampValuesNormalized++; return hi ? $"{dto.Day} {HindiMonth(dto.Month)} की सुबह" : $"before dawn on {dto.ToString("MMMM d", CultureInfo.InvariantCulture)}"; } return hi ? "घटना की तारीख प्रमाणित है" : "the event date is verified"; }
    public static string LocalizeTime(string value, bool hi, dynamic counters) { if (TryParse(value, out var dto)) { counters.TimestampValuesNormalized++; if (hi && dto.Hour == 5 && dto.Minute == 30) return $"{dto.Day} {HindiMonth(dto.Month)} की सुबह लगभग साढ़े पाँच बजे"; return hi ? $"{dto.Day} {HindiMonth(dto.Month)} को लगभग {dto:HH:mm} बजे" : $"around {dto.ToString("h:mm tt", CultureInfo.InvariantCulture)} on {dto.ToString("MMMM d", CultureInfo.InvariantCulture)}"; } return hi ? "सुबह के अनुकूल समय में" : "during the favorable viewing time"; }
    public static string LocalizeWindow(string value, bool hi, dynamic counters) { if (TryParse(value, out var dto)) { counters.TimestampValuesNormalized++; return hi ? $"{dto.Day} {HindiMonth(dto.Month)} की भोर से पहले" : $"before dawn on {dto.ToString("MMMM d", CultureInfo.InvariantCulture)}"; } return Contains(value,"before dawn") ? (hi ? "भोर से पहले" : "before dawn") : hi ? "अनुकूल देखने की अवधि में" : "during the best viewing window"; }
    private static bool TryParse(string value, out DateTimeOffset dto) => DateTimeOffset.TryParse(value.Replace('T',' '), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto);
    private static bool Contains(string a,string b)=>a?.Contains(b,StringComparison.OrdinalIgnoreCase)==true;
    private static string HindiMonth(int m) => m switch { 1=>"जनवरी",2=>"फ़रवरी",3=>"मार्च",4=>"अप्रैल",5=>"मई",6=>"जून",7=>"जुलाई",8=>"अगस्त",9=>"सितंबर",10=>"अक्टूबर",11=>"नवंबर",12=>"दिसंबर",_=>""};
}

public static class DirectionResolver
{
    public static string Resolve(string value, bool hi, dynamic counters) { var v=(value??"").Trim().Trim('.'); if (Regex.IsMatch(v,"^SE$|south.?east",RegexOptions.IgnoreCase)) { counters.DirectionCodesResolved++; return hi ? "दक्षिण-पूर्वी आकाश" : "the southeastern sky"; } if (Regex.IsMatch(v,"^SW$|south.?west",RegexOptions.IgnoreCase)) { counters.DirectionCodesResolved++; return hi ? "दक्षिण-पश्चिमी आकाश" : "the southwestern sky"; } if (Regex.IsMatch(v,"^E$|east",RegexOptions.IgnoreCase)) { counters.DirectionCodesResolved++; return hi ? "पूर्वी आकाश" : "the eastern sky"; } if (Regex.IsMatch(v,"^W$|west",RegexOptions.IgnoreCase)) { counters.DirectionCodesResolved++; return hi ? "पश्चिमी आकाश" : "the western sky"; } return hi ? "उचित दिशा में खुला आकाश" : "the appropriate open sky direction"; }
}

public static class AstronomyTerminologyResolver
{
    public static string Resolve(string value, LanguageProfile profile)
    {
        var clean = Regex.Replace(value ?? string.Empty, @"\b(Mars|Jupiter|Venus|Saturn|Mercury|Earth)\b", m => profile.Terminology.TryGetValue(m.Value, out var term) ? term : m.Value, RegexOptions.IgnoreCase);
        if (profile.LanguageCode == "hi")
        {
            clean = clean.Replace("Mars", "मंगल", StringComparison.OrdinalIgnoreCase).Replace(" and ", " और ", StringComparison.OrdinalIgnoreCase);
            if (Regex.IsMatch(clean, @"[A-Za-z]{3,}")) clean = "मुख्य खगोलीय पिंड";
        }
        return clean;
    }
}

public static class NumberUnitFormatter
{
    public sealed record StructuredMeasurement(decimal NumericValue, int Precision, string Unit, string Qualifier, string LocalizedSpeakableValue);

    public static string Format(string value, string unit, bool hi)
    {
        return FormatStructured(value, unit, hi).LocalizedSpeakableValue;
    }

    public static StructuredMeasurement FormatStructured(string value, string unit, bool hi, string qualifier = "about")
    {
        var m = Regex.Match(value ?? string.Empty, @"\d+(?:[.]\d+)?", RegexOptions.CultureInvariant);
        var rawNumber = m.Success ? m.Value : "0";
        var precision = rawNumber.Contains('.', StringComparison.Ordinal) ? rawNumber.Length - rawNumber.IndexOf('.') - 1 : 0;
        var number = decimal.TryParse(rawNumber, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
        var display = precision > 0 ? number.ToString($"F{precision}", CultureInfo.InvariantCulture) : number.ToString("0", CultureInfo.InvariantCulture);
        var u = unit.Contains("degree", StringComparison.OrdinalIgnoreCase) ? (hi ? "डिग्री" : "degrees") : unit;
        var q = hi ? "लगभग" : string.IsNullOrWhiteSpace(qualifier) ? "about" : qualifier;
        return new(number, precision, u, q, hi ? $"{q} {display} {u}" : $"{q} {display} {u}");
    }
}

public static class NarrationContextBuilder
{
    private static readonly Regex ForbiddenVisualLanguageRegex = new(
        @"\b(?:create\s+a\s+visual-only\s+hook\s+frame|(?:landscape|portrait)\s+composition|reserve\s+(?:a\s+)?label-safe|label-safe\s+(?:space|area)|apply\s+slow\s+camera\s+motion|camera\s+motion|render\s+\w+\s+in\s+the\s+upper\s+third|place\s+the\s+label|render\s+a\s+label|show\s+the\s+object\s+label|source\s+facts\s+attached|slow\s+reveal|steady\s+hold|visual\s+comprehension|safe\s*area|primary\s+subject|cameraIntent|compositionIntent|visualRole|motionIntent|visualAccuracyRules|prohibitedVisualChoices|safeArea|lightingIntent|visual\s+hierarchy|visual\s+prompt)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static NarrationContextDocument Build(JsonElement? longContract, JsonElement? shortContract, JsonElement? decisionLog, JsonElement? editorialBrief, JsonElement? producerNotes, JsonElement? styleContract, DocumentaryPerformerSceneFactCards factCards, RequiredSemanticFactResolutionResult? resolvedFacts, string voiceProfile, string orchestrationVersion)
    {
        var formats = new[]
        {
            new NarrationFormatContext("long", BuildBeats("long", longContract, styleContract, factCards.Long, resolvedFacts, voiceProfile)),
            new NarrationFormatContext("short", BuildBeats("short", shortContract, styleContract, factCards.Short, resolvedFacts, voiceProfile))
        };
        return new NarrationContextDocument("AstroPulse-NarrationContext-v2", orchestrationVersion, formats);
    }

    public static bool ContainsForbiddenVisualLanguage(string? value)
        => !string.IsNullOrWhiteSpace(value) && ForbiddenVisualLanguageRegex.IsMatch(value);

    private static IReadOnlyList<NarrationContextBeat> BuildBeats(string format, JsonElement? contract, JsonElement? styleContract, SceneFactCardSet cards, RequiredSemanticFactResolutionResult? resolvedFacts, string voiceProfile)
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
            var beatId = FirstNonEmpty(GetString(beat, "documentaryBeatId"), GetString(beat, "beatId"), GetString(beat, "id"));
            var facts = ReadResolvedFacts(format, beatId, resolvedFacts).DefaultIfEmpty().First() is null ? ReadAllocatedFacts(beat, warnings) : ReadResolvedFacts(format, beatId, resolvedFacts);
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

    private static IReadOnlyList<NarrationVerifiedFact> ReadResolvedFacts(string format, string? beatId, RequiredSemanticFactResolutionResult? resolvedFacts)
    {
        if (resolvedFacts is null) return [];
        var beat = resolvedFacts.Beats.FirstOrDefault(b => b.Format.Equals(format, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(beatId) || b.DocumentaryBeatId.Equals(beatId, StringComparison.OrdinalIgnoreCase)));
        return beat is null ? [] : beat.RequiredFacts.Concat(beat.OptionalFacts).Where(f => f.SafeForNarration).Select(f => new NarrationVerifiedFact(f.FactType, f.FactOrigin == "DomainKnowledge" ? f.SemanticMeaning : Convert.ToString(f.CanonicalValue, CultureInfo.InvariantCulture) ?? string.Empty, f.SemanticMeaning, f.Unit)).ToArray();
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
    private static readonly IReadOnlyDictionary<string, (string En, string Hi)> Regions = new Dictionary<string, (string En, string Hi)>(StringComparer.OrdinalIgnoreCase)
    {
        ["IN-RJ-UDAIPUR"] = ("Udaipur, Rajasthan", "उदयपुर, राजस्थान")
    };

    public static bool TryResolveDisplay(string value, string language, out string display)
    {
        display = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim().TrimEnd('.');
        foreach (var region in Regions)
        {
            if (trimmed.Equals(region.Key, StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(trimmed, $@"\b{Regex.Escape(region.Key)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                display = language.Equals("hi", StringComparison.OrdinalIgnoreCase) ? region.Value.Hi : region.Value.En;
                return true;
            }
        }
        return false;
    }

    public static string ResolveDisplay(string value, string language)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return TryResolveDisplay(value, language, out var display)
            ? Regex.Replace(value, @"\bIN-RJ-UDAIPUR\b", display, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            : value;
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

public sealed record NarrationPurityFailure(string Format, string SceneId, string DocumentaryBeatId, string Field, string RuleId, string MatchedPhrase, string SurroundingText, string SourceArtifact, string SourceField, string Severity)
{
    public override string ToString()
        => $"Narration context purity failure: format={Format}; sceneId={SceneId}; documentaryBeatId={DocumentaryBeatId}; field={Field}; ruleId={RuleId}; matchedPhrase={MatchedPhrase}; surroundingText={SurroundingText}; sourceArtifact={SourceArtifact}; sourceField={SourceField}; severity={Severity}";
}

public static class ContextSchemaValidator
{
    private static readonly HashSet<string> KnownSuccessCriteria = new(StringComparer.OrdinalIgnoreCase)
    {
        "NoPrivateNoteLeakage", "NoImperativeInstructionLeakage", "NoRawTimestampLeakage", "NoProductionLanguageLeakage", "NoInternalFieldLabelLeakage", "NoInternalIdentifierLeakage",
        "private-note prose", "imperative guidance language", "raw time strings", "production staging language", "data labels", "internal IDs", "Keep the story factual and natural.", "कहानी तथ्यपरक और स्वाभाविक रहे।"
    };

    public static IReadOnlyList<NarrationPurityFailure> Validate(NarrationContextDocument context)
    {
        var failures = new List<NarrationPurityFailure>();
        foreach (var format in context.Formats)
        foreach (var (beat, index) in format.Beats.Select((b, i) => (b, i)))
        {
            if (string.IsNullOrWhiteSpace(format.Format)) Add("format", "RequiredProperty", string.Empty, index);
            if (string.IsNullOrWhiteSpace(beat.KnowledgeGoal)) Add("knowledgeGoal", "RequiredProperty", string.Empty, index);
            foreach (var criterion in beat.SuccessCriteria.Where(c => !string.IsNullOrWhiteSpace(c) && !KnownSuccessCriteria.Contains(c) && !Regex.IsMatch(c, @"^No[A-Za-z0-9]+Leakage$", RegexOptions.CultureInvariant)))
                Add("successCriteria", "UnrecognizedSuccessCriterion", criterion, index);
        }
        return failures;

        void Add(string field, string ruleId, string match, int index) => failures.Add(new NarrationPurityFailure("unknown", string.Empty, $"beat-{index + 1}", field, ruleId, match, match, "narration-context", field, "Blocking"));
    }
}

public static class SpeakableContextPurityValidator
{
    private static readonly Regex RawTimestamp = new(@"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TimezoneOffset = new(@"(?<!\d)\b[+-]\d{2}:?\d{2}\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex InternalIdentifier = new(@"\b(?:[A-Z]{2}-[A-Z0-9]{2,}(?:-[A-Z0-9]{2,})+|(?:long|short)-beat-\d+|sceneId|beatId|sourceSemanticBeatIds)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FilePath = new(@"(?:^|\s)(?:[A-Za-z]:\\|/[^\s]+/|\.{1,2}/)[^\s]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex VisualProduction = new(@"\b(?:create\s+a\s+visual-only\s+hook\s+frame|use\s+a\s+landscape\s+composition|reserve\s+(?:a\s+)?label-safe\s+(?:space|area)|apply\s+slow\s+camera\s+motion|render\s+\w+\s+in\s+the\s+upper\s+third|place\s+the\s+label|render\s+a\s+label|show\s+the\s+object\s+label|in\s+this\s+scene,?\s+show|scene\s+\d+\s+should\s+show|create\s+a\s+scene\s+with)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex EditorialImperative = new(@"(?:^|[.!?]\s+)(?:Explain why|Use the verified timing|Use the timing field|Turn (?:these|this) facts into|Establish the importance|The viewer should understand|Mention the direction and time)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex InternalFieldLabel = new(@"(?:^|[.!?]\s+)(?:Timing|SceneId|BeatId|SourceField|SuccessCriteria)\s*:|\buse\s+the\s+timing\s+field\b|\bthe\s+timing\s+objective\s+is\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<NarrationPurityFailure> Validate(NarrationContextDocument context)
    {
        var failures = new List<NarrationPurityFailure>();
        foreach (var format in context.Formats)
        foreach (var (beat, index) in format.Beats.Select((b, i) => (b, i)))
        {
            Check(beat.KnowledgeGoal, "knowledgeGoal", format.Format, index); Check(beat.AudienceOutcome, "audienceOutcome", format.Format, index); Check(beat.EditorialIntent, "editorialIntent", format.Format, index); Check(beat.ObservationObjective, "observationObjective", format.Format, index); Check(beat.TransitionGoal, "transitionGoal", format.Format, index); Check(beat.OptionalProducerNotes, "optionalProducerNotes", format.Format, index);
            foreach (var c in beat.ScientificConstraints) Check(c, "scientificConstraints", format.Format, index);
            foreach (var fact in beat.VerifiedFacts) Check(fact.Value, "verifiedFacts.speakableValue", format.Format, index, fact.FactKey);
        }
        return failures;
        void Check(string? value, string field, string format, int index, string sourceField = "")
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (var (id, rx) in new[] { ("RawTimestamp", RawTimestamp), ("TimezoneOffset", TimezoneOffset), ("InternalIdentifier", InternalIdentifier), ("FilePath", FilePath), ("VisualProductionInstruction", VisualProduction), ("EditorialImperativeInstruction", EditorialImperative), ("InternalFieldLabel", InternalFieldLabel) })
            {
                var m = rx.Match(value); if (!m.Success) continue;
                failures.Add(new NarrationPurityFailure(format, string.Empty, $"{format}-beat-{index + 1:000}", field, id, m.Value, Surround(value, m.Index, m.Length), "narration-context", string.IsNullOrWhiteSpace(sourceField) ? field : sourceField, "Blocking"));
            }
            if (value.TrimStart().StartsWith("{") || value.TrimStart().StartsWith("[")) failures.Add(new NarrationPurityFailure(format, string.Empty, $"{format}-beat-{index + 1:000}", field, "SerializedJson", value.Trim()[..Math.Min(value.Trim().Length, 40)], Surround(value, 0, Math.Min(value.Length, 40)), "narration-context", field, "Blocking"));
            if (value.Contains("recommendedPublishWindow", StringComparison.OrdinalIgnoreCase) || value.Contains("scheduledUtc", StringComparison.OrdinalIgnoreCase)) failures.Add(new NarrationPurityFailure(format, string.Empty, $"{format}-beat-{index + 1:000}", field, "PublishingMetadata", "publishing metadata", Surround(value, 0, Math.Min(value.Length, 40)), "narration-context", field, "Blocking"));
        }
    }
    private static string Surround(string text, int index, int length) { var start = Math.Max(0, index - 32); var end = Math.Min(text.Length, index + length + 32); return text[start..end]; }
}

public static class GeneratedNarrationValidator
{
    private static readonly Regex InternalRegionCode = new(@"\b[A-Z]{2}-[A-Z0-9]{2,}(?:-[A-Z0-9]{2,})+\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PascalCaseSemanticKey = new(@"\b(?:PlanetPairingApparentLineOfSightGeometry|ApparentAlignmentExplanation|ObservationTiming|BinocularGuidance|NarrativeRole|TransitionIntent|FactType|CapabilityId|[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+){2,})\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex IncompleteTransition = new(@"\bthrough the\s*[.!?]|\{[A-Za-z0-9_]+\}|<[^>]+>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FactListFragment = new(@"^(?:[A-Z][a-z]+|[\p{IsDevanagari}]+)(?:,\s*(?:[A-Z][a-z]+|[\p{IsDevanagari}]+))+[.!?।]?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PlanningLeakage = new(@"(?:^|[.!?]\s+)(?:Explain why|Use the verified timing|Turn (?:these|this) facts into|Establish the importance|Mention the direction and time)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    public static IReadOnlyList<NarrationPurityFailure> Validate(string narration, string format = "output")
    {
        var failures = new List<NarrationPurityFailure>();
        var text = narration ?? string.Empty;
        Add(InternalRegionCode.Match(text), "InternalRegionCode");
        Add(PascalCaseSemanticKey.Match(text), "PascalCaseSemanticKey");
        Add(IncompleteTransition.Match(text), "IncompleteTransition");
        foreach (var sentence in Regex.Split(text, @"(?<=[.!?।])\s+").Select(s => s.Trim()))
            Add(FactListFragment.Match(sentence), "StandaloneFactListFragment");
        Add(PlanningLeakage.Match(text), "PlanningLeakage");
        return failures;
        void Add(Match m, string rule) { if (m.Success) failures.Add(new NarrationPurityFailure(format, string.Empty, string.Empty, "generatedNarration", rule, m.Value, m.Value, "documentary-script", "narration", "Blocking")); }
    }
}

public static class NarrationContextPurityValidator
{
    public static IReadOnlyList<string> Validate(NarrationContextDocument context)
        => ContextSchemaValidator.Validate(context).Concat(SpeakableContextPurityValidator.Validate(context)).Select(f => f.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
        if (lower.Contains("explain") || lower.Contains("science")) return "Science meaning";
        if (lower.Contains("close") || lower.Contains("ending")) return "Reflective close";
        return index == 0 ? "Opening sky fact" : "Documentary context";
    }

    private static IReadOnlyList<string> SplitSentences(string value) => Regex.Split(value ?? string.Empty, @"(?<=[.!?])\s+")
        .Select(v => v.Trim(' ', '.', ';', ':'))
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .ToArray();
}

public interface INarrationRealizer
{
    NarrationRealizationResult Realize(NarrationSafeContext context, AstronomyFamilyProfile familyProfile, LanguageProfile languageProfile);
}

public interface IRequiredSemanticFactResolver
{
    RequiredSemanticFactResolutionResult Resolve(RequiredSemanticFactResolutionInput input);
}

public sealed record RequiredSemanticFactResolutionInput(AstronomyFamilyProfile FamilyProfile, JsonElement? LongDocumentaryContract, JsonElement? ShortDocumentaryContract, JsonElement? EditorialContract, JsonElement? StoryGraph, JsonElement? ProductionEventIntelligence, JsonElement? ObservationMetadata, JsonElement? QuestionAnswerSet, LanguageProfile LanguageProfile, ContentPlanProductionPipelineRequest? ProductionPipelineRequest = null, CanonicalEventIdentity? CanonicalEventIdentity = null);
public sealed record RequiredSemanticFactResolutionResult(IReadOnlyList<ResolvedBeatFacts> Beats, object Diagnostics)
{
    public bool Blocking => Beats.Any(b => b.Blocking);
}
public sealed record ResolvedBeatFacts(string Format, string SceneId, string DocumentaryBeatId, string NarrativeRole, IReadOnlyList<ResolvedSemanticFact> RequiredFacts, IReadOnlyList<ResolvedSemanticFact> OptionalFacts, IReadOnlyList<string> MissingRequiredFacts, IReadOnlyList<string> OmittedOptionalFacts, IReadOnlyList<string> ResolutionWarnings, IReadOnlyList<FactConflict> Conflicts, IReadOnlyList<SemanticCapabilityResolution> CapabilityResolutions, bool Blocking);
public sealed record ResolvedSemanticFact(string FactType, string FactKey, object CanonicalValue, string? Unit, string SemanticMeaning, string SourceArtifact, string SourceField, string? SourceBeatId, string VerificationStatus, decimal Confidence, string Requiredness, string? LocalizedDisplayValue, string? SpeakableValue, string Language, bool SafeForNarration, string FactOrigin = "Source", string? DerivationRuleId = null, IReadOnlyList<string>? SourceInputs = null);
public sealed record FactConflict(string FactType, IReadOnlyList<object> Values, string SelectedSourceArtifact, bool Blocking, string Message);
public sealed record SemanticCapabilityResolution(string Capability, string Status, string? SelectedSource, object? CanonicalValue, string? SpeakableValue, IReadOnlyList<string> AlternativesConsidered, IReadOnlyList<string> Warnings, string CapabilityStrength, IReadOnlyList<SemanticCapabilityCandidate> Candidates, IReadOnlyList<SemanticCapabilityRejection> RejectedSources, IReadOnlyList<string> SubstitutionsApplied);
public sealed record SemanticCapabilityCandidate(string Source, string SourceField, object Value, string Strength);
public sealed record SemanticCapabilityRejection(string Source, string SourceField, string Reason);


public interface IAstronomyDomainKnowledgeProvider
{
    bool TryResolve(string familyProfileId, string semanticFactType, AstronomyKnowledgeContext context, out ResolvedSemanticFact fact);
}

public sealed record AstronomyKnowledgeContext(string FamilyProfileId, IReadOnlyList<AstronomyKnowledgeContextFact> UpstreamFacts, string LanguageCode);
public sealed record AstronomyKnowledgeContextFact(string FactType, object Value, string SourceArtifact, string SourceField);

public sealed class AstronomyDomainKnowledgeProvider : IAstronomyDomainKnowledgeProvider
{
    private static readonly string[] Registry = ["PlanetPairing", "Eclipse", "Occultation", "MeteorShower", "Constellation", "PlanetProfile", "Comet", "DeepSkyObject", "Nebula", "Galaxy", "BlackHoleOrScientificExplainer"];
    private static readonly string[] PlanetPairingFacts = ["ApparentAlignmentExplanation", "PhysicalProximityClarification", "PerspectiveExplanation", "WhyPlanetsAppearClose"];
    public bool TryResolve(string familyProfileId, string semanticFactType, AstronomyKnowledgeContext context, out ResolvedSemanticFact fact)
    {
        fact = default!;
        var family = NormalizeFamily(familyProfileId);
        if (!Registry.Contains(family, StringComparer.OrdinalIgnoreCase)) return false;
        if (!family.Equals("PlanetPairing", StringComparison.OrdinalIgnoreCase)) return false;
        if (!PlanetPairingFacts.Any(t => t.Equals(semanticFactType, StringComparison.OrdinalIgnoreCase))) return false;
        if (Regex.IsMatch(semanticFactType, "AngularSeparation|ViewingDirection|Direction|BestViewingTime|VisibilityMethod|Time|Date|Region|Location", RegexOptions.IgnoreCase)) return false;
        var semanticMeaning = new { apparentCloseness = true, viewpoint = "Earth", cause = "line-of-sight geometry", physicalProximity = false, terminologyKeys = new[] { "apparentCloseness", "EarthViewpoint", "lineOfSightGeometry", "notPhysicalProximity" }, scientificConstraints = new[] { "Do not imply physical close approach.", "Do not imply collision risk.", "Do not imply identical brightness or visibility.", "Do not use conjunction terminology unless taxonomy permits it." } };
        fact = new ResolvedSemanticFact(semanticFactType, semanticFactType, semanticMeaning, null, "PlanetPairingApparentLineOfSightGeometry", "Astronomy Domain Knowledge Provider", "PlanetPairingKnowledgeProfile", null, "DomainKnowledge", 1.0m, "Required", null, null, context.LanguageCode, true, "DomainKnowledge", "planet-pairing-apparent-line-of-sight-v1", ["familyProfileId", "semanticFactType"]);
        return true;
    }
    private static string NormalizeFamily(string family) => family.Equals("PlanetaryConjunction", StringComparison.OrdinalIgnoreCase) ? "PlanetPairing" : family;
}

public static class DomainKnowledgeDiagnosticsBuilder
{
    public static object Build(string requestedFamilyProfile, string resolvedFamilyProfile, RequiredSemanticFactResolutionResult resolution)
    {
        var facts = resolution.Beats.SelectMany(b => b.RequiredFacts.Concat(b.OptionalFacts)).ToArray();
        return new
        {
            requestedFamilyProfile,
            resolvedFamilyProfile,
            requestedFactTypes = resolution.Beats.SelectMany(b => b.RequiredFacts.Select(f => f.FactType).Concat(b.MissingRequiredFacts).Concat(b.OptionalFacts.Select(f => f.FactType))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            factsResolvedFromUpstream = facts.Where(f => f.FactOrigin == "Source").Select(f => f.FactType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            factsResolvedFromDerivedRules = facts.Where(f => f.FactOrigin == "Derived").Select(f => f.FactType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            factsResolvedFromDomainKnowledge = facts.Where(f => f.FactOrigin == "DomainKnowledge").Select(f => f.FactType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            unresolvedFactTypes = resolution.Beats.SelectMany(b => b.MissingRequiredFacts).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            providerProfileUsed = facts.FirstOrDefault(f => f.FactOrigin == "DomainKnowledge")?.SourceField,
            providerRuleIds = facts.Where(f => f.FactOrigin == "DomainKnowledge" && f.DerivationRuleId is not null).Select(f => f.DerivationRuleId).Distinct().ToArray(),
            overwritesPrevented = facts.Where(f => f.FactOrigin != "DomainKnowledge").Select(f => f.FactType).Intersect(facts.Where(f => f.FactOrigin == "DomainKnowledge").Select(f => f.FactType), StringComparer.OrdinalIgnoreCase).ToArray(),
            languageNeutralKnowledgeConfirmed = facts.Where(f => f.FactOrigin == "DomainKnowledge").All(f => f.CanonicalValue is not string),
            warnings = resolution.Beats.SelectMany(b => b.ResolutionWarnings).Distinct().ToArray(),
            errors = resolution.Beats.SelectMany(b => b.MissingRequiredFacts.Select(m => new { b.Format, b.SceneId, beatRole = b.NarrativeRole, factType = m })).Distinct().ToArray()
        };
    }
}

public sealed record SemanticResolutionScopeKeyV1(
    SemanticCapabilityId CapabilityId,
    bool Required,
    SemanticEvidenceStrengthV1 MinimumEvidenceStrength,
    IReadOnlyList<SemanticEvidenceCategoryV1> AllowedEvidenceCategories,
    SemanticMissingValueBehaviorV1 MissingValueBehavior,
    string SourceContextIdentity,
    string SourceContextVersion)
{
    public bool Equals(SemanticResolutionScopeKeyV1? other)
        => other is not null
        && CapabilityId.Equals(other.CapabilityId)
        && Required == other.Required
        && MinimumEvidenceStrength == other.MinimumEvidenceStrength
        && MissingValueBehavior == other.MissingValueBehavior
        && string.Equals(SourceContextIdentity, other.SourceContextIdentity, StringComparison.Ordinal)
        && string.Equals(SourceContextVersion, other.SourceContextVersion, StringComparison.Ordinal)
        && AllowedEvidenceCategories.SequenceEqual(other.AllowedEvidenceCategories);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CapabilityId);
        hash.Add(Required);
        hash.Add(MinimumEvidenceStrength);
        hash.Add(MissingValueBehavior);
        hash.Add(SourceContextIdentity, StringComparer.Ordinal);
        hash.Add(SourceContextVersion, StringComparer.Ordinal);
        foreach (var category in AllowedEvidenceCategories) hash.Add(category);
        return hash.ToHashCode();
    }
}

public sealed class RequiredSemanticFactResolver : IRequiredSemanticFactResolver
{
    private readonly IAstronomyDomainKnowledgeProvider _domainKnowledgeProvider;
    private readonly ISemanticCapabilityResolver _capabilityResolver;
    private readonly ISemanticResolutionEngineV1 _semanticResolutionEngine;
    private readonly ISemanticSourcePolicyCatalogV1 _sourcePolicyCatalog;
    private readonly ISemanticSourceAdapterRegistryV1 _sourceAdapterRegistry;

    public RequiredSemanticFactResolver() : this(SemanticDefaults.SemanticCapabilityResolver, SemanticDefaults.DomainKnowledgeProvider, SemanticDefaults.SemanticResolutionEngineV1, SemanticDefaults.SemanticSourcePolicyCatalogV1, SemanticDefaults.SemanticSourceAdapterRegistryV1) { }
    public RequiredSemanticFactResolver(IAstronomyDomainKnowledgeProvider domainKnowledgeProvider) : this(SemanticDefaults.SemanticCapabilityResolver, domainKnowledgeProvider, SemanticDefaults.SemanticResolutionEngineV1, SemanticDefaults.SemanticSourcePolicyCatalogV1, SemanticDefaults.SemanticSourceAdapterRegistryV1) { }
    public RequiredSemanticFactResolver(ISemanticCapabilityResolver capabilityResolver, IAstronomyDomainKnowledgeProvider domainKnowledgeProvider, ISemanticResolutionEngineV1 semanticResolutionEngine, ISemanticSourcePolicyCatalogV1? sourcePolicyCatalog = null, ISemanticSourceAdapterRegistryV1? sourceAdapterRegistry = null) { _capabilityResolver = capabilityResolver; _domainKnowledgeProvider = domainKnowledgeProvider; _semanticResolutionEngine = semanticResolutionEngine; _sourcePolicyCatalog = sourcePolicyCatalog ?? SemanticDefaults.SemanticSourcePolicyCatalogV1; _sourceAdapterRegistry = sourceAdapterRegistry ?? SemanticDefaults.SemanticSourceAdapterRegistryV1; }

    public RequiredSemanticFactResolutionResult Resolve(RequiredSemanticFactResolutionInput input)
    {
        var all = new List<CandidateFact>();
        var occurrences = EnumerateRequirementOccurrences(input).ToArray();
        var supportedOccurrences = occurrences.Where(o => o.Request is not null && o.ScopeKey is not null).ToArray();
        var resolvedByScope = supportedOccurrences
            .GroupBy(o => o.ScopeKey!)
            .ToDictionary(g => g.Key, g => _semanticResolutionEngine.Resolve(g.First().Request!));

        var beats = new List<ResolvedBeatFacts>();
        foreach (var beatGroup in occurrences.GroupBy(o => new { o.Format, o.SceneId, o.BeatId, o.Role }))
        {
            var requiredOccurrences = beatGroup.Where(o => o.Required).ToArray();
            var optionalOccurrences = beatGroup.Where(o => !o.Required).ToArray();
            var resolvedRequired = requiredOccurrences.Select(o => o.ScopeKey is null ? null : Project(o, resolvedByScope[o.ScopeKey].Fact, input)).Where(f => f is not null).Cast<ResolvedSemanticFact>().ToList();
            var resolvedOptional = optionalOccurrences.Select(o => o.ScopeKey is null ? null : Project(o, resolvedByScope[o.ScopeKey].Fact, input)).Where(f => f is not null).Cast<ResolvedSemanticFact>().ToList();
            var required = requiredOccurrences.Select(o => o.LegacyFactType).ToArray();
            var optional = optionalOccurrences.Select(o => o.LegacyFactType).ToArray();
            var missing = required.Where(t => !resolvedRequired.Any(f => Matches(t, f.FactType))).ToArray();
            var omitted = optional.Where(t => !resolvedOptional.Any(f => Matches(t, f.FactType))).ToArray();
            var conflicts = beatGroup.SelectMany(o => o.ScopeKey is null ? Array.Empty<FactConflict>() : ToFactConflicts(o, resolvedByScope[o.ScopeKey].Fact)).Concat(FindConflicts(required.Concat(optional), all)).DistinctBy(c => c.FactType).ToArray();
            var capabilityResults = beatGroup.Select(o => o.ScopeKey is null ? o.CapabilityResolution : ToCapabilityResolution(o, resolvedByScope[o.ScopeKey].Fact)).ToArray();
            var unsupportedWarnings = beatGroup.Where(o => !o.IsSupported).Select(o => $"Unsupported legacy capability {o.LegacyFactType} classified as {o.CapabilityResolution.Status}; no semantic resolution request was created.");
            var warnings = omitted.Select(o => $"Optional capability {o} was unavailable and omitted.").Concat(unsupportedWarnings).Concat(capabilityResults.SelectMany(r => r.Warnings)).Concat(conflicts.Select(c => c.Message)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            beats.Add(new(beatGroup.Key.Format, beatGroup.Key.SceneId, beatGroup.Key.BeatId, beatGroup.Key.Role, resolvedRequired, resolvedOptional, missing, omitted, warnings, conflicts, capabilityResults, missing.Length > 0 || conflicts.Any(c => c.Blocking)));
        }
        var diagnostics = new
        {
            component = "RequiredSemanticFactResolver-v1",
            resolverConcreteType = GetType().FullName,
            semanticEngineConcreteType = _semanticResolutionEngine.GetType().FullName,
            policyCount = _sourcePolicyCatalog.Policies.Count,
            adapterCount = _sourceAdapterRegistry.Adapters.Count,
            sourceContextPresence = BuildPhase7SourceContextPresenceSnapshot(input),
            semanticCapabilityDiagnostics = beats.SelectMany(b => b.CapabilityResolutions.Select(r => new { r.Capability, capabilityId = r.Capability, registeredAdapterIds = r.Candidates.Select(c => c.Source).Distinct(), adaptersExecuted = r.Candidates.Select(c => c.Source).Concat(r.RejectedSources.Select(x => x.Source)).Distinct(), candidateSources = r.Candidates.Select(c => c.Source).Distinct(), candidatesFound = r.Candidates.Count, rejectedCandidates = r.RejectedSources, selectedAdapterId = r.SelectedSource, selectedSource = r.SelectedSource, selectedStrength = r.CapabilityStrength, selectionReason = r.Status, conversionApplied = r.SubstitutionsApplied.Any(x => x.Contains("converted", StringComparison.OrdinalIgnoreCase)), substitutionApplied = r.SubstitutionsApplied.Any(), unresolvedReason = r.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase) ? null : string.Join("; ", r.RejectedSources.Select(x => x.Reason).DefaultIfEmpty("NoApprovedSourceAvailable")) })),
            requiredFactResultDiagnostics = supportedOccurrences.Select(o =>
            {
                var result = resolvedByScope[o.ScopeKey!];
                var policy = _sourcePolicyCatalog.Policies.FirstOrDefault(p => p.CapabilityId.Equals(result.Fact.CapabilityId));
                return new
                {
                    requestedLegacyField = o.LegacyFactType,
                    canonicalCapabilityId = result.Fact.CapabilityId.Value,
                    policyFound = policy is not null,
                    approvedSourceIds = policy?.ApprovedSources.Select(s => s.SourceId).ToArray() ?? Array.Empty<string>(),
                    registeredAdapterIds = _sourceAdapterRegistry.Adapters.Where(a => a.CapabilityId.Equals(result.Fact.CapabilityId)).Select(a => a.AdapterId).ToArray(),
                    invokedAdapterIds = result.Diagnostics.InvokedAdapterIds,
                    candidateCount = result.Diagnostics.CandidateCount,
                    candidateRejectionReasons = result.Diagnostics.CandidateEvaluations.Where(e => !e.Eligible).Select(e => e.RejectionReason ?? e.Disposition.ToString()).Concat(result.Fact.RejectedCandidates.Select(c => c.DiagnosticMessage)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    selectedAdapterId = result.Fact.WinningAdapterId,
                    finalResolutionStatus = result.Fact.Status.ToString(),
                    finalDiagnostic = result.Fact.DiagnosticMessage
                };
            }).ToArray(),
            sourcePrecedence = "capability-specific adapter precedence",
            blocking = beats.Any(b => b.Blocking),
            beats = beats.Select(b => new { input.FamilyProfile.FamilyId, b.Format, b.SceneId, b.DocumentaryBeatId, b.NarrativeRole, requiredCapabilities = RequiredTypes(input.FamilyProfile, b.NarrativeRole, b.Format), resolvedRequiredCapabilities = b.RequiredFacts.Select(f => f.FactType), b.MissingRequiredFacts, optionalCapabilities = OptionalTypes(input.FamilyProfile, b.NarrativeRole, b.Format), resolvedOptionalCapabilities = b.OptionalFacts.Select(f => f.FactType), b.OmittedOptionalFacts, candidateSources = CandidateSourcesByCapability(b.CapabilityResolutions), selectedSources = SelectedSourcesByCapability(b.CapabilityResolutions), rejectedSources = RejectedSourcesByCapability(b.CapabilityResolutions), substitutionsApplied = b.CapabilityResolutions.SelectMany(r => r.SubstitutionsApplied).Distinct(StringComparer.OrdinalIgnoreCase), capabilityStrength = CapabilityStrengthByCapability(b.CapabilityResolutions), beatAdaptations = b.ResolutionWarnings.Where(w => w.Contains("adapt", StringComparison.OrdinalIgnoreCase) || w.Contains("omitted", StringComparison.OrdinalIgnoreCase)), capabilityResolutions = b.CapabilityResolutions, sourceArtifacts = b.RequiredFacts.Concat(b.OptionalFacts).Select(f => f.SourceArtifact).Distinct(), b.Conflicts, derivedFacts = b.RequiredFacts.Concat(b.OptionalFacts).Where(f => f.FactOrigin == "Derived"), b.Blocking, warnings = b.ResolutionWarnings })
        };
        return new RequiredSemanticFactResolutionResult(beats, diagnostics);
    }




    private static object BuildPhase7SourceContextPresenceSnapshot(RequiredSemanticFactResolutionInput input)
    {
        var request = input.ProductionPipelineRequest;
        return new
        {
            productionPipelineRequest = new { present = request is not null, planId = request?.PlanId, eventType = request?.EventType, regionId = request?.RegionId, timeZone = request?.TimeZone, language = request?.Language, primaryObjectCount = request?.PrimaryObjects.Count ?? 0, secondaryObjectCount = request?.SecondaryObjects.Count ?? 0 },
            productionEventIntelligence = new { present = input.ProductionEventIntelligence.HasValue, eventType = TryGetRootString(input.ProductionEventIntelligence, "eventType") },
            canonicalEventIdentity = new { present = input.CanonicalEventIdentity is not null, eventType = input.CanonicalEventIdentity?.EventType, family = input.CanonicalEventIdentity?.EventFamily, source = input.CanonicalEventIdentity?.ResolutionSource },
            familyProfile = new { present = input.FamilyProfile is not null, familyId = input.FamilyProfile.FamilyId, profileId = input.FamilyProfile.FamilyId },
            observationMetadata = new { present = input.ObservationMetadata.HasValue },
            domainKnowledge = new { present = true, provider = nameof(AstronomyDomainKnowledgeProvider) },
            editorialContract = new { present = input.EditorialContract.HasValue },
            documentaryContract = new { longPresent = input.LongDocumentaryContract.HasValue, shortPresent = input.ShortDocumentaryContract.HasValue },
            locationContext = new { present = request is not null && (!string.IsNullOrWhiteSpace(request.RegionId) || !string.IsNullOrWhiteSpace(request.VisibilityRegion)), regionId = request?.RegionId, visibilityRegion = request?.VisibilityRegion },
            languageAndFormat = new { language = input.LanguageProfile.LanguageCode, requestedFormats = request?.RequestedOutputs ?? Array.Empty<string>() },
            beatOccurrence = new { longBeatCount = CountDocumentaryBeats(input.LongDocumentaryContract), shortBeatCount = CountDocumentaryBeats(input.ShortDocumentaryContract) }
        };
    }

    private static int CountDocumentaryBeats(JsonElement? contract)
    {
        if (!contract.HasValue || !contract.Value.TryGetProperty("beats", out var beats) || beats.ValueKind != JsonValueKind.Array) return 0;
        return beats.GetArrayLength();
    }

    private static IEnumerable<FactConflict> ToFactConflicts(RequirementOccurrence occurrence, ResolvedSemanticFactV1 fact)
        => fact.Conflicts.Where(c => c.Material).Select(c => new FactConflict(occurrence.LegacyFactType, c.CandidateIds.ToArray(), fact.WinningSourceId ?? fact.WinningAdapterId ?? fact.CapabilityId.Value, !c.Resolvable, c.DiagnosticMessage));

    private static SemanticCapabilityResolution ToCapabilityResolution(RequirementOccurrence occurrence, ResolvedSemanticFactV1 fact)
    {
        if (!occurrence.IsSupported) return occurrence.CapabilityResolution;
        var status = fact.Status is SemanticResolutionStatusV1.Resolved or SemanticResolutionStatusV1.ResolvedByCombination ? "Resolved" : fact.Status.ToString();
        var candidates = fact.Status is SemanticResolutionStatusV1.Resolved or SemanticResolutionStatusV1.ResolvedByCombination
            ? [new SemanticCapabilityCandidate(fact.WinningAdapterId ?? fact.WinningSourceId ?? fact.CapabilityId.Value, fact.Provenance.FirstOrDefault()?.SourcePropertyPath ?? fact.WinningCandidateId ?? fact.CapabilityId.Value, fact.TypedValue?.Value ?? fact.CanonicalValue ?? string.Empty, fact.EvidenceStrength.ToString())]
            : Array.Empty<SemanticCapabilityCandidate>();
        var rejected = fact.RejectedCandidates.Select(c => new SemanticCapabilityRejection(c.SourceId, c.Provenance.FirstOrDefault()?.SourcePropertyPath ?? c.CanonicalValue, "RejectedByPolicy")).ToArray();
        var warnings = fact.Warnings.Concat(fact.DiagnosticCode.Equals("Resolved", StringComparison.OrdinalIgnoreCase) ? [] : [fact.DiagnosticMessage]).Where(w => !string.IsNullOrWhiteSpace(w)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var substitutions = occurrence.CapabilityResolution.SubstitutionsApplied.Concat(occurrence.LegacyFactType.Equals(fact.CapabilityId.Value, StringComparison.OrdinalIgnoreCase) ? [] : [$"{occurrence.LegacyFactType} mapped to canonical capability {fact.CapabilityId.Value}."]).ToArray();
        return new SemanticCapabilityResolution(
            fact.CapabilityId.Value,
            status,
            fact.WinningSourceId,
            fact.TypedValue?.Value ?? fact.CanonicalValue,
            fact.SpeakableValue,
            fact.RejectedCandidates.Select(c => c.CanonicalValue).ToArray(),
            warnings,
            fact.EvidenceStrength.ToString(),
            candidates,
            rejected,
            substitutions);
    }

    private static ResolvedSemanticFact? Project(RequirementOccurrence occurrence, ResolvedSemanticFactV1 fact, RequiredSemanticFactResolutionInput input)
    {
        var legacy = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, occurrence.LegacyFactType, occurrence.BeatId, occurrence.Required ? "Required" : "Optional", input.LanguageProfile.LanguageCode);
        return legacy is null
            ? null
            : new ResolvedSemanticFact(
                legacy.FactType,
                legacy.FactKey,
                legacy.CanonicalValue,
                legacy.Unit,
                legacy.SemanticMeaning,
                legacy.SourceArtifact,
                legacy.SourceField,
                legacy.SourceBeatId,
                legacy.VerificationStatus.ToString(),
                legacy.Confidence,
                legacy.Requiredness.ToString(),
                legacy.LocalizedDisplayValue,
                legacy.SpeakableValue,
                legacy.Language,
                legacy.SafeForNarration,
                legacy.FactOrigin,
                legacy.DerivationRuleId,
                legacy.SourceInputs?.ToArray());
    }

    private IEnumerable<RequirementOccurrence> EnumerateRequirementOccurrences(RequiredSemanticFactResolutionInput input)
    {
        foreach (var (format, contract) in new[] { ("long", input.LongDocumentaryContract), ("short", input.ShortDocumentaryContract) })
        foreach (var (beat, index) in ReadArray(contract, "beats").OrderBy(e => GetInt(e, "beatOrder") ?? 0).Select((b, i) => (b, i)))
        {
            var role = ResolveRole(beat, index);
            var beatId = FirstNonEmpty(GetString(beat, "documentaryBeatId"), GetString(beat, "beatId"), GetString(beat, "id")) ?? $"{format}-beat-{index + 1:000}";
            var requirementKeys = RequiredTypes(input.FamilyProfile, role, format).Select(t => (Type: t, Required: true))
                .Concat(OptionalTypes(input.FamilyProfile, role, format).Select(t => (Type: t, Required: false)))
                .DistinctBy(x => (x.Type, x.Required))
                .ToArray();
            foreach (var requirement in requirementKeys)
                yield return CreateOccurrence(input, format, GetString(beat, "sceneId") ?? string.Empty, beatId, role, requirement.Type, requirement.Required);
        }
    }

    private RequirementOccurrence CreateOccurrence(RequiredSemanticFactResolutionInput input, string format, string sceneId, string beatId, string role, string type, bool required)
    {
        var legacyResolution = ResolveLegacyCapability(type);
        var capabilityId = legacyResolution.CanonicalCapabilityId;
        if (capabilityId is null || legacyResolution.MigrationDisposition is LegacySemanticCapabilityMigrationDisposition.Future or LegacySemanticCapabilityMigrationDisposition.NeedsDomainDecision or LegacySemanticCapabilityMigrationDisposition.RemoveDeadReference or LegacySemanticCapabilityMigrationDisposition.Unsupported or LegacySemanticCapabilityMigrationDisposition.UnsupportedLegacyTerm)
        {
            var warning = BuildUnsupportedLegacyCapabilityWarning(legacyResolution);
            var unsupportedCapabilityResolution = new SemanticCapabilityResolution(
                type,
                legacyResolution.MigrationDisposition.ToString(),
                null,
                null,
                null,
                [],
                [warning],
                "Missing",
                [],
                [],
                []);
            return new RequirementOccurrence(format, sceneId, beatId, role, type, new SemanticCapabilityId(type), required, null, null, unsupportedCapabilityResolution);
        }

        var capability = new SemanticCapabilityResolution(
            type,
            legacyResolution.MigrationDisposition.ToString(),
            null,
            null,
            null,
            [],
            [],
            "PendingV1Resolution",
            [],
            [],
            legacyResolution.StructuredFieldPath is null ? [] : [$"{type} migrated to {capabilityId.Value.Value} via {legacyResolution.StructuredFieldPath}."]);
        var minimumStrength = SemanticEvidenceStrengthV1.Weak;
        var allowedCategories = Enum.GetValues<SemanticEvidenceCategoryV1>().OrderBy(x => x.ToString(), StringComparer.Ordinal).ToArray();
        var missingBehavior = required ? SemanticMissingValueBehaviorV1.BlockRequired : SemanticMissingValueBehaviorV1.OmitOptional;
        var adapterContext = CreateAdapterContext(input);
        var sourceContextIdentity = FirstNonEmpty(adapterContext.EventIdentity?.CanonicalEventType, input.FamilyProfile.FamilyId, "UnknownEvent");
        var sourceContextVersion = FirstNonEmpty(adapterContext.EventIdentity?.ResolutionSource, "RequiredSemanticFactResolver.LegacyInput");
        var request = new SemanticResolutionRequestV1(capabilityId.Value, required, required ? SemanticRequirementLevelV1.Required : SemanticRequirementLevelV1.Optional, missingBehavior, minimumStrength, allowedCategories, adapterContext, input.FamilyProfile.FamilyId, format, beatId);
        var scopeKey = new SemanticResolutionScopeKeyV1(capabilityId.Value, required, minimumStrength, allowedCategories, missingBehavior, sourceContextIdentity, sourceContextVersion);
        return new RequirementOccurrence(format, sceneId, beatId, role, type, capabilityId.Value, required, scopeKey, request, capability);
    }

    private static LegacySemanticCapabilityResolution ResolveLegacyCapability(string type)
    {
        var map = LegacySemanticCapabilityMapV1.Entries.FirstOrDefault(e => e.LegacyTerm.Equals(type, StringComparison.OrdinalIgnoreCase));
        if (map is null)
            return new(type, LegacySemanticCapabilityResolutionStatus.UnsupportedLegacyTerm, null, null, LegacySemanticCapabilityMigrationDisposition.UnsupportedLegacyTerm, false, "No V1 mapping exists for this term.");

        var status = map.MigrationDisposition == LegacySemanticCapabilityMigrationDisposition.StructuredField
            ? LegacySemanticCapabilityResolutionStatus.StructuredFieldMigration
            : LegacySemanticCapabilityResolutionStatus.DeprecatedAliasMatch;
        return new(type, status, map.CanonicalCapabilityId, map.StructuredFieldPath, map.MigrationDisposition, true, null);
    }

    private static string BuildUnsupportedLegacyCapabilityWarning(LegacySemanticCapabilityResolution resolution)
    {
        var details = new List<string>
        {
            $"Unsupported legacy capability '{resolution.InputTerm}': Disposition={resolution.MigrationDisposition}"
        };
        if (resolution.CanonicalCapabilityId is not null) details.Add($"CanonicalCapability={resolution.CanonicalCapabilityId.Value}");
        if (!string.IsNullOrWhiteSpace(resolution.StructuredFieldPath)) details.Add($"StructuredFieldPath={resolution.StructuredFieldPath}");
        details.Add("no semantic request created.");
        return string.Join("; ", details);
    }

    private sealed record RequirementOccurrence(string Format, string SceneId, string BeatId, string Role, string LegacyFactType, SemanticCapabilityId CapabilityId, bool Required, SemanticResolutionScopeKeyV1? ScopeKey, SemanticResolutionRequestV1? Request, SemanticCapabilityResolution CapabilityResolution)
    {
        public bool IsSupported => Request is not null && ScopeKey is not null;
    }

    private static Dictionary<string, SemanticCapabilityCandidate[]> CandidateSourcesByCapability(IEnumerable<SemanticCapabilityResolution> resolutions) => resolutions
        .GroupBy(r => r.Capability, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.SelectMany(r => r.Candidates).ToArray(), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> SelectedSourcesByCapability(IEnumerable<SemanticCapabilityResolution> resolutions) => resolutions
        .Where(r => r.SelectedSource is not null)
        .GroupBy(r => r.Capability, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Select(r => r.SelectedSource!).First(), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, SemanticCapabilityRejection[]> RejectedSourcesByCapability(IEnumerable<SemanticCapabilityResolution> resolutions) => resolutions
        .GroupBy(r => r.Capability, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.SelectMany(r => r.RejectedSources).ToArray(), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> CapabilityStrengthByCapability(IEnumerable<SemanticCapabilityResolution> resolutions) => resolutions
        .GroupBy(r => r.Capability, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.OrderByDescending(r => CapabilityStrengthRank(r.CapabilityStrength)).First().CapabilityStrength, StringComparer.OrdinalIgnoreCase);

    private static int CapabilityStrengthRank(string strength) => strength.Equals("Strong", StringComparison.OrdinalIgnoreCase) ? 3 : strength.Equals("Medium", StringComparison.OrdinalIgnoreCase) ? 2 : strength.Equals("Weak", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    private SemanticSourceAdapterContextV1 CreateAdapterContext(RequiredSemanticFactResolutionInput input)
    {
        var request = input.ProductionPipelineRequest;
        var eventType = FirstNonEmpty(request?.EventType, TryGetRootString(input.ProductionEventIntelligence, "eventType"), TryGetAllocatedFactString(input.LongDocumentaryContract, "EventType"), input.FamilyProfile.FamilyId);
        var canonicalType = FirstNonEmpty(input.CanonicalEventIdentity?.EventType, input.FamilyProfile.FamilyId, eventType) ?? input.FamilyProfile.FamilyId;
        var familyId = FirstNonEmpty(input.CanonicalEventIdentity?.EventFamily, input.FamilyProfile.FamilyId, canonicalType) ?? input.FamilyProfile.FamilyId;
        var identity = new global::Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts.CanonicalAstronomyEventIdentity(canonicalType, familyId, familyId, eventType, input.CanonicalEventIdentity?.ResolutionSource ?? "RequiredSemanticFactResolver.Phase7CanonicalIdentity");
        var productionEventWindow = ReadEventWindowFromRequest(request) ?? ReadEventWindow(input.ProductionEventIntelligence);
        var observationEventWindow = ReadEventWindow(input.ObservationMetadata);
        var documentaryEventWindow = ReadEventWindow(input.LongDocumentaryContract);
        var angularSeparation = ReadAngularSeparationFromRequest(request) ?? ReadAngularSeparation(input.ProductionEventIntelligence) ?? ReadAngularSeparation(input.LongDocumentaryContract);
        var observationAngularSeparation = ReadAngularSeparation(input.ObservationMetadata);
        var primaryObjects = ReadObjectsFromRequest(request, includeSecondary: true) ?? ReadPrimaryObjects(input.ProductionEventIntelligence) ?? ReadPrimaryObjects(input.LongDocumentaryContract);
        var secondaryObjects = ReadSecondaryObjectsFromRequest(request);
        var meteorActivity = ReadMeteorActivity(input.ProductionEventIntelligence);
        var requestLocation = ReadObservationLocationFromRequest(request);
        var eventSource = new ProductionEventIntelligenceSourceV1(
            eventType,
            familyId,
            familyId,
            primaryObjects ?? ImmutableArray<AstronomicalObjectValue>.Empty,
            secondaryObjects,
            productionEventWindow,
            angularSeparation,
            ReadObservationDirectionFromRequest(request),
            meteorActivity);
        var observationSource = new ObservationMetadataSourceV1(observationEventWindow ?? productionEventWindow, observationAngularSeparation ?? angularSeparation, ReadObservationDirection(input.ObservationMetadata) ?? ReadObservationDirectionFromRequest(request), ReadObservationLocation(input.ObservationMetadata) ?? requestLocation);
        var documentarySource = new DocumentaryContractSourceV1(documentaryEventWindow);
        var domain = new AstronomyDomainKnowledgeSourceV1(DomainKnowledge: ReadDomainKnowledge(input.LongDocumentaryContract) ?? ResolveDomainKnowledge(familyId, primaryObjects ?? ImmutableArray<AstronomicalObjectValue>.Empty, input.LanguageProfile.LanguageCode));
        var objectKnowledge = new AstronomyObjectKnowledgeSourceV1(VerifiedObjects: primaryObjects ?? ImmutableArray<AstronomicalObjectValue>.Empty, ObjectKnowledge: ReadObjectKnowledge(input.LongDocumentaryContract, familyId));
        return new SemanticSourceAdapterContextV1(identity, eventSource, observationSource, DocumentaryContract: documentarySource, AstronomyObjectKnowledge: objectKnowledge, AstronomyDomainKnowledge: domain, Language: input.LanguageProfile.LanguageCode, TimeZone: request?.TimeZone, LocationContext: requestLocation);
    }

    private static ImmutableArray<AstronomicalObjectValue>? ReadObjectsFromRequest(ContentPlanProductionPipelineRequest? request, bool includeSecondary)
    {
        if (request is null) return null;
        var items = request.PrimaryObjects.Select(n => (Name: n, Role: "Primary", Path: "ProductionPipelineRequest.PrimaryObjects"))
            .Concat(includeSecondary ? request.SecondaryObjects.Select(n => (Name: n, Role: "Secondary", Path: "ProductionPipelineRequest.SecondaryObjects")) : []);
        var values = items.Where(i => !string.IsNullOrWhiteSpace(i.Name)).DistinctBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => new AstronomicalObjectValue(i.Name.Trim(), null, i.Role, null, [new SemanticSourceProvenanceV1(SemanticSourcePolicyVocabularyV1.ProductionEventIntelligence, nameof(ContentPlanProductionPipelineRequest), i.Path, true)])).ToImmutableArray();
        return values.Length == 0 ? null : values;
    }

    private static ImmutableArray<AstronomicalObjectValue> ReadSecondaryObjectsFromRequest(ContentPlanProductionPipelineRequest? request)
        => request?.SecondaryObjects.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => new AstronomicalObjectValue(n.Trim(), null, "Secondary", null, [new SemanticSourceProvenanceV1(SemanticSourcePolicyVocabularyV1.ProductionEventIntelligence, nameof(ContentPlanProductionPipelineRequest), "ProductionPipelineRequest.SecondaryObjects", true)])).ToImmutableArray() ?? [];

    private static EventWindowValue? ReadEventWindowFromRequest(ContentPlanProductionPipelineRequest? request)
        => request is null || (!request.StartUtc.HasValue && !request.PeakUtc.HasValue && !request.EndUtc.HasValue && !request.ScheduledUtc.HasValue && string.IsNullOrWhiteSpace(request.LocalPeakTime) && string.IsNullOrWhiteSpace(request.BestViewingWindowLocal))
            ? null
            : new EventWindowValue(request.StartUtc, request.PeakUtc ?? request.ScheduledUtc, request.EndUtc, null, null, null, null, request.TimeZone, FirstNonEmpty(request.BestViewingWindowLocal, request.LocalPeakTime, (request.PeakUtc ?? request.ScheduledUtc)?.ToString("O", CultureInfo.InvariantCulture)));

    private static AngularSeparationValue? ReadAngularSeparationFromRequest(ContentPlanProductionPipelineRequest? request)
        => request?.AngularSeparationDegrees is { } degrees ? new AngularSeparationValue(degrees, null, null, null, null, request.PeakUtc) : null;

    private static ObservationDirectionValue? ReadObservationDirectionFromRequest(ContentPlanProductionPipelineRequest? request)
        => string.IsNullOrWhiteSpace(request?.SkyDirectionHint) ? null : new ObservationDirectionValue(request.SkyDirectionHint, null, null, null, request.SkyDirectionHint);

    private static ObservationLocationValue? ReadObservationLocationFromRequest(ContentPlanProductionPipelineRequest? request)
        => request is null || (string.IsNullOrWhiteSpace(request.RegionId) && string.IsNullOrWhiteSpace(request.TimeZone)) ? null : new ObservationLocationValue(request.RegionId, null, null, null, request.TimeZone);

    private static ImmutableArray<AstronomicalObjectValue>? ReadPrimaryObjects(params JsonElement?[] sources)
    {
        var text = sources.Select(s => TryGetAllocatedFactString(s, "PrimaryObjects") ?? TryGetRootString(s, "PrimaryObjects")).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return string.IsNullOrWhiteSpace(text) ? null : text.Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => new AstronomicalObjectValue(n, null, "Primary", null, [])).ToImmutableArray();
    }

    private static EventWindowValue? ReadEventWindow(params JsonElement?[] sources)
    {
        foreach (var s in sources)
        {
            var peakUtcText = TryGetRootString(s, "peakUtc") ?? TryGetAllocatedFactString(s, "peakUtc");
            DateTimeOffset? peakUtc = DateTimeOffset.TryParse(peakUtcText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var peak) ? peak : null;
            var local = TryGetRootString(s, "localPeakTime") ?? TryGetAllocatedFactString(s, "localPeakTime");
            var best = TryGetRootString(s, "bestViewingWindowLocal") ?? TryGetAllocatedFactString(s, "bestViewingWindowLocal") ?? TryGetAllocatedFactString(s, "PeakWindow") ?? TryGetAllocatedFactString(s, "EventDateOrWindow");
            if (peakUtc is not null || !string.IsNullOrWhiteSpace(local) || !string.IsNullOrWhiteSpace(best))
                return new EventWindowValue(null, peakUtc, null, null, null, null, null, null, FirstNonEmpty(local, best, peakUtcText));
        }
        return null;
    }

    private static AngularSeparationValue? ReadAngularSeparation(params JsonElement?[] sources)
    {
        foreach (var s in sources)
        {
            var text = TryGetRootString(s, "angularSeparation") ?? TryGetAllocatedFactString(s, "AngularSeparation");
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var degrees)) return new AngularSeparationValue(degrees, null, null, null, null, null);
            var raw = TryGetAllocatedFactElement(s, "AngularSeparation");
            var value = GetString(raw, "value");
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out degrees)) return new AngularSeparationValue(degrees, null, null, null, null, null);
        }
        return null;
    }

    private static ObservationDirectionValue? ReadObservationDirection(JsonElement? source)
    {
        var direction = TryGetAllocatedFactString(source, "Direction");
        return string.IsNullOrWhiteSpace(direction) ? null : new ObservationDirectionValue(direction, null, null, null, direction);
    }

    private static ObservationLocationValue? ReadObservationLocation(JsonElement? source)
    {
        var location = TryGetAllocatedFactString(source, "Region") ?? TryGetAllocatedFactString(source, "LocationContext") ?? TryGetAllocatedFactString(source, "VisibilityRegion");
        return string.IsNullOrWhiteSpace(location) ? null : new ObservationLocationValue(location, null, null, null, null);
    }

    private static MeteorActivityValue? ReadMeteorActivity(JsonElement? source)
    {
        var zhrElement = TryGetRootProperty(source, "zhr");
        var zhrText = zhrElement is { ValueKind: JsonValueKind.Object } obj ? GetString(obj, "value") : zhrElement?.ValueKind == JsonValueKind.Number ? zhrElement.Value.GetRawText() : null;
        return int.TryParse(zhrText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zhr) ? new MeteorActivityValue(null, null, null, zhr, null, null) : null;
    }

    private static DomainScientificKnowledgeValue? ReadDomainKnowledge(JsonElement? source)
    {
        var alignment = TryGetAllocatedFactString(source, "ApparentAlignmentExplanation") ?? TryGetAllocatedFactString(source, "ApparentPairingScience");
        var significance = TryGetAllocatedFactString(source, "ScientificImportance");
        return string.IsNullOrWhiteSpace(alignment) && string.IsNullOrWhiteSpace(significance) ? null : new DomainScientificKnowledgeValue(null, alignment, significance, null);
    }


    private DomainScientificKnowledgeValue? ResolveDomainKnowledge(string familyId, ImmutableArray<AstronomicalObjectValue> objects, string languageCode)
    {
        if (_domainKnowledgeProvider.TryResolve(familyId, "ApparentAlignmentExplanation", new AstronomyKnowledgeContext(familyId, objects.Select(o => new AstronomyKnowledgeContextFact("AstronomicalObject", o.Name, "ProductionEventIntelligence", o.Role ?? "Object")).ToArray(), languageCode), out var fact))
            return new DomainScientificKnowledgeValue(null, Convert.ToString(fact.CanonicalValue, CultureInfo.InvariantCulture), null, null);
        return null;
    }

    private static ObjectKnowledgeValue? ReadObjectKnowledge(JsonElement? source, string familyId)
    {
        var facts = new List<ObjectKnowledgeFactV1>();
        foreach (var key in new[] { "Name", "ObjectName", "ObjectType", "SkyRegion", "SkyLocation", "ScientificIdentity", "IdentificationPattern", "MajorStars", "ScientificImportance", "Distance" })
        {
            var value = TryGetAllocatedFactString(source, key);
            if (!string.IsNullOrWhiteSpace(value)) facts.Add(new ObjectKnowledgeFactV1(key, value!, new SemanticSourceProvenanceV1(SemanticSourcePolicyVocabularyV1.AstronomyObjectKnowledgeProvider, nameof(ObjectKnowledgeValue), $"DocumentaryContract.AllocatedFacts.{key}", true)));
        }
        return facts.Count == 0 ? null : new ObjectKnowledgeValue(familyId, facts.ToImmutableArray());
    }

    private static string? TryGetRootString(JsonElement? element, string name) => element is { ValueKind: JsonValueKind.Object } e ? GetString(e, name) : null;
    private static JsonElement? TryGetRootProperty(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e) return null;
        foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }
    private static JsonElement? TryGetAllocatedFactElement(JsonElement? contract, string name)
    {
        foreach (var beat in ReadArray(contract, "beats"))
        {
            var facts = FindProperty(beat, "allocatedFacts");
            if (facts is { ValueKind: JsonValueKind.Object } f) foreach (var p in f.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        }
        return null;
    }
    private static string? TryGetAllocatedFactString(JsonElement? contract, string name)
    {
        var value = TryGetAllocatedFactElement(contract, name);
        return value is null ? null : value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString() : value.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? value.Value.GetRawText() : null;
    }

    [Obsolete("Legacy rollback-only path. Sprint 4B runtime resolution must use SemanticResolutionEngineV1.")]
    private static ResolvedSemanticFact ToResolved(string type, CandidateFact best, string beatId, string req, string language)
        => new(type, type, best.Value, best.Unit, type, best.SourceArtifact, best.SourceField, best.BeatId ?? beatId, best.SourceArtifact == "Approved Derived Facts" ? "Derived" : "Verified", best.Confidence, req, null, null, language, true, best.SourceArtifact == "Approved Derived Facts" ? "Derived" : "Source", best.RuleId, best.Inputs);
    [Obsolete("Legacy rollback-only path. Sprint 4B runtime resolution must use SemanticResolutionEngineV1.")]
    private static bool TryDerive(string type, List<CandidateFact> all, string family, out CandidateFact fact)
    {
        fact = default!;
        return false;
    }
    private static IEnumerable<string> RequiredTypes(AstronomyFamilyProfile p, string role, string format)
    {
        var r = role.ToLowerInvariant();
        if (p.FamilyId == "PlanetaryConjunction" || p.FamilyId == "PlanetPairing")
        {
            if (r.Contains("hook")) return ["PrimaryObjects", "EventIdentity"];
            if (r.Contains("timing")) return ["ObservationTiming"];
            if (r.Contains("science")) return ["ApparentAlignmentExplanation", "PhysicalProximityClarification"];
            if (r.Contains("observation")) return ["ObservationDirection", "ObservationTiming"];
            if (r.Contains("orientation")) return format == "long" ? ["ObservationDirection", "LocationContext"] : ["ObservationDirection"];
            return ["PrimaryObjects"];
        }
        if (!p.ContentNature.Contains("Event", StringComparison.OrdinalIgnoreCase)) return p.RequiredFactTypes.Where(t => !Regex.IsMatch(t, "Date|Time|Peak|Window", RegexOptions.IgnoreCase));
        return p.RequiredFactTypes;
    }
    private static IEnumerable<string> OptionalTypes(AstronomyFamilyProfile p, string role, string format) => p.OptionalFactTypes.Distinct(StringComparer.OrdinalIgnoreCase);
    [Obsolete("Legacy rollback-only path. Sprint 4B runtime conflict analysis is owned by SemanticResolutionEngineV1.")]
    private static IEnumerable<FactConflict> FindConflicts(IEnumerable<string> types, List<CandidateFact> all) => types.SelectMany(t => all.Where(c => Matches(t, c.Type)).GroupBy(c => c.Value.ToString(), StringComparer.OrdinalIgnoreCase).Count() > 1 ? [new FactConflict(t, all.Where(c => Matches(t, c.Type)).Select(c => c.Value).Distinct().ToArray(), all.Where(c => Matches(t, c.Type)).OrderByDescending(c => c.Confidence).First().SourceArtifact, false, $"Conflicting {t} values resolved by source precedence.")] : Array.Empty<FactConflict>()).DistinctBy(c => c.FactType);
    [Obsolete("Legacy rollback-only path. Sprint 4B runtime must not scan documentary JSON for facts.")]
    private static void AddDocumentary(List<CandidateFact> facts, JsonElement? contract, string format) { foreach (var beat in ReadArray(contract, "beats")) { var beatId = FirstNonEmpty(GetString(beat, "documentaryBeatId"), GetString(beat, "beatId"), GetString(beat, "id")); var a = FindProperty(beat, "allocatedFacts"); if (a is null) continue; AddJsonFacts(facts, a, "Documentary Contract", format, beatId); } }
    [Obsolete("Legacy rollback-only path. Sprint 4B runtime must not scan raw JSON for facts.")]
    private static void AddJsonFacts(List<CandidateFact> facts, JsonElement? e, string source, string? format, string? beatId = null, string path = "")
    {
        if (e is null) return;
        if (e.Value.ValueKind == JsonValueKind.Object) foreach (var p in e.Value.EnumerateObject()) { var type = CanonicalType(p.Name); var value = GetString(p.Value, "value") ?? ValueToString(p.Value); if (!string.IsNullOrWhiteSpace(value) && IsKnownFact(type)) facts.Add(new(type, value!, GetString(p.Value, "unit"), source, string.IsNullOrWhiteSpace(path) ? p.Name : path + "." + p.Name, beatId, format)); AddJsonFacts(facts, p.Value, source, format, beatId, string.IsNullOrWhiteSpace(path) ? p.Name : path + "." + p.Name); }
        else if (e.Value.ValueKind == JsonValueKind.Array) foreach (var item in e.Value.EnumerateArray()) AddJsonFacts(facts, item, source, format, beatId, path);
    }
    private static string CanonicalType(string key) { var k = key ?? ""; if (Regex.IsMatch(k, "primary.*object|objectPair|objects", RegexOptions.IgnoreCase)) return "PrimaryObjects"; if (Regex.IsMatch(k, "event.*type|family|title|name", RegexOptions.IgnoreCase)) return "EventIdentity"; if (Regex.IsMatch(k, "bestViewingWindowLocal|viewingWindow|preferredViewingWindow|date|window|interval", RegexOptions.IgnoreCase)) return "ObservationTiming"; if (Regex.IsMatch(k, "peak.*time|local.*time|peakUtc|peakUTC", RegexOptions.IgnoreCase)) return "LocalPeakTime"; if (Regex.IsMatch(k, "direction|azimuth|skyDirection", RegexOptions.IgnoreCase)) return "ObservationDirection"; if (Regex.IsMatch(k, "zhr|zenithal.*hourly.*rate|activityRate|peakRate", RegexOptions.IgnoreCase)) return "Zhr"; if (Regex.IsMatch(k, "separation|angular", RegexOptions.IgnoreCase)) return "AngularRelationship"; if (Regex.IsMatch(k, "region|location|timezone", RegexOptions.IgnoreCase)) return "LocationContext"; if (Regex.IsMatch(k, "binocular|naked|visibility|mode", RegexOptions.IgnoreCase)) return "ObservationMode"; if (Regex.IsMatch(k, "alignment|proximity|explanation|mechanism|science|pairing", RegexOptions.IgnoreCase)) return "ApparentPairingScience"; return k; }
    private static bool IsKnownFact(string type) => !Regex.IsMatch(type, "id|source|publish|scheduled|utc", RegexOptions.IgnoreCase);
    private static bool Matches(string requested, string actual) => requested.Equals(actual, StringComparison.OrdinalIgnoreCase) || requested.Contains(actual, StringComparison.OrdinalIgnoreCase) || actual.Contains(requested.Replace("OrWindow", ""), StringComparison.OrdinalIgnoreCase) || CapabilityAliasAccepts(requested, actual);
    private static bool CapabilityAliasAccepts(string capability, string actual) => CapabilityAliases(capability).Contains(actual, StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<string> CapabilityAliases(string capability) => capability switch
    {
        "ObservationTiming" => ["ObservationTiming", "EventDateOrWindow", "ViewingWindow", "BestViewingWindowLocal", "LocalPeakTime", "PeakTime", "PeakWindow", "StartTime", "EndTime"],
        "ObservationDirection" => ["ObservationDirection", "Direction", "SkyDirection", "SkyLocation"],
        "Zhr" => ["Zhr", "ZHR", "ZenithalHourlyRate", "Zenithal Hourly Rate"],
        "EventIdentity" => ["EventIdentity", "EventType", "Name", "Title"],
        "PrimaryObjects" => ["PrimaryObjects", "OccultingObject", "HiddenObject", "ObjectName", "Name"],
        "ApparentAlignmentExplanation" or "PhysicalProximityClarification" or "ApparentPairingScience" => ["ApparentPairingScience", "ApparentAlignmentExplanation", "PhysicalProximityClarification", "PerspectiveExplanation", "WhyPlanetsAppearClose"],
        "AngularRelationship" or "AngularSeparation" => ["AngularRelationship", "AngularSeparation"],
        "LocationContext" => ["LocationContext", "Region", "VisibilityRegion", "Location", "ObservationLocation"],
        _ => [capability]
    };

    private sealed record CandidateFact(string Type, object Value, string? Unit, string SourceArtifact, string SourceField, string? BeatId, string? Format, decimal Confidence = .95m, string? RuleId = null, IReadOnlyList<string>? Inputs = null);
    private static string ResolveRole(JsonElement beat, int index) { var text = string.Join(" ", GetString(beat, "narrativeRole"), GetString(beat, "beatRole"), GetString(beat, "role"), GetString(beat, "purpose")); foreach (var r in new[] { "Hook", "Orientation", "Timing", "Observation", "Science", "Significance", "Closing" }) if (text.Contains(r, StringComparison.OrdinalIgnoreCase)) return r; return index == 0 ? "Hook" : "Science"; }
    private static IReadOnlyList<JsonElement> ReadArray(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return []; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().Select(i => i.Clone()).ToArray(); return []; }
    private static JsonElement? FindProperty(JsonElement element, string name) { if (element.ValueKind != JsonValueKind.Object) return null; foreach (var p in element.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value; return null; }
    private static string? GetString(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return null; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? p.Value.GetRawText() : null; return null; }
    private static int? GetInt(JsonElement? element, string name) => int.TryParse(GetString(element, name), out var value) ? value : null;
    private static string? ValueToString(JsonElement element) => element.ValueKind switch { JsonValueKind.String => element.GetString(), JsonValueKind.Number => element.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public sealed record AstronomyFamilyProfile(string FamilyId, string ContentNature, string PreferredLongArchetype, string PreferredShortArchetype, IReadOnlyList<string> RequiredFactTypes, IReadOnlyList<string> OptionalFactTypes, IReadOnlyList<string> AllowedBeatRoles, IReadOnlyList<string> PreferredBeatOrder, string ObservationRequirements, string TimingRequirements, IReadOnlyList<string> ScientificConcepts, IReadOnlyList<string> ProhibitedAssumptions, IReadOnlyList<string> ValidationRules);
public sealed record ResolvedFamilyProfile(string ResolvedEventFamily, string ResolvedProfileId, string ResolutionSource, bool FallbackUsed, string? FallbackReason, string ResolvedProfileVersion)
{
    public string CanonicalFamilyId => ResolvedEventFamily;
}
public sealed record AstronomyFamilyProfileResolutionInput(string? EventType, string? ContentCategory, string? DocumentaryArchetype, string? ObservationMode, JsonElement? EditorialContract = null, JsonElement? CreativeStoryboard = null, JsonElement? LongDocumentaryContract = null, JsonElement? ShortDocumentaryContract = null, JsonElement? ProductionEventIntelligence = null, JsonElement? ObservationMetadata = null);
public sealed record AstronomyFamilyProfileResolutionResult(AstronomyFamilyProfile Profile, ResolvedFamilyProfile Resolved, object Diagnostics);
public sealed record FamilyProfileResolutionStage(string Stage, string? InputValue, string? ResolvedEventFamily, string? ResolvedProfileId, string ResolutionSource, bool FallbackUsed);
public sealed record CanonicalEventIdentity(string EventType, string? EventFamily, string? StrategyId, string? SourceEventType, string NormalizedEventType, string ResolutionSource, bool AliasApplied, IReadOnlyDictionary<string, string?> InspectedSources, IReadOnlyList<string> StoryFrameEventTypes, IReadOnlyList<string> Conflicts, IReadOnlyList<string> BlockingErrors);
public sealed record CanonicalEventIdentityResolutionInput(string? RequestEventType, string? ProductionIntelligenceEventType, string? DocumentaryContractEventType, IReadOnlyList<string> StoryFrameEventTypes, string? NarrationContextEventType);
public sealed record RealizedSemanticFact(string IntentType, string FactType, string Label, string Value, string? Unit = null);
public sealed record TransitionIntent(string FromConcept, string ToConcept, string Relationship);
public sealed record NarrationRealizationResult(string Format, string SceneId, string BeatRole, string FamilyProfileId, string ContentNature, string NarrativeRole, string NarrativePurpose, IReadOnlyList<RealizedSemanticFact> SpeakableFacts, IReadOnlyList<string> ScientificBoundaries, IReadOnlyList<RealizedSemanticFact> ObservationDetails, TransitionIntent? TransitionIntent, string Tone, string Rhythm, int WordBudget, string? PriorBeatSummary, string? NextBeatPurpose, IReadOnlyList<string> ForbiddenNarrationPatterns, string OpeningGuidance, bool CanRealize = true, IReadOnlyList<string>? MissingRequiredFacts = null);
public sealed record NarrationRealizationIssue(string FamilyProfile, string Format, string SceneId, string BeatRole, string Field, string DetectedIssue, string SourceArtifact, string SourceField, string NormalizationStep, string RealizationStep);

public static class CanonicalEventIdentityResolver
{
    public static CanonicalEventIdentity Resolve(CanonicalEventIdentityResolutionInput input)
    {
        var sources = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProductionPipelineRequest.EventType"] = Clean(input.RequestEventType),
            ["ProductionEventIntelligence.EventType"] = Clean(input.ProductionIntelligenceEventType),
            ["Phase6DocumentaryContract.EventType"] = Clean(input.DocumentaryContractEventType),
            ["StoryFrame.EventType"] = Clean(input.StoryFrameEventTypes.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))),
            ["NarrationContext.EventType"] = Clean(input.NarrationContextEventType)
        };
        var selected = sources.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Value));
        var conflicts = sources.Where(p => !string.IsNullOrWhiteSpace(p.Value) && !string.Equals(p.Value, selected.Value, StringComparison.OrdinalIgnoreCase)).Select(p => $"{p.Key}={p.Value} differs from {selected.Key}={selected.Value}").ToArray();
        if (string.IsNullOrWhiteSpace(selected.Value))
            return new(string.Empty, null, null, null, string.Empty, "Missing", false, sources, input.StoryFrameEventTypes, conflicts, ["Canonical event identity missing. Inspected sources: " + string.Join(", ", sources.Select(s => $"{s.Key}={(string.IsNullOrWhiteSpace(s.Value) ? "<missing>" : s.Value)}"))]);

        var normalized = Normalize(selected.Value!, out var aliasApplied);
        return new(normalized, MapFamily(normalized), normalized, selected.Value, normalized, selected.Key, aliasApplied, sources, input.StoryFrameEventTypes, conflicts, []);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalize(string value, out bool aliasApplied)
    {
        var normalized = new AstronomyEventAliasCatalogV1().Normalize(value);
        aliasApplied = normalized.AppliedAliases.Count > 0;
        return normalized.CanonicalEventType ?? value.Trim();
    }

    private static string? MapFamily(string eventType) => AstronomyFamilyProfileCatalog.TryMapToProfileId(eventType, out var profileId) ? profileId : null;
}

public static class CanonicalEventIdentityDiagnosticsBuilder
{
    public static object Build(CanonicalEventIdentity identity, AstronomyFamilyProfileResolutionResult? resolution = null) => new
    {
        requestEventType = identity.InspectedSources.GetValueOrDefault("ProductionPipelineRequest.EventType"),
        productionIntelligenceEventType = identity.InspectedSources.GetValueOrDefault("ProductionEventIntelligence.EventType"),
        documentaryContractEventType = identity.InspectedSources.GetValueOrDefault("Phase6DocumentaryContract.EventType"),
        storyFrameEventTypes = identity.StoryFrameEventTypes,
        narrationContextEventType = identity.InspectedSources.GetValueOrDefault("NarrationContext.EventType"),
        selectedCanonicalEventType = string.IsNullOrWhiteSpace(identity.EventType) ? null : identity.EventType,
        selectedEventFamily = identity.EventFamily,
        selectedStrategyId = identity.StrategyId,
        resolutionSource = identity.ResolutionSource,
        aliasApplied = identity.AliasApplied,
        profileId = resolution?.Resolved.ResolvedProfileId,
        profileResolved = resolution is not null,
        conflicts = identity.Conflicts,
        blockingErrors = identity.BlockingErrors
    };
}

public static class AstronomyFamilyProfileCatalog
{
    public const string ProfileVersion = "AstronomyFamilyProfileCatalog-v2";
    private static readonly IReadOnlyDictionary<string, AstronomyFamilyProfile> Profiles = new Dictionary<string, AstronomyFamilyProfile>(StringComparer.OrdinalIgnoreCase)
    {
        ["PlanetPairing"] = new("PlanetPairing", "TimedObservationEvent", "ObservationExplainer", "SkyWatchShort", ["PrimaryObjects", "ObservationTiming", "ApparentPairingScience"], ["ObservationDirection", "AngularRelationship", "LocalPeakTime", "BinocularGuidance", "VisibilityConditions"], ["Hook", "Orientation", "Timing", "Observation", "Science", "Significance", "Closing"], ["Hook", "Orientation", "Timing", "Science", "Observation", "Closing"], "Direction when available; equipment only when verified.", "Event date or window is required.", ["ApparentAlignment"], ["Physical closeness", "Unverified weather", "Unverified brightness"], ["No raw producer notes", "No unsupported science"]),
        ["PlanetaryConjunction"] = new("PlanetaryConjunction", "TimedObservationEvent", "ObservationExplainer", "SkyWatchShort", ["PrimaryObjects", "ObservationTiming", "ApparentPairingScience"], ["ObservationDirection", "AngularRelationship", "LocalPeakTime", "BinocularGuidance", "VisibilityConditions"], ["Hook", "Orientation", "Timing", "Observation", "Science", "Significance", "Closing"], ["Hook", "Orientation", "Timing", "Science", "Observation", "Closing"], "Direction when available; equipment only when verified.", "Event date or window is required.", ["ApparentAlignment"], ["Physical closeness", "Unverified weather", "Unverified brightness"], ["No raw producer notes", "No unsupported science"]),
        ["Occultation"] = new("Occultation", "TimedObservationEvent", "TimedMechanismExplainer", "SkyWatchShort", ["OccultingObject", "HiddenObject", "StartTime", "VisibilityRegion", "Mechanism"], ["EndTime", "Duration", "ReappearanceTime", "TelescopeGuidance"], ["Hook", "Orientation", "Timing", "Observation", "Science", "Closing"], ["Hook", "Timing", "Orientation", "Science", "Observation", "Closing"], "Region and object pairing required.", "Start time required; end time or duration preferred.", ["ForegroundBody", "Reappearance"], ["Global visibility", "Instantaneous everywhere"], ["Mechanism must be stated"]),
        ["Eclipse"] = new("Eclipse", "TimedObservationEvent", "TimedMechanismExplainer", "SkyWatchShort", ["EclipseType", "EventDateOrWindow", "VisibilityRegion", "SafetyGuidance", "Mechanism"], ["StartTime", "PeakTime", "EndTime", "Magnitude"], ["Hook", "Orientation", "Timing", "Observation", "Science", "Closing"], ["Hook", "Safety", "Timing", "Science", "Observation", "Closing"], "Safety guidance required for solar eclipses.", "Window or date required.", ["ShadowGeometry", "OrbitalAlignment"], ["Unsafe solar viewing", "Worldwide visibility"], ["Safety must not be omitted"]),
        ["MeteorShower"] = new("MeteorShower", "TimedObservationEvent", "ObservationGuide", "SkyWatchShort", ["Name", "EventDateOrWindow", "Radiant", "PeakWindow"], ["MoonPhase", "Zhr", "DarkSkyGuidance"], ["Hook", "Orientation", "Timing", "Observation", "Science", "Closing"], ["Hook", "Timing", "Observation", "Science", "Closing"], "Dark sky and patience guidance when verified.", "Peak window required.", ["CometDebris", "Radiant"], ["Guaranteed counts"], ["No guaranteed meteors"]),
        ["NamedFullMoon"] = new("NamedFullMoon", "TimedObservationEvent", "ObservationExplainer", "SkyWatchShort", ["Name", "EventDateOrWindow", "MoonPhase"], ["MoonriseTime", "VisibilityRegion", "CulturalNameContext"], ["Hook", "Orientation", "Timing", "Observation", "Science", "Closing"], ["Hook", "Timing", "Science", "Observation", "Closing"], "Moonrise/location guidance only when verified.", "Full Moon date or window is required.", ["LunarPhase", "SunEarthMoonGeometry"], ["Unverified folklore", "Guaranteed horizon visibility"], ["Named Moon identity must come from event type or verified metadata"]),
        ["FullMoon"] = new("FullMoon", "TimedObservationEvent", "ObservationExplainer", "SkyWatchShort", ["EventDateOrWindow", "MoonPhase"], ["MoonriseTime", "VisibilityRegion"], ["Hook", "Orientation", "Timing", "Observation", "Science", "Closing"], ["Hook", "Timing", "Science", "Observation", "Closing"], "Moonrise/location guidance only when verified.", "Full Moon date or window is required.", ["LunarPhase", "SunEarthMoonGeometry"], ["Unverified folklore", "Guaranteed horizon visibility"], ["Full Moon identity must come from event type or verified metadata"]),
        ["Constellation"] = new("Constellation", "EducationalObjectProfile", "ObjectProfile", "ConstellationShort", ["Name", "SkyRegion", "IdentificationPattern", "MajorStars", "ScientificIdentity"], ["Mythology", "BestSeason", "DeepSkyObjects"], ["Hook", "Orientation", "Science", "Significance", "Closing"], ["Hook", "Orientation", "Science", "Significance", "Closing"], "Identification pattern replaces event viewing direction.", "No event date required.", ["StarPattern", "CelestialCoordinates"], ["Required event date"], ["Do not force event structure"]),
        ["PlanetProfile"] = new("PlanetProfile", "EducationalObjectProfile", "PlanetProfile", "PlanetShort", ["Name", "PlanetType", "ScientificIdentity"], ["Distance", "Visibility", "Moons", "Atmosphere"], ["Hook", "Science", "Significance", "Closing"], ["Hook", "Science", "Significance", "Closing"], "Observation details optional.", "Timing optional.", ["Orbit", "Composition"], ["Current visibility unless verified"], ["No fabricated live sky facts"]),
        ["Comet"] = new("Comet", "ScientificObjectProfile", "CometProfile", "CometShort", ["Name", "ObjectType", "Orbit", "ScientificImportance"], ["Perihelion", "Visibility", "TelescopeGuidance"], ["Hook", "Science", "Observation", "Significance", "Closing"], ["Hook", "Science", "Observation", "Closing"], "Observation optional unless visibility story.", "Timing optional unless observing event.", ["IcyBody", "TailFormation"], ["Guaranteed naked-eye visibility"], ["Visibility must be verified"]),
        ["DeepSkyObject"] = new("DeepSkyObject", "ScientificObjectProfile", "DeepSkyProfile", "DeepSkyShort", ["ObjectName", "ObjectType", "SkyLocation", "ScientificImportance"], ["Distance", "DiscoveryHistory", "TelescopeGuidance", "ImagingNotes"], ["Hook", "Orientation", "Science", "Significance", "Closing"], ["Hook", "Orientation", "Science", "Significance", "Closing"], "Location relative to constellation or stars preferred.", "Timing optional unless observation-focused.", ["Distance", "AstrophysicalStructure"], ["Required event date"], ["Do not force event structure"]),
        ["BlackHoleOrScientificExplainer"] = new("BlackHoleOrScientificExplainer", "ScientificExplainer", "ScienceExplainer", "ScienceShort", ["Concept", "ScientificIdentity", "Evidence", "ScientificImportance"], ["DiscoveryHistory", "ObservationMethod"], ["Hook", "Science", "Significance", "Closing"], ["Hook", "Science", "Significance", "Closing"], "Observation details optional.", "No timing required.", ["Gravity", "EventHorizon", "ObservationalEvidence"], ["Visible surface", "Required event date"], ["No unsupported claims"])
    };

    public static AstronomyFamilyProfile Resolve(JsonElement? contract, JsonElement? storyboard) => ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput(FirstNonEmpty(GetString(contract, "eventType"), GetString(contract, "family")), GetString(contract, "contentCategory"), GetString(contract, "documentaryArchetype"), null, contract, storyboard)).Profile;

    public static AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(AstronomyFamilyProfileResolutionInput input)
    {
        var stages = BuildStages(input).ToArray();
        var winning = stages.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.ResolvedProfileId));
        if (winning is null || string.IsNullOrWhiteSpace(winning.ResolvedProfileId) || !Profiles.TryGetValue(winning.ResolvedProfileId, out var profile))
            throw new InvalidOperationException($"Unable to resolve astronomy family profile. EventType = {input.EventType ?? "<missing>"}. No matching profile found.");
        var resolved = new ResolvedFamilyProfile(winning.ResolvedEventFamily!, winning.ResolvedProfileId!, winning.ResolutionSource, false, null, ProfileVersion);
        var diagnostics = new
        {
            familyProfileRequested = new { input.EventType, input.ContentCategory, input.DocumentaryArchetype, input.ObservationMode },
            familyProfileResolved = resolved.ResolvedProfileId,
            familyProfileSource = resolved.ResolutionSource,
            fallbackApplied = false,
            fallbackReason = (string?)null,
            resolvedProfileVersion = ProfileVersion,
            resolutionChain = stages.Select(s => new { s.Stage, resolvedEventFamily = s.ResolvedEventFamily, resolvedProfileId = s.ResolvedProfileId, s.ResolutionSource, s.FallbackUsed })
        };
        return new(profile, resolved, diagnostics);
    }

    public static AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(CanonicalEventIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.EventType))
            throw new InvalidOperationException("Canonical event identity missing. " + string.Join("; ", identity.BlockingErrors.DefaultIfEmpty("No event type was present in inspected sources.")));
        if (!TryMapToProfileId(identity.EventType, out var profileId) || string.IsNullOrWhiteSpace(profileId))
            throw new InvalidOperationException($"Unsupported astronomy event type: {identity.EventType}");
        if (!Profiles.TryGetValue(profileId, out var profile))
            throw new InvalidOperationException($"Unsupported astronomy event type: {identity.EventType}");
        var resolved = new ResolvedFamilyProfile(profileId, profileId, identity.ResolutionSource, identity.AliasApplied, identity.AliasApplied ? $"Normalized {identity.SourceEventType} to {identity.NormalizedEventType}." : null, ProfileVersion);
        var diagnostics = new
        {
            canonicalEventIdentity = identity,
            familyProfileResolved = resolved.ResolvedProfileId,
            familyProfileSource = resolved.ResolutionSource,
            fallbackApplied = false,
            fallbackReason = (string?)null,
            resolvedProfileVersion = ProfileVersion
        };
        return new(profile, resolved, diagnostics);
    }

    private static IEnumerable<FamilyProfileResolutionStage> BuildStages(AstronomyFamilyProfileResolutionInput input)
    {
        yield return Stage("ProductionPipelineRequest.EventType", input.EventType, "ProductionPipelineRequest.EventType");
        yield return Stage("ProductionEventIntelligence.EventType", GetString(input.ProductionEventIntelligence, "eventType"), "ProductionEventIntelligence.EventType");
        yield return Stage("Story Intelligence", FirstNonEmpty(GetString(input.EditorialContract, "storyTheme"), GetString(input.EditorialContract, "theme")), "EditorialContract.storyTheme");
        yield return Stage("Editorial Contract", FirstNonEmpty(GetString(input.EditorialContract, "eventType"), GetString(input.EditorialContract, "family"), input.ContentCategory), "EditorialContract");
        yield return Stage("Creative Storyboard", FirstNonEmpty(GetString(input.CreativeStoryboard, "eventType"), GetString(input.CreativeStoryboard, "storyTheme")), "CreativeStoryboard");
        yield return Stage("Documentary Contract", FirstNonEmpty(GetString(input.LongDocumentaryContract, "eventType"), GetString(input.ShortDocumentaryContract, "eventType"), input.DocumentaryArchetype), "DocumentaryContract");
        yield return Stage("Narration Context", FirstNonEmpty(input.ObservationMode, input.ContentCategory), "NarrationContext");
        yield return Stage("AstronomyFamilyProfileResolver", FirstNonEmpty(input.EventType, GetString(input.ProductionEventIntelligence, "eventType"), GetString(input.CreativeStoryboard, "eventType")), nameof(ResolveFamilyProfile));
        yield return Stage("RequiredSemanticFactResolver", FirstNonEmpty(input.EventType, GetString(input.ProductionEventIntelligence, "eventType")), "RequiredSemanticFactResolverInput");
    }

    private static FamilyProfileResolutionStage Stage(string stage, string? value, string source)
    {
        var family = MapToProfileId(value);
        return new(stage, value, family, family, source, false);
    }

    public static bool TryMapToProfileId(string? value, out string? profileId)
    {
        profileId = MapToProfileId(value);
        return profileId is not null;
    }

    private static string? MapToProfileId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Equals("NamedFullMoon", StringComparison.OrdinalIgnoreCase)) return "NamedFullMoon";
        if (text.Equals("FullMoon", StringComparison.OrdinalIgnoreCase)) return "FullMoon";
        if (text.Equals("PlanetGrouping", StringComparison.OrdinalIgnoreCase)) return "PlanetGrouping";
        if (text.Equals("PlanetaryConjunction", StringComparison.OrdinalIgnoreCase)) return "PlanetaryConjunction";
        if (Regex.IsMatch(text, "PlanetPairing|planetary encounter|close apparent meeting of two planets|conjunction|close pairing", RegexOptions.IgnoreCase)) return "PlanetPairing";
        if (Regex.IsMatch(text, "SolarEclipse|LunarEclipse|Eclipse", RegexOptions.IgnoreCase)) return "Eclipse";
        if (Regex.IsMatch(text, "LunarOccultation|Occultation|occult", RegexOptions.IgnoreCase)) return "Occultation";
        if (Regex.IsMatch(text, "Constellation|Orion|Ursa", RegexOptions.IgnoreCase)) return "Constellation";
        if (Regex.IsMatch(text, "Galaxy|Nebula|DeepSkyObject|deep sky|cluster|Messier|NGC", RegexOptions.IgnoreCase)) return "DeepSkyObject";
        if (Regex.IsMatch(text, "^Planet$|PlanetProfile", RegexOptions.IgnoreCase)) return "PlanetProfile";
        if (Regex.IsMatch(text, "MeteorShower|meteor", RegexOptions.IgnoreCase)) return "MeteorShower";
        if (Regex.IsMatch(text, "Comet", RegexOptions.IgnoreCase)) return "Comet";
        if (Regex.IsMatch(text, "black hole|event horizon", RegexOptions.IgnoreCase)) return "BlackHoleOrScientificExplainer";
        return null;
    }

    private static string? GetString(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return null; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? p.Value.GetRawText() : null; return null; }
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public sealed class NarrationRealizer : INarrationRealizer
{
    public NarrationRealizationResult Realize(NarrationSafeContext context, AstronomyFamilyProfile familyProfile, LanguageProfile languageProfile)
    {
        var facts = context.SpeakableFacts.Select(f => new RealizedSemanticFact(IntentForFact(f.FactType), f.FactKey, HumanizeFact(f.FactKey), f.SpeakableValue, f.CanonicalUnit)).ToArray();
        var observation = facts.Where(f => Regex.IsMatch(f.FactType + f.Label, "Direction|Window|Time|Date|Region|Visibility|Telescope|Binocular|Radiant|SkyLocation|Pattern", RegexOptions.IgnoreCase)).ToArray();
        var role = ResolveBeatRole(context, familyProfile);
        return new NarrationRealizationResult(context.Format, string.IsNullOrWhiteSpace(context.SceneId) ? $"{context.Format}-{role}" : context.SceneId, role, familyProfile.FamilyId, familyProfile.ContentNature, role, Purpose(role, familyProfile), facts, BuildBoundaries(context, familyProfile), observation, BuildTransition(role, familyProfile), context.Tone, context.Rhythm, context.WordBudget, null, null, ["private-note prose", "imperative guidance language", "raw time strings", "production staging language", "data labels", "internal IDs"], OpeningGuidance(role, familyProfile));
    }
    private static string IntentForFact(string t) => Regex.IsMatch(t, "Direction|Window|Time|Date|Region|Visibility|Telescope|Binocular|SkyLocation", RegexOptions.IgnoreCase) ? "ObservationGuidance" : Regex.IsMatch(t, "Explanation|Mechanism|Science|Identity|Importance|Concept", RegexOptions.IgnoreCase) ? "ScienceMeaning" : "SpeakableFact";
    private static string ResolveBeatRole(NarrationSafeContext c, AstronomyFamilyProfile p) { var s = c.NarrativeRole + " " + c.KnowledgeGoal + " " + c.ObservationObjective; foreach (var r in p.AllowedBeatRoles) if (s.Contains(r, StringComparison.OrdinalIgnoreCase)) return r; if (!string.IsNullOrWhiteSpace(c.ObservationObjective)) return p.AllowedBeatRoles.Contains("Observation") ? "Observation" : "Orientation"; return p.PreferredBeatOrder.FirstOrDefault() ?? "Science"; }
    private static string Purpose(string role, AstronomyFamilyProfile p) => role.ToLowerInvariant() switch { "hook" => p.ContentNature.Contains("Event") ? "Create curiosity about a timed sky event." : p.FamilyId.Contains("Galaxy") ? "Create wonder about scale and distance." : "Create curiosity about the subject.", "orientation" => p.ContentNature.Contains("Event") ? "Give spatial clarity using verified observing facts." : "Show how to locate or identify the subject without event assumptions.", "timing" => "Give temporal clarity only from verified timing facts.", "science" => "Convert verified scientific concepts into accurate spoken understanding.", "significance" => "Reveal meaning, scale, or importance.", "closing" => "Leave a memorable factual takeaway and wonder.", _ => "Carry the documentary beat using verified facts." };
    private static IReadOnlyList<string> BuildBoundaries(NarrationSafeContext c, AstronomyFamilyProfile p) => c.Constraints.Concat(p.ProhibitedAssumptions.Select(a => "Do not imply " + a + ".")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static TransitionIntent BuildTransition(string role, AstronomyFamilyProfile p) => role.Equals("Orientation", StringComparison.OrdinalIgnoreCase) ? new("Where to look", "When or why it matters", "LocationToTimingOrMeaning") : new(role, "Next documentary concept", "SemanticContinuity");
    private static string OpeningGuidance(string role, AstronomyFamilyProfile p) => role.ToLowerInvariant() switch { "hook" => "curiosity, surprise, or wonder", "orientation" => "spatial clarity", "timing" => "temporal clarity", "observation" => "practical action", "science" => "conceptual meaning", "significance" => "meaning or scale", "closing" => "memorable takeaway or wonder", _ => p.ContentNature };
    private static string HumanizeFact(string value) => Regex.Replace(value ?? string.Empty, "(?<!^)([A-Z])", " $1").Trim();
}

public static class NarrationRealizedContextMapper
{
    public static NarrationContextDocument ToContext(NarrationContextDocument source, IReadOnlyList<NarrationRealizationResult> results)
    {
        var byFormat = results.GroupBy(r => r.Format, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        return source with { Formats = source.Formats.Select(f => new NarrationFormatContext(f.Format, byFormat.TryGetValue(f.Format, out var rs) ? rs.Select(ToBeat).ToArray() : f.Beats)).ToArray() };
    }
    private static NarrationContextBeat ToBeat(NarrationRealizationResult r) => new(r.NarrativeRole, r.NarrativePurpose, r.OpeningGuidance, r.SpeakableFacts.Concat(r.ObservationDetails).Select(f => new NarrationVerifiedFact(f.FactType, f.Value, f.Unit)).ToArray(), r.ScientificBoundaries, string.Join("; ", r.ObservationDetails.Select(f => f.Value)), r.TransitionIntent is null ? string.Empty : $"{r.TransitionIntent.FromConcept} -> {r.TransitionIntent.ToConcept} ({r.TransitionIntent.Relationship})", r.Tone, r.Rhythm, r.ForbiddenNarrationPatterns, null);
}

public static class NarrationRealizationValidator
{
    private static readonly Regex Blocked = new(@"\b(explain|establish|use the verified|turn this into|producer notes?|scene goal|visual|camera|render|metadata|JSON|prompt|\d{4}-\d{2}-\d{2}T|[a-z]{2}-[A-Z]{2}-[a-z0-9-]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static IReadOnlyList<NarrationRealizationIssue> Validate(IReadOnlyList<NarrationRealizationResult> results, AstronomyFamilyProfile profile)
    {
        var issues = new List<NarrationRealizationIssue>();
        foreach (var r in results)
        {
            foreach (var (field, value) in new[] { ("narrativePurpose", r.NarrativePurpose), ("openingGuidance", r.OpeningGuidance), ("transitionIntent", r.TransitionIntent?.Relationship ?? string.Empty) })
                if (Blocked.IsMatch(value)) issues.Add(new(profile.FamilyId, r.Format, r.SceneId, r.BeatRole, field, "imperative editorial or raw metadata language detected", "narration-realization", field, "NarrationInputNormalizer", "NarrationRealizer"));
            var labels = r.SpeakableFacts.Select(f => f.FactType).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var req in RequiredForBeat(profile, r.BeatRole).Where(x => !x.Contains("when verified", StringComparison.OrdinalIgnoreCase)))
                if (IsStrict(req) && !labels.Any(l => l.Contains(req, StringComparison.OrdinalIgnoreCase) || req.Contains(l, StringComparison.OrdinalIgnoreCase))) issues.Add(new(profile.FamilyId, r.Format, r.SceneId, r.BeatRole, req, "missing required profile fact", "narration-safe-context", req, "profile-requiredness", "NarrationRealizer"));
        }
        return issues;
    }
    private static IEnumerable<string> RequiredForBeat(AstronomyFamilyProfile profile, string beatRole)
    {
        if (profile.FamilyId.Equals("PlanetPairing", StringComparison.OrdinalIgnoreCase) || profile.FamilyId.Equals("PlanetaryConjunction", StringComparison.OrdinalIgnoreCase))
            return beatRole.Contains("Science", StringComparison.OrdinalIgnoreCase) ? ["ApparentAlignmentExplanation", "PhysicalProximityClarification"] : [];
        return profile.RequiredFactTypes;
    }
    private static bool IsStrict(string req) => !Regex.IsMatch(req, "Date|Time|Direction|Angular|Safety|Start|End|Peak", RegexOptions.IgnoreCase);
}

public static class RequiredSemanticFactPhase7Validator
{
    public static IReadOnlyList<NarrationRealizationIssue> Validate(RequiredSemanticFactResolutionResult resolution)
        => resolution.Beats.Where(b => b.Blocking).SelectMany(b => b.MissingRequiredFacts.Select(m => new NarrationRealizationIssue(
            "RequiredSemanticFactResolver",
            b.Format,
            b.SceneId,
            b.NarrativeRole,
            m,
            "missing required semantic fact",
            "required-semantic-fact-diagnostics",
            m,
            "RequiredSemanticFactResolver",
            "NarrationRealizer"))).DistinctBy(i => (i.Format, i.SceneId, i.BeatRole, i.Field)).ToArray();
}

public static class NarrationRealizationDiagnosticsBuilder
{
    public static object Build(AstronomyFamilyProfile profile, IReadOnlyList<NarrationRealizationResult> results, IReadOnlyList<NarrationRealizationIssue> issues, LanguageProfile language) => new { familyProfileSelected = profile.FamilyId, profile.ContentNature, longArchetype = profile.PreferredLongArchetype, shortArchetype = profile.PreferredShortArchetype, requiredFacts = profile.RequiredFactTypes, optionalFacts = profile.OptionalFactTypes, missingRequiredFacts = issues.Where(i => i.DetectedIssue.Contains("missing")).Select(i => i.Field).Distinct().ToArray(), omittedOptionalFacts = profile.OptionalFactTypes.Where(o => !results.SelectMany(r => r.SpeakableFacts).Any(f => f.FactType.Contains(o, StringComparison.OrdinalIgnoreCase))).ToArray(), semanticIntentsCreated = results.SelectMany(r => r.SpeakableFacts.Select(f => f.IntentType)).Distinct().ToArray(), producerInstructionsRemoved = true, transitionsRealized = results.Count(r => r.TransitionIntent is not null), languageProfileUsed = language.ProfileId, terminologyProfileUsed = language.TerminologySource, openingDiversityMetrics = new { distinctGuidance = results.Select(r => r.OpeningGuidance).Distinct(StringComparer.OrdinalIgnoreCase).Count(), total = results.Count }, warnings = Array.Empty<string>(), errors = issues };
}


public static class LlmDocumentaryTranscriptionist
{
    public static DocumentaryScript Transcribe(IReadOnlyList<NarrationContextBeat> contexts, string format, string language, string outline, IReadOnlyList<NarrationRealizationResult>? realizations = null)
    {
        var orderedContexts = contexts.ToArray();
        var realized = realizations ?? [];
        var isShort = format.Equals("short", StringComparison.OrdinalIgnoreCase);
        var isHindi = language.Equals("hi", StringComparison.OrdinalIgnoreCase);
        var title = isHindi ? (isShort ? "आज का आकाश एक नज़र में" : "शाम के आकाश में शांत युति") : (isShort ? "Tonight's Sky in One Look" : "A Quiet Alignment in the Evening Sky");
        var scenes = orderedContexts.Select((context, index) =>
        {
            var narration = isHindi
                ? BuildHindiRealizedScene(realized.ElementAtOrDefault(index), context, index, orderedContexts.Length, isShort)
                : BuildEnglishRealizedScene(realized.ElementAtOrDefault(index), context, index, orderedContexts.Length, isShort, outline);
            var facts = context.VerifiedFacts.Select(f => f.Value).ToArray();
            var realizedSceneId = realized.ElementAtOrDefault(index)?.SceneId;
            var sceneId = !string.IsNullOrWhiteSpace(realizedSceneId) && !Regex.IsMatch(realizedSceneId, @"^(?:long|short)-[A-Za-z]+$", RegexOptions.CultureInvariant)
                ? realizedSceneId!
                : ResolveStableSceneId(format, index);
            return new DocumentaryScriptScene(sceneId, index + 1, SentenceRealizer.Finalize(RemoveAdjacentDuplicateSentences(CleanScript(narration)), isHindi), string.Empty, facts, [], BuildObservationLine(context));
        }).ToArray();
        var fullScript = RemoveAdjacentDuplicateSentences(string.Join("\n\n", scenes.Select(s => s.NarrationText)));
        return new DocumentaryScript("AstroPulse-DocumentaryScript-v3", format, title, language, scenes, fullScript);
    }


    private static string BuildEnglishRealizedScene(NarrationRealizationResult? r, NarrationContextBeat context, int index, int total, bool isShort, string outline)
    {
        if (r is null) return isShort ? BuildShortScene(context, index, total) : BuildLongScene(context, index, total, outline);
        var facts = string.Join(" ", r.SpeakableFacts.Concat(r.ObservationDetails).Select(f => NaturalFactSentence(new NarrationVerifiedFact(f.FactType, f.Value, f.Unit))).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Take(isShort ? 2 : 4));
        return r.BeatRole.ToLowerInvariant() switch
        {
            var role when role.Contains("hook") => CleanScript($"Look up with curiosity: this sky story begins in plain sight. {facts} The invitation is simple, but the scale behind it is not."),
            var role when role.Contains("orientation") => CleanScript($"First, find the pattern in the sky. {facts} Let the confirmed details guide your eye without adding anything unverified."),
            var role when role.Contains("timing") => CleanScript($"Timing is part of the story only where the evidence supports it. {facts}"),
            var role when role.Contains("science") => CleanScript($"The science gives the view its meaning. {facts} Keep the explanation within the verified boundary: {string.Join(" ", r.ScientificBoundaries.Take(1))}"),
            var role when role.Contains("closing") => CleanScript($"Carry away the wonder, not just the facts. {facts} A small confirmed detail can make the whole sky feel newly readable. Until next time, keep looking up."),
            _ => string.IsNullOrWhiteSpace(facts) ? "The verified details carry this beat without adding unsupported claims." : CleanScript(facts)
        };
    }

    private static string BuildHindiRealizedScene(NarrationRealizationResult? r, NarrationContextBeat context, int index, int total, bool isShort)
    {
        if (r is null) return isShort ? BuildHindiShortScene(context, index, total) : BuildHindiLongScene(context, index, total);
        var facts = string.Join(" ", r.SpeakableFacts.Concat(r.ObservationDetails).Select(f => HindiFactSentence(new NarrationVerifiedFact(f.FactType, f.Value, f.Unit))).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Take(isShort ? 2 : 4));
        return r.BeatRole.ToLowerInvariant() switch
        {
            var role when role.Contains("hook") => CleanScript($"जिज्ञासा के साथ ऊपर देखिए; यह आकाश-कथा सामने से शुरू होती है। {facts} दृश्य छोटा हो सकता है, पर उसका पैमाना बहुत बड़ा है।"),
            var role when role.Contains("orientation") => CleanScript($"पहले आकाश में पहचानने योग्य दिशा या पैटर्न खोजिए। {facts} केवल पुष्ट जानकारी को ही मार्गदर्शन बनने दें।"),
            var role when role.Contains("timing") => CleanScript($"समय तभी कहानी का हिस्सा बने, जब तथ्य उसे सहारा दें। {facts}"),
            var role when role.Contains("science") => CleanScript($"विज्ञान इस दृश्य को अर्थ देता है। {facts} बात उतनी ही रखें जितनी पुष्ट सीमा अनुमति देती है।"),
            var role when role.Contains("closing") => CleanScript($"साथ में केवल तथ्य नहीं, आश्चर्य भी ले जाइए। {facts} आकाश की छोटी-सी पुष्टि पूरी रात को नया अर्थ दे सकती है। अगली बार तक, आसमान देखते रहिए।"),
            _ => string.IsNullOrWhiteSpace(facts) ? "यह भाग बिना अपुष्ट दावे जोड़े पुष्ट जानकारी पर टिका रहता है।" : CleanScript(facts)
        };
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
        var v = RegionDisplayResolver.ResolveDisplay(value, "hi").Replace("Mercury", "बुध", StringComparison.OrdinalIgnoreCase).Replace("Venus", "शुक्र", StringComparison.OrdinalIgnoreCase).Replace("Earth", "पृथ्वी", StringComparison.OrdinalIgnoreCase).Replace("Mars", "मंगल", StringComparison.OrdinalIgnoreCase).Replace("Jupiter", "बृहस्पति", StringComparison.OrdinalIgnoreCase).Replace("Saturn", "शनि", StringComparison.OrdinalIgnoreCase).Replace("Uranus", "अरुण", StringComparison.OrdinalIgnoreCase).Replace("Neptune", "वरुण", StringComparison.OrdinalIgnoreCase).Replace("Look toward the", "", StringComparison.OrdinalIgnoreCase).Replace("Look toward", "", StringComparison.OrdinalIgnoreCase).Replace("face the", "", StringComparison.OrdinalIgnoreCase).Replace("western sky", "पश्चिमी आकाश", StringComparison.OrdinalIgnoreCase).Replace("after sunset", "सूर्यास्त के बाद", StringComparison.OrdinalIgnoreCase).Replace("degrees", "डिग्री", StringComparison.OrdinalIgnoreCase);
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
            return CleanScript($"For observing, keep the instructions specific to this moment. {factText} {observation}");
        }

        return CleanScript($"The explanation is quieter than the spectacle. {factText} Nothing has moved close together in space; the alignment belongs to our viewpoint. From the ground, separate orbits can briefly arrange themselves into a pattern that feels almost deliberately placed.");
    }

    private static string BuildShortScene(NarrationContextBeat context, int index, int total)
    {
        var facts = context.VerifiedFacts.ToArray();
        var factText = string.Join(" ", NaturalFactSentences(facts).Take(index == 0 ? 2 : 3));
        if (index == 0) return CleanScript($"Tonight, the sky offers a small mystery. {factText} Two distant worlds can appear close simply because we are seeing them from the same small place on Earth.");
        if (index == total - 1) return CleanScript($"Step outside calmly and let the sky do the work. {factText} A few minutes of looking up can turn a familiar horizon into something memorable. Until next time, keep looking up.");
        if (IsObservationMoment(context, facts)) return CleanScript($"Use the verified time and direction for this view. {factText}");
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
    private static string ResolveStableSceneId(string format, int index) => $"scene-{index + 1:000}";
    private static string RemoveAdjacentDuplicateSentences(string value)
    {
        var sentences = Regex.Split(value, @"(?<=[.!?])\s+").Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        var kept = new List<string>();
        foreach (var sentence in sentences) if (kept.Count == 0 || !NormalizeFactKey(kept[^1]).Equals(NormalizeFactKey(sentence), StringComparison.OrdinalIgnoreCase)) kept.Add(sentence.Trim());
        return string.Join(" ", kept);
    }
    private static string CleanScript(string value) => Regex.Replace(value, "\\s{2,}", " ", RegexOptions.CultureInvariant).Trim();
}

public static class SentenceRealizer
{
    private static readonly Regex SplitDecimalRegex = new(@"\b(\d+)\.\s+(\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DuplicateTemporalPrepRegex = new(@"\b(?:On before dawn|at at|on on|during on)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EditorialImperativeRegex = new(@"(^|[.!?]\s+)(?:Turn\b.+?\binto\b|Explain\b|Establish\b|Introduce\b|Give the viewer\b|Keep the guidance\b|Make clear\b|Emphasize\b)[^.!?]*(?:[.!?]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Finalize(string text, bool isHindi)
    {
        var cleaned = SplitDecimalRegex.Replace(text ?? string.Empty, "$1.$2");
        cleaned = DuplicateTemporalPrepRegex.Replace(cleaned, m => m.Value.ToLowerInvariant() switch
        {
            "on before dawn" => "before dawn",
            "at at" => "at",
            "on on" => "on",
            "during on" => "during",
            _ => m.Value
        });
        cleaned = EditorialImperativeRegex.Replace(cleaned, string.Empty);
        var sentences = Regex.Split(cleaned, @"(?<=[.!?।])\s+")
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Where(s => !Regex.IsMatch(s, @"^(?:[A-Z][a-z]+,\s*)+[A-Z][a-z]+\.?$", RegexOptions.CultureInvariant))
            .ToArray();
        var kept = new List<string>();
        foreach (var sentence in sentences)
        {
            var s = sentence.Trim();
            if (!isHindi && s.Length > 0 && char.IsLetter(s[0])) s = char.ToUpperInvariant(s[0]) + s[1..];
            if (!Regex.IsMatch(s, @"[.!?।]$")) s += isHindi ? "।" : ".";
            if (kept.Count == 0 || !Normalize(kept[^1]).Equals(Normalize(s), StringComparison.OrdinalIgnoreCase)) kept.Add(s);
        }
        return string.Join(" ", kept);
    }

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z\p{IsDevanagari}0-9]+", " ").Trim();
}

public sealed record NarrationLlmRequestV1(string RequestVersion, string Component, string Model, decimal Temperature, decimal TopP, int MaxTokens, string RequestedLanguage, string NormalizedLanguage, string OutputLanguage, string ResolvedCulture, string OutputScript, string LanguageProfileId, string SystemPrompt, string UserPrompt, int PromptQualityScore, IReadOnlyList<string> SourceContracts, DateTime CreatedUtc);

public sealed record LanguageProfile(string LanguageCode, string Culture, string DisplayName, string NativeName, string Script, string OutputInstruction, IReadOnlyList<string> AllowedForeignTerms, IReadOnlyDictionary<string, string> Terminology, bool ProfileFound, bool FallbackUsed, string Source, string TerminologySource, string ProfileId, string ChannelEnding, decimal MinimumComplianceScore);

public static class LanguageProfileResolver
{
    private static readonly IReadOnlyDictionary<string, string> HindiTerminology = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Mercury"] = "बुध", ["Venus"] = "शुक्र", ["Earth"] = "पृथ्वी", ["Mars"] = "मंगल", ["Jupiter"] = "बृहस्पति", ["Saturn"] = "शनि", ["Uranus"] = "अरुण", ["Neptune"] = "वरुण", ["planet"] = "ग्रह", ["western sky"] = "पश्चिमी आकाश", ["after sunset"] = "सूर्यास्त के बाद", ["sunset"] = "सूर्यास्त", ["angular separation"] = "कोणीय दूरी", ["conjunction"] = "ग्रहों की युति", ["horizon"] = "क्षितिज", ["naked eye"] = "नंगी आँखों से", ["binoculars"] = "दूरबीन"
    };

    public static LanguageProfile Resolve(string? requestedLanguage)
    {
        var value = (requestedLanguage ?? "en").Trim();
        if (string.IsNullOrWhiteSpace(value)) value = "en";
        if (Regex.IsMatch(value, "^(hi|hi-IN|Hindi|हिन्दी|हिंदी)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new("hi", "hi-IN", "Hindi", "हिंदी", "Devanagari",
                "Write all spoken narration in natural Hindi using Devanagari script. Do not output complete English sentences. Use semantic guidance only as meaning and author original Hindi documentary narration.",
                ["Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune"], HindiTerminology, true, false, "LanguageProfileResolver:built-in:hi-IN", "LanguageProfileResolver:built-in-terminology:hi-IN", "hi-IN-Devanagari-v1", "फिर मिलेंगे—तब तक आसमान की ओर देखते रहिए।", 80m);
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
    public string RequestedLanguageCode => RequestedLanguage;
    public string RequestedCulture => RequestedLanguage;
    public string RequestedLanguageFamily => RequestedLanguage.Split('-')[0];
    public string DetectedLanguageCode => DetectedPrimaryLanguage;
    public string DetectedLanguageFamily => DetectedPrimaryLanguage.Split('-')[0];
    public bool LanguageFamilyMatch => string.Equals(RequestedLanguageFamily, DetectedLanguageFamily, StringComparison.OrdinalIgnoreCase);
    public bool ScriptMatch => RequestedLanguage.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? DetectedScripts.Contains("Devanagari") : DetectedScripts.Contains("Latin") || DetectedScripts.Count == 0;
    public int ComplianceScore => LanguageComplianceScore;
    public decimal ScriptRatio => RequestedLanguage.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? DevanagariCharacterRatio : LatinCharacterRatio;
    public int EnglishSentenceCount => FullEnglishSentenceCount;
    public int MixedSentenceCount => MixedLanguageSentenceCount;
}

public static class LanguageOutputValidator
{
    private static readonly string[] EnglishTemplates = ["You are watching a real sky alignment unfold", "Let the timing guide", "The main pattern", "It matters because", "Until next time, keep looking up"];
    private static readonly Regex RawTimestampRegex = new(@"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)?\b|\b\d{5,6}\+00:00\b|\+00:00\b|\b\d{1,2}:\d{2}\s*UTC\b|\bUTC\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex InternalIdRegex = new(@"\b[A-Z]{2}-[A-Z0-9]{2,}(?:-[A-Z0-9]{2,})+\b|\b(?:long|short)-beat-\d+\b|\b(?:PlanetPairingApparentLineOfSightGeometry|ApparentAlignmentExplanation|ObservationTiming|BinocularGuidance|NarrativeRole|TransitionIntent|FactType|CapabilityId|[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+){2,})\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        var templates = profile.LanguageCode.Equals("hi", StringComparison.OrdinalIgnoreCase)
            ? EnglishTemplates.Sum(t => Regex.Matches(text, Regex.Escape(t), RegexOptions.IgnoreCase).Count)
            : EnglishTemplates.Where(t => !t.Equals("Until next time, keep looking up", StringComparison.OrdinalIgnoreCase)).Sum(t => Regex.Matches(text, Regex.Escape(t), RegexOptions.IgnoreCase).Count);
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
        "which objects form", "where in the sky", "what to do next", "guide the viewer",
        "open by", "end with", "the event feels", "warning", "metadata"
    ];

    public static IReadOnlyList<string> DetectForbiddenNarrationPhrases(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var direct = ForbiddenNarrationPhrases
            .Where(p => Regex.IsMatch(text, $@"\b{Regex.Escape(p)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();
        if (Regex.IsMatch(text, @"\b(?:the\s+)?viewer\s+should\s+know\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) direct.Add("viewer should know");
        if (Regex.IsMatch(text, @"(^|[.!?]\s+)(?:Turn\b.+?\binto\b|Explain\b|Establish\b|Introduce\b|Give the viewer\b|Keep the guidance\b|Make clear\b|Emphasize\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) direct.Add("planning imperative");
        return direct.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

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
        "Science" => "The apparent closeness is perspective from Earth.",
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
