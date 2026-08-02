# O2.ORCH.6.2.H2 — Phase 6 committed-input certification results

## Execution identity

- **UTC execution timestamp:** 2026-08-02T06:20:14Z–2026-08-02T06:35:00Z
- **Branch:** `work`
- **Certified source commit:** `af22221`
- **Final verdict:** `PHASE6_COMMITTED_INPUT_BOUNDARY_STILL_FAILING`

## Governing documents reviewed

- `Architecture/RC2-Phase-Output-Contract-v1.0.md`
- `docs/implementation/Drashyam_RC2_Pipeline_Development_Guide_v1.1_Final.docx`
- `docs/implementation/Drashyam_Artifact_Contract_Reference_v1.0.docx`
- `docs/implementation/o2-orch-phase-6-story-frame-audit.md`
- `docs/audits/o2-orch-phase-4-documentary-blueprint-audit.md`
- `docs/audits/o2-orch-5-phase5-hardening.md`

The result was reviewed against the same sources after implementation. The frozen rules remain: one authority owner per phase, certified artifacts only downstream, and no upstream mutation.

## Change inventory

### Files added

None. The required `StoryFrameIntegrationCommittedInputTests.cs` is still absent; that is a certification blocker.

### Files modified

- `Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/StoryFrameAuthorityContracts.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`
- `Backend/tests/Astronomy.MediaFactory.Tests/Phase6InputAuthorityEvaluatorTests.cs`
- `Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelinePhase6RoutingTests.cs`
- `docs/implementation/o2-orch-phase-6-p6-2-certification-results.md`

## Production boundary result

The production constructor now requires a non-optional `IPhase6InputAuthorityEvaluator`; the nullable default and `P6INPUT_EVALUATOR_UNAVAILABLE` runtime branch were removed. DI retains exactly one scoped registration. A typed `Phase6InputAuthorityException` owns `ReasonCode`, immutable deterministic `Errors`, and a deterministic message. The Phase 6 route throws this type for invalid/null evaluator authority, catches it separately, puts the exact code on `ProductionPhaseResult.ReasonCode`, and passes it to the validation serializer's `reasonCode` field without parsing a message.

The preserved route is:

`ProductionPipelineExecutionService → ExecutePhase6Async → ExecuteLockedPhase6Async → PhaseChronicleDocumentaryArchitectCoreAsync → IPhase6InputAuthorityEvaluator.EvaluateAsync → IStoryFrameIntegrationService.BuildAsync → StoryFrameArtifactValidator → atomic Phase 6 commit`.

## Evidence and policy review

- **Phase 4 evidence source:** typed `Phase4CommittedAuthorityEvaluation`, including committed validation and manifest inventory.
- **Phase 5 evidence source:** typed `Phase5CommittedStateEvaluation`, including its seven-artifact inventory, committed validation, and phase manifest evidence.
- **Publication identity:** the Phase 5 committed validation transaction ID exposed as `PublicationTransactionId`, not an editorial identity.
- **Committed-state gate:** typed `PublicationCommitted` and `CommittedStateValidationPassed` values.
- **Canonical variants:** case-insensitive input normalization; duplicate requests deduplicate; output order is `Long`, `Short`; unallowed variants reject.
- **Scene-ID comparison:** ordinal. Long and Short remain independent source collections. The current evaluator performs typed evidence checks, but the requested 95-test exhaustive reconciliation certification was not delivered.
- **Variant scene order:** corresponding committed Long/Short collections are the intended validator source. P6.3 mapping redesign was not started.
- **Diagnostics:** safe, relative, deterministic, distinct committed authority paths; certification diagnostics, story graph, absolute, staging, and backup paths are excluded.
- **Cancellation:** `OperationCanceledException` is outside evaluator normalization filters and outside Phase 6 routing failure filters, so cancellation propagates.
- **Upstream immutability:** architecture requires Phase 4/5 read-only use, but the requested focused byte/timestamp snapshot routing suite was not delivered; certification therefore does not claim this as fully tested.

## DI verification

Source and focused tests confirm one evaluator registration and that the constructor parameter has no default value. The focused routing file still does not execute the real public Phase 6 production route, and the requested isolated missing-registration service-provider test is absent. This remains a blocker.

## Command results

A one-time `dotnet restore Backend/Astronomy.MediaFactory.slnx` was required because the .NET 10 SDK and NuGet assets were initially absent. The prompt names `Backend/Astronomy.MediaFactory.sln`, which does not exist; the repository solution is `.slnx`. Also, .NET 10 rejects `--logger "console;verbosity=normal"` for `dotnet build`, so the equivalent `--verbosity normal` was used. Test filters were not weakened.

| Check | Exact command | Total | Passed | Failed | Skipped | Duration |
|---|---|---:|---:|---:|---:|---:|
| Production build | `dotnet build Backend/Astronomy.MediaFactory.slnx --no-restore --verbosity minimal` | N/A | N/A | 0 compile errors | N/A | 1m 49.61s |
| Evaluator | `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~Phase6InputAuthorityEvaluatorTests" --logger "console;verbosity=minimal"` | 3 | 3 | 0 | 0 | 112ms (16.312s process) |
| Routing | `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~ProductionPipelinePhase6RoutingTests" --logger "console;verbosity=minimal"` | 2 | 2 | 0 | 0 | 306ms (16.746s process) |
| Committed-input integration | `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~StoryFrameIntegrationCommittedInputTests" --logger "console;verbosity=minimal"` | 0 | 0 | 0 | 0 | 14.050s process; no tests matched |
| Story Frame | `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~StoryFrame" --logger "console;verbosity=minimal"` | 62 | 61 | 1 | 0 | 14s (29.620s process) |
| Phase 6 | `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~Phase6" --logger "console;verbosity=minimal"` | 36 | 34 | 2 | 0 | 1s (18.121s process) |
| Phase 4 regression | `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~Phase4|FullyQualifiedName~DocumentaryBlueprint" --logger "console;verbosity=minimal"` | 1,051 | 1,050 | 1 | 0 | 1m 45s (121.239s process) |
| Phase 5 regression | `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~Phase5|FullyQualifiedName~PublicationTransaction" --logger "console;verbosity=minimal"` | 133 | 132 | 1 | 0 | 12s (27.566s process) |
| Pipeline regression | `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~ProductionPipelineExecutionServiceTests" --logger "console;verbosity=minimal"` | 142 | 142 | 0 | 0 | 3s (23.924s process) |
| Complete project | `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore --no-build --logger "console;verbosity=minimal"` | 4,965 | 4,530 | 424 | 11 | 5m 30s (353.600s process) |

## Focused/regression failed tests

- `Astronomy.MediaFactory.Tests.VisualQualityFrameworkTests.StoryFramePromptBuilders_ConsumeVisualQualityFrameworkWithoutRenderingChanges`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastV2SceneRenderingTests.Orchestrator_DispatchesPhase6BRenderers_WithoutPlaceholderFallback`
- `Astronomy.MediaFactory.Tests.Phase5CommittedAuthorityArchitectureTests.phase6_does_not_require_optional_certification_diagnostics`
- `Astronomy.MediaFactory.Tests.Rc2StoryIntelligenceTests.Phase4And5_BuildStoryGraphThenEditorialIntelligence`

The Phase 5 architecture failure is also selected by the Phase 6 filter. Complete-project execution produced the exact 424 failures listed below; these include the focused failures.

## Exact complete-project failures

- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_Fails_WhenCombinedDurationDoesNotMatchNarration`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_AutoDisablesTransitions_WhenSceneDurationTooShort`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_EffectsDisabled_ProducesPlainScaledVideo`
- `Astronomy.MediaFactory.Tests.VideoAssemblyIntelligenceServiceTests.GenerateVideoAssemblyAsync_RenderDryRunReportsRenderPolishValidation`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_AdjustsSceneDuration_ForTransitionOverlap`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_UsesExpectedOutputSize_ForShortsAndLong`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_WritesProcessDiagnosticsToLog`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_DerivesFfprobePath_FromFfmpegPath`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_CalculatesEffectiveSegmentTimeout(configuredSeconds: 180, sceneDurationSeconds: 100, expectedSeconds: 1000)`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_CalculatesEffectiveSegmentTimeout(configuredSeconds: 450, sceneDurationSeconds: 20, expectedSeconds: 450)`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_CalculatesEffectiveSegmentTimeout(configuredSeconds: 180, sceneDurationSeconds: 36, expectedSeconds: 360)`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_CalculatesEffectiveSegmentTimeout(configuredSeconds: 30, sceneDurationSeconds: 10, expectedSeconds: 300)`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_SegmentedKenBurnsAndFade_AreVideoOnlyEffects`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_UsesNarrationDuration_ForSceneSegments`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_DoesNotApplyAtempoCompression_ByDefault`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_WritesRenderPerformanceReport_ForIntermediateSegments`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_AddsDirectionalPan_WhenDirectionalMotionEnabled`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_WritesFinalRenderDiagnostics`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_UsesExplicitFfprobePath_WhenConfigured`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_ShortVideoUsesPortraitSafeEffects`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_LongVideoUses1440pProductionPreset_WhenEnabled`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_DisablesKenBurns_WhenConfigured`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_ShortsUsePortraitProductionPreset`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_FinalLongRenderUsesConfiguredFinalLongTimeout`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_Throws_WhenFfmpegFails`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_MetaReelFinal_UsesMetaProfile`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_WritesEncodingReport_WithMinimumBitrateAndYuv420p`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_FallsBackToBareFfprobe_WhenNoPathConfigured`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_WritesSpeechSpeedDiagnostics`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_SegmentRenderStillUsesSegmentTimeout`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_FinalLongTimeoutDoesNotScaleWithVideoDuration`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_DoesNotAdjustSceneDuration_WhenTransitionsDisabled`
- `Astronomy.MediaFactory.Tests.FfmpegRenderingTests.FfmpegVideoRenderService_KeepsStableZoomCentered_WhenDirectionalMotionDisabled`
- `Astronomy.MediaFactory.Tests.VideoAssemblyIntelligenceServiceTests.GenerateVideoAssemblyAsync_TtsNonDryRunRejectsSyntheticSilentAudioBeforeFinalOutputs`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastV2IntelligenceTests.V2_Intelligence_Generates_Cinematic_Blueprint`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastV2IntelligenceTests.V2_NarrationPlan_Has_Expected_Segments_Durations_And_Strategies`
- `Astronomy.MediaFactory.Tests.VideoAssemblyIntelligenceServiceTests.GenerateVideoAssemblyAsync_LongFormTtsUsesAzureAndWritesActualSectionTimings`
- `Astronomy.MediaFactory.Tests.AstronomyContextProviderTests.BuildContextAsync_HybridMode_UsesAttractiveObjectAsOverviewHook`
- `Astronomy.MediaFactory.Tests.VideoAssemblyIntelligenceServiceTests.GenerateVideoAssemblyAsync_DryRunReturnsPreviewPathWithoutWriting`
- `Astronomy.MediaFactory.Tests.VideoAssemblyIntelligenceServiceTests.GenerateVideoAssemblyAsync_TtsNonDryRunWithoutRealProviderFailsClearly`
- `Astronomy.MediaFactory.Tests.VideoAssemblyIntelligenceServiceTests.GenerateVideoAssemblyAsync_ScriptNonDryRunWritesNarrationScriptOnly`
- `Astronomy.MediaFactory.Tests.AstronomyContextProviderTests.BuildContextAsync_UsesMidpointFallback_WhenNoSamplesProvided`
- `Astronomy.MediaFactory.Tests.VideoAssemblyIntelligenceServiceTests.GenerateVideoAssemblyAsync_AssemblyNonDryRunWritesPlanOnly`
- `Astronomy.MediaFactory.Tests.VideoAssemblyIntelligenceServiceTests.SceneLevelSubtitleBlocks_SplitSingleSceneNarrationIntoReadableDisplayCues`
- `Astronomy.MediaFactory.Tests.VideoAssemblyIntelligenceServiceTests.GenerateVideoAssemblyAsync_AssemblyFailsWhenVisualAssetMissing`
- `Astronomy.MediaFactory.Tests.VideoAssemblyIntelligenceServiceTests.GenerateVideoAssemblyAsync_IntelligenceNonDryRunWritesVideoAssemblyIntelligenceOnly`
- `Astronomy.MediaFactory.Tests.CgA1CertificationDiTests.FoundationRegistrationSucceedsAndDoesNotRegisterMissingConcreteServices`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.ProductionQualityValidator_AcceptsMeteorRequiredObjectAliasesAndPurposeAwareScenes`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.ProductionQualityContainsToken_IgnoresPunctuationAndExtraWhitespace(text: "Snow-Moon", token: "Snow Moon", expected: True)`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.ProductionQualityContainsToken_IgnoresPunctuationAndExtraWhitespace(text: "Snow    Moon", token: "Snow Moon", expected: True)`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.ProductionQualityContainsToken_IgnoresPunctuationAndExtraWhitespace(text: "Snow Moon", token: "Snow Moon", expected: True)`
- `Astronomy.MediaFactory.Tests.KnowledgeFoundation.KnowledgeStatementValidationTests.Foundational_validator_is_payload_type_neutral_and_public_api_stays_within_task_21c_scope`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.ProductionQualityValidator_AcceptsAssetKeyWithoutObjectVisualSourceForRequiredCelestialObject`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.ProductionQualityValidator_AcceptsSnowMoonShortTitleMetadataForPhase10`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.ProductionQualityValidator_PlanetGroupingValidatesIndividualVisibleObjectsOnly`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.AstronomyAdapter_RoutesPlanetGroupingToDedicatedStrategy`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.ProductionQualityValidator_PrefersSceneApprovalStagingRootForPhase10`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.ProductionQualityValidator_UsesSceneValidationStrategyForGeminids`
- `Astronomy.MediaFactory.Tests.EventProductionIntelligenceTests.QuestionDrivenVisualSpec_SerializesMeteorTimingStrategyValidationFacts`
- `Astronomy.MediaFactory.Tests.KnowledgeFoundation.EvidenceAndConfidenceSerializationTests.Task21_and_task1_enum_serialization_contracts_are_preserved`
- `Astronomy.MediaFactory.Tests.NarrationContextPurityTests.RawIsoTimestampFact_IsConvertedSafely`
- `Astronomy.MediaFactory.Tests.Phase7ProductionResolverInputParityTests.RealPhase7EntryPoint_PreservesCompleteTypedSemanticContext`
- `Astronomy.MediaFactory.Tests.AstronomyFamilyProfileV1CompatibilityAdapterTests.MeteorShowerPreservesCurrentRequirements`
- `Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.AstronomyKnowledgeValidationArchitectureTests.ValidationFoundationFiles_ExistAndAvoidInfrastructureTerms`
- `Astronomy.MediaFactory.Tests.StructuredFieldProjectionRegressionTests.Missing_Optional_MeteorActivityZhr_Does_Not_Project_Filler`
- `Astronomy.MediaFactory.Tests.YouTubePublishingIntegrationTests.ThumbnailOverTwoMb_IsCompressedBeforeUpload`
- `Astronomy.MediaFactory.Tests.YouTubePublishingIntegrationTests.WrongThumbnailExtension_IsSkippedWithWarning`
- `Astronomy.MediaFactory.Tests.CanonicalEventIdentityResolverTests.UnsupportedTypesAreUnsupportedNotMissing(eventType: "PlanetGrouping")`
- `Astronomy.MediaFactory.Tests.CanonicalEventIdentityResolverTests.TitleBasedFamilyInferenceIsNotUsed`
- `Astronomy.MediaFactory.Tests.CanonicalEventIdentityResolverTests.SupportedTypesResolveFromCanonicalIdentity(eventType: "SolarEclipse", expectedProfile: "Eclipse")`
- `Astronomy.MediaFactory.Tests.FamilyResolutionV1IntegrationTests.UnknownEventTypeReportsUnsupportedEventTypeOnly`
- `Astronomy.MediaFactory.Tests.FamilyResolutionV1IntegrationTests.DiCreatedCatalogValidationSucceeds`
- `Astronomy.MediaFactory.Tests.FamilyResolutionV1IntegrationTests.SemanticRuntimeRegistersOnlyV1SemanticRuntimeServices`
- `Astronomy.MediaFactory.Tests.KnowledgeFoundation.Query.AstronomyKnowledgeQueryArchitectureTests.No_reverse_dependency_from_catalog_or_typed_domains_into_query`
- `Astronomy.MediaFactory.Tests.CanonicalEventIdentityResolverV1Tests.RuntimeUsesV1EventIdentityOnlyThroughApprovedServices`
- `Astronomy.MediaFactory.Tests.Phase5CommittedAuthorityArchitectureTests.phase6_does_not_require_optional_certification_diagnostics`
- `Astronomy.MediaFactory.Tests.TypedKnowledgeDomainFoundationTests.Task23AProductionBoundaryHasNoForbiddenDependenciesOrBehaviors`
- `Astronomy.MediaFactory.Tests.Rc2StoryIntelligenceTests.Phase4And5_BuildStoryGraphThenEditorialIntelligence`
- `Astronomy.MediaFactory.Tests.SceneAssetsV3ServiceTests.GenerateAsync_AccurateSkyGuideV2Enabled_CallsProviderForOnlyGuideScenesAndWritesDiagnostics`
- `Astronomy.MediaFactory.Tests.TokenHealthServiceTests.YouTubeTokenFilePreferredOverConfiguredRefreshToken`
- `Astronomy.MediaFactory.Tests.TokenHealthServiceTests.YouTubeDiagnosticsNeverExposeFullSecret`
- `Astronomy.MediaFactory.Tests.TokenHealthServiceTests.YouTubeTokenHealthUsesLatestTokenFileMetadata`
- `Astronomy.MediaFactory.Tests.TokenHealthServiceTests.YouTubeMismatchWarningEmittedWhenTokenFileDiffersFromConfiguredToken`
- `Astronomy.MediaFactory.Tests.WeeklyAudioDrivenTimelineReconciliationTests.ReconcileAsync_UsesNewRendererContract_WhenLegacyInputsAreMissing`
- `Astronomy.MediaFactory.Tests.WeeklyAudioDrivenTimelineReconciliationTests.ReconcileAsync_UsesActualAudioDurationsAsSegmentSourceOfTruth`
- `Astronomy.MediaFactory.Tests.AzureOpenAiContentGenerationServiceTests.GenerateShortAsync_ReturnsShortPayload_WhenJsonIsValid`
- `Astronomy.MediaFactory.Tests.AzureOpenAiContentGenerationServiceTests.GenerateAsync_FallsBack_WhenModelReturnsUnexpectedProperties`
- `Astronomy.MediaFactory.Tests.SceneAssetsV3ServiceTests.GenerateAsync_ForMeteorShower_UsesMeteorContextAndDoesNotFailOnConjunctionForbiddenList`
- `Astronomy.MediaFactory.Tests.AzureOpenAiContentGenerationServiceTests.GenerateAsync_ReturnsValidatedModelResponse_WhenJsonIsValid`
- `Astronomy.MediaFactory.Tests.SsmlBuilderTests.BuildSsml_EscapesXmlCharacters`
- `Astronomy.MediaFactory.Tests.ThumbnailSelectionSerializationTests.PipelineOrchestrator_WritesThumbnailSelectionWithoutCaseConflictingThumbnailKeys`
- `Astronomy.MediaFactory.Tests.ThumbnailSelectionSerializationTests.CinematicThumbnailService_WritesThumbnailSelectionWithoutCaseConflictingThumbnailKeys`
- `Astronomy.MediaFactory.Tests.ApiHostRuntimeCompositionDiagnosticsTests.ApiHost_GeminidsPhase7ResolverReturnsRadiantAndPeakWindow`
- `Astronomy.MediaFactory.Tests.ThumbnailConfigurationTests.AddMediaFactory_UsesLocalAssetCollage_WhenDeprecatedThumbnailAiSectionsExist`
- `Astronomy.MediaFactory.Tests.WeeklySscSceneBuilderTests.GroupingBuild_UsesCanonicalPattern_AndValidationRequirements`
- `Astronomy.MediaFactory.Tests.AstronomyQuestionEngineTests.GenerateQuestionAnswersAsync_StrategyDrivenEventTypesPassValidation(eventCode: "MARS_JUPITER_2026", eventType: "PlanetPairing", title: "Mars Jupiter Pairing", objects:`
- `Astronomy.MediaFactory.Tests.AstronomyQuestionEngineTests.GenerateQuestionAnswersAsync_StrategyDrivenEventTypesPassValidation(eventCode: "NEW_MOON_2026", eventType: "NewMoon", title: "New Moon", objects:`
- `Astronomy.MediaFactory.Tests.AstronomyQuestionEngineTests.GenerateQuestionAnswersAsync_StrategyDrivenEventTypesPassValidation(eventCode: "SOLAR_ECLIPSE_2026", eventType: "SolarEclipse", title: "Partial Solar Eclipse", objects:`
- `Astronomy.MediaFactory.Tests.AstronomyQuestionEngineTests.GenerateQuestionAnswersAsync_StrategyDrivenEventTypesPassValidation(eventCode: "MOON_SATURN_CONJUNCTION_2026", eventType: "Conjunction", title: "Moon Saturn Conjunction", objects:`
- `Astronomy.MediaFactory.Tests.AstronomyQuestionEngineTests.ValidateQuestionAnswerSetAsync_ApprovesGoldenRareEventPlanetConjunctionPilot`
- `Astronomy.MediaFactory.Tests.AstronomyQuestionEngineTests.GenerateQuestionAnswersAsync_PlanetGroupingWithoutDirectionUsesHorizonArcGuidanceAndPassesValidation`
- `Astronomy.MediaFactory.Tests.AstronomyQuestionEngineTests.GenerateQuestionAnswersAsync_UsesBrightPlanetPairingWhyWhenConjunctionSeparationIsMissing`
- `Astronomy.MediaFactory.Tests.AstronomyQuestionEngineTests.ValidateQuestionAnswerSetAsync_ApprovesPlanetConjunctionWhyAlignmentSignificance`
- `Astronomy.MediaFactory.Tests.AstronomyQuestionEngineTests.GenerateQuestionAnswersAsync_UsesViewerFriendlyOverlayAnswersForClosePlanetPairing`
- `Astronomy.MediaFactory.Tests.SceneAssetsV3ServiceTests.GenerateAsync_ForPlanetConjunction_WritesConjunctionTimelineAndRejectsForbiddenMeteorTerms`
- `Astronomy.MediaFactory.Tests.AstroPulseGalleryServiceTests.GalleryV3_LoadContext_RequestLanguageOverridesEnglishIntelligenceForHindiPhase13`
- `Astronomy.MediaFactory.Tests.AstroPulseGalleryServiceTests.GalleryV3_JupiterVenusPrompts_RemoveMarsAndRequireRecognizablePlanetTreatment`
- `Astronomy.MediaFactory.Tests.QuestionSceneIntentEnricherTests.EnrichQuestionScenePlanAsync_WritesEnrichedPlanWhenDryRunIsFalse`
- `Astronomy.MediaFactory.Tests.QuestionSceneIntentEnricherTests.EnrichQuestionScenePlanAsync_DryRunReturnsPreviewWithoutWritingFile`
- `Astronomy.MediaFactory.Tests.QuestionSceneIntentEnricherTests.EnrichQuestionScenePlanAsync_AppliesRequestedAudienceContextToRootAndScenes`
- `Astronomy.MediaFactory.Tests.SemanticSourceAdapterRegistryV1Tests.GetAdapters_Null_Or_Default_Requested_Capability_Produces_Precise_ArgumentException`
- `Astronomy.MediaFactory.Tests.SemanticSourceAdapterRegistryV1Tests.GetAdapters_Blank_Value_Produces_Precise_ArgumentException`
- `Astronomy.MediaFactory.Tests.NarrativeCompositionEngineTests.Compose_Allocates_Configurable_DurationBudget_ToBeats`
- `Astronomy.MediaFactory.Tests.AstronomyAssetProducerPreviewServiceTests.AllCurrentJobs_HaveProducerCoverage_AndNoDbMutationOccurs`
- `Astronomy.MediaFactory.Tests.SemanticSourcePolicyContractsV1Tests.Runtime_Orchestration_Does_Not_Duplicate_Source_Policy_Logic`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastV2TimelineCompositionTests.Orchestrator_ComposesDeterministic110SecondTimeline`
- `Astronomy.MediaFactory.Tests.Phase7SemanticSourceContextIntegrationTests.JupiterVenusTypedProductionRequestWiresSemanticSourceContext`
- `Astronomy.MediaFactory.Tests.FacebookVideoPublishServiceTests.FileAboveThreshold_UsesResumableUpload`
- `Astronomy.MediaFactory.Tests.FacebookVideoPublishServiceTests.ChunkTransfer_LoopsUntilOffsetsComplete`
- `Astronomy.MediaFactory.Tests.FacebookVideoPublishServiceTests.SimpleUpload413_RetriesWithResumableUpload`
- `Astronomy.MediaFactory.Tests.SemanticCapabilityInventoryCertificationV1Tests.Canonical_Inventory_Has_Vocabulary_Catalog_Policy_Adapter_And_Family_Validation_Coverage`
- `Astronomy.MediaFactory.Tests.VisualIntelligenceOrchestratorTests.All_feature_flags_false_writes_summary_only`
- `Astronomy.MediaFactory.Tests.VisualIntelligenceOrchestratorTests.Hero_platform_writes_hero_intelligence_contract_to_run_diagnostics`
- `Astronomy.MediaFactory.Tests.VisualIntelligenceOrchestratorTests.Default_appsettings_keep_visual_intelligence_disabled`
- `Astronomy.MediaFactory.Tests.VisualIntelligenceOrchestratorTests.Hero_platform_writes_fallback_contract_when_v4_inputs_are_missing`
- `Astronomy.MediaFactory.Tests.VisualIntelligenceOrchestratorTests.Production_default_config_remains_no_op`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastV2PhaseDiagnosticsEndpointTests.Endpoint_Returns_AstronomyEvents_Result_For_AstronomyEvents_Phase`
- `Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ObservationalAndVisibility.AstronomyObservationalAndVisibilityValidationArchitectureTests.Production_validation_avoids_forbidden_dependencies`
- `Astronomy.MediaFactory.Tests.WeeklyAICinematicAssetGenerationServiceTests.GenerateAndPersistAsync_SelectsTopBatchAndDefersRemainder_WhenProviderConfigured`
- `Astronomy.MediaFactory.Tests.PipelineOrchestratorSceneNarrationTests.RunAsync_UsesFallbackSceneNarration_WhenScriptOmitsVisualScenes`
- `Astronomy.MediaFactory.Tests.PipelineOrchestratorSceneNarrationTests.RunAsync_FullVideoWithThreeObjects_KeepsClosingNarrationLast`
- `Astronomy.MediaFactory.Tests.PipelineOrchestratorSceneNarrationTests.RunAsync_FullVideoWithFiveObjects_DoesNotUseClosingForAdditionalObjects`
- `Astronomy.MediaFactory.Tests.PipelineOrchestratorSceneNarrationTests.RunAsync_ShortVideoSequenceInput_RemainsUnchanged`
- `Astronomy.MediaFactory.Tests.PipelineOrchestratorSceneNarrationTests.RunAsync_FullVideoWithSpecialEvent_KeepsClosingNarrationLast`
- `Astronomy.MediaFactory.Tests.PipelineOrchestratorSceneNarrationTests.RunAsync_WritesUniqueSceneNarrationArtifacts_AndCombinesOutputs`
- `Astronomy.MediaFactory.Tests.PipelineOrchestratorSceneNarrationTests.RunAsync_FullVideoWithStaleSceneIndex_UsesFinalVisualSceneOrderForNarration`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastV2SceneRenderingTests.Orchestrator_UnknownRendererType_ReturnsValidationError`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastV2SceneRenderingTests.Orchestrator_DispatchesPhase6BRenderers_WithoutPlaceholderFallback`
- `Astronomy.MediaFactory.Tests.StellariumVisualGenerationServiceTests.PrepareVisualsAsync_SkipsObjectScenes_BelowAltitudeThreshold`
- `Astronomy.MediaFactory.Tests.StellariumVisualGenerationServiceTests.BuildSceneScript_UsesSceneObjectName_WhenSelectingTargets`
- `Astronomy.MediaFactory.Tests.StellariumVisualGenerationServiceTests.BuildSceneScript_UsesMinimalLabels_ForDeepSkyScenes`
- `Astronomy.MediaFactory.Tests.StellariumVisualGenerationServiceTests.PrepareVisualsAsync_NestsConfiguredDirectories_ByDateAndPipelineRun`
- `Astronomy.MediaFactory.Tests.StellariumVisualGenerationServiceTests.PrepareVisualsAsync_UsesNightObservationTimes_AndUtcInScripts`
- `Astronomy.MediaFactory.Tests.StellariumVisualGenerationServiceTests.BuildSceneScript_ContainsProjectionLandscapeAndScreenshotTarget`
- `Astronomy.MediaFactory.Tests.StellariumVisualGenerationServiceTests.BuildSceneScript_WithCinematicMotionEnabled_UsesTwoZoomCallsAndWaitsBeforeScreenshot`
- `Astronomy.MediaFactory.Tests.StellariumVisualGenerationServiceTests.PrepareVisualsAsync_GeneratesDailySkyGuideScenesAndManifest`
- `Astronomy.MediaFactory.Tests.StellariumVisualGenerationServiceTests.BuildSceneScript_WithCinematicMotionDisabled_UsesStableZoomBehavior`
- `Astronomy.MediaFactory.Tests.StellariumVisualGenerationServiceTests.BuildSceneScript_EnablesObjectPointer_ForPlanetAndMoonScenes`
- `Astronomy.MediaFactory.Tests.Phase7CanonicalEventDispatchV1Tests.Phase7AcceptsConstellationAliasesAndResolvesToConstellationProfile(eventType: "Constellation")`
- `Astronomy.MediaFactory.Tests.Phase7CanonicalEventDispatchV1Tests.Phase7AcceptsConstellationAliasesAndResolvesToConstellationProfile(eventType: "CONSTELLATION")`
- `Astronomy.MediaFactory.Tests.Phase7CanonicalEventDispatchV1Tests.Phase7AcceptsConstellationAliasesAndResolvesToConstellationProfile(eventType: "constellation")`
- `Astronomy.MediaFactory.Tests.ThumbnailRendererTests.ThumbnailPromptWriterV9_PlanetaryProfile_OnlyRendersEventPlanets`
- `Astronomy.MediaFactory.Tests.ThumbnailRendererTests.ThumbnailPromptWriterV9_JupiterVenus_RestoresV92PromptDensity(language: "hi")`
- `Astronomy.MediaFactory.Tests.ThumbnailRendererTests.ThumbnailPromptWriterV9_JupiterVenus_RestoresV92PromptDensity(language: "en")`
- `Astronomy.MediaFactory.Tests.NightSkyVisibilityPlannerRankingTests.BuildPlan_ExcludesLowAltitudeObjects`
- `Astronomy.MediaFactory.Tests.NightSkyVisibilityPlannerRankingTests.BuildPlan_SelectsDiverseDynamicObjectCount`
- `Astronomy.MediaFactory.Tests.Phase7NarrationHandoffTests.NarrationSafeContext_ContainsMeteorProjectedFacts`
- `Astronomy.MediaFactory.Tests.Phase7NarrationHandoffTests.NarrationRealizer_ConsumesNarrationSafeContextWithoutMissingRadiant`
- `Astronomy.MediaFactory.Tests.PipelineSchedulerTests.EventPlan_Global_Medium_Event_Is_Injected_And_Low_Score_Is_Skipped_With_Reason`
- `Astronomy.MediaFactory.Tests.PipelineSchedulerTests.Approved_AI_Profile_Writes_Optimization_Used_Without_Mutating_Request`
- `Astronomy.MediaFactory.Tests.WeeklyStellariumScriptWriterTests.WriteAsync_Writes_Scripts_And_Diagnostics`
- `Astronomy.MediaFactory.Tests.LanguageOutputValidatorTests.HindiValidator_PassesDevanagariWithApprovedEnglishTerms`
- `Astronomy.MediaFactory.Tests.ProductionVisualComposerServiceTests.GenerateProductionVisualsAsync_DryRun_UsesLocalOverlayTimeCompleteTextAndDistinctScenePrompts`
- `Astronomy.MediaFactory.Tests.SemanticResolutionRuntimeIsolationV1Tests.RuntimeUsesSemanticResolutionEngineWithoutBypassingItsInternalLayers`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_ImageGenerationWritesThumbnailPngsAndValidationOnly`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_BrightPlanetVisibilityCompositionUsesObjectTonightCopy`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_DryRunReturnsPreviewPathWithoutWriting`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_PlanetConjunctionCompositionUsesCompactObjectCopy`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_SceneSelectionWritesThumbnailSceneManifestOnly`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_CompositionWritesReusableThumbnailCompositionModel`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_IntelligenceNonDryRunWritesThumbnailIntelligenceOnly`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_ImagesMarsJupiterPairingUsesOnlyCurrentPlanets`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_PlanetGroupingCompositionUsesVisibleObjectsAndMotifCopy`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_PlanetConjunctionImagesUseCompactThumbnailText`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_Phase12ThumbnailV3DoesNotRequireHeroSceneManifest`
- `Astronomy.MediaFactory.Tests.ThumbnailAssetIntelligenceServiceTests.GenerateThumbnailAssetsAsync_PlanetConjunctionIntelligenceBuildsCleanVisualGuideProfile`
- `Astronomy.MediaFactory.Tests.ExecutionContractRegistryTests.DormantAstronomyCatalogCreatesValidEmptyDomainRegistry`
- `Astronomy.MediaFactory.Tests.Rc2EarlyValidationOwnershipTests.Authoritative_validation_is_preserved_and_not_overwritten_by_generic_validator(phaseNo: 3)`
- `Astronomy.MediaFactory.Tests.GalleryIntelligenceAlignmentEngineTests.EducationalStorytellingDiagnostics_Include_Required_Review_And_Benchmark_Metadata`
- `Astronomy.MediaFactory.Tests.GalleryIntelligenceAlignmentEngineTests.InformationDensityDiagnostics_Include_Required_Review_Sections`
- `Astronomy.MediaFactory.Tests.GalleryIntelligenceAlignmentEngineTests.NarrativeFlowDiagnostics_Include_Required_Review_Sections`
- `Astronomy.MediaFactory.Tests.AstronomyFamilyProfileCatalogV1Tests.CatalogValidationSucceeds`
- `Astronomy.MediaFactory.Tests.AstronomyFamilyProfileCatalogV1Tests.EveryActiveV1FamilyResolvesToItself`
- `Astronomy.MediaFactory.Tests.AstronomyEventDiscoveryPreviewServiceTests.VerifyAstronomyEvents_WritesVerifiedJsonAndDeduplicatesFullMoons`
- `Astronomy.MediaFactory.Tests.ContentPlanningGeneratePlanTests.AssetAwarePackage_Sets_AssetsReady_True_Only_When_Required_Assets_Exist`
- `Astronomy.MediaFactory.Tests.ContentPlanningGeneratePlanTests.ScenePlanner_Returns_Scenes_For_DailySkyGuide_And_WideSky`
- `Astronomy.MediaFactory.Tests.ThumbnailRendererTests.ThumbnailPromptBuilder_AddsArtworkOnlyRules`
- `Astronomy.MediaFactory.Tests.LegacySemanticCapabilityMapV1Tests.Event_Timing_Legacy_Terms_Map_To_EventWindow_Subfields(term: "PeakWindow")`
- `Astronomy.MediaFactory.Tests.MeteorShowerExecutableFamilyCoverageTests.RequiredSemanticFactResolver_MeteorActivityProjectsRadiantAndPeakWindow`
- `Astronomy.MediaFactory.Tests.MeteorShowerExecutableFamilyCoverageTests.ApiHost_Geminids_ResolverRetainsProjectedFactsInBeats`
- `Astronomy.MediaFactory.Tests.MeteorShowerExecutableFamilyCoverageTests.RequiredSemanticFactResolver_MeteorShowerRequestsMeteorActivity`
- `Astronomy.MediaFactory.Tests.MeteorShowerExecutableFamilyCoverageTests.ApiHost_Geminids_Phase7PrePromptParityPasses`
- `Astronomy.MediaFactory.Tests.HeroImageV4ComparisonTests.Flag_true_generates_comparison_artifacts_without_changing_production_hero`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_StoryDryRunDoesNotRequireExistingHeroFiles`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.HeroV65TitleResolver_HindiUsesLocalizedEventTitleInsteadOfGenericHook(eventType: "PlanetConjunction", title: "Jupiter Mars Conjunction", primaryObject: "Jupiter", secondaryObject: "Mars", expectedTitle: "बृहस्पति और मंगल")`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.HeroV65TitleResolver_HindiUsesLocalizedEventTitleInsteadOfGenericHook(eventType: "MeteorShower", title: "Perseids Meteor Shower Peak", primaryObject: "Perseids", secondaryObject: "", expectedTitle: "पर्सिड्स उल्का वर्षा")`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.HeroV65TitleResolver_HindiUsesLocalizedEventTitleInsteadOfGenericHook(eventType: "PlanetConjunction", title: "Jupiter Venus Conjunction", primaryObject: "Jupiter", secondaryObject: "Venus", expectedTitle: "बृहस्पति और शुक्र")`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.HeroV65TitleResolver_HindiUsesLocalizedEventTitleInsteadOfGenericHook(eventType: "SolarEclipse", title: "Total Solar Eclipse", primaryObject: "Sun", secondaryObject: "Moon", expectedTitle: "पूर्ण सूर्य ग्रहण")`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.HeroV65TitleResolver_HindiUsesLocalizedEventTitleInsteadOfGenericHook(eventType: "MeteorShower", title: "Geminids Meteor Shower Peak", primaryObject: "Geminids", secondaryObject: "", expectedTitle: "जेमिनिड्स उल्का वर्षा")`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_BlueprintDryRunReturnsBlueprintWithoutGeneratingImages`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.HeroV65TitleResolver_EnglishKeepsEventSpecificTitleBehavior(eventType: "MeteorShower", title: "Perseids Meteor Shower Peak", expectedTitle: "PERSEIDS METEOR SHOWER PEAK")`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.HeroV65TitleResolver_EnglishKeepsEventSpecificTitleBehavior(eventType: "PlanetConjunction", title: "Jupiter Venus Conjunction", expectedTitle: "JUPITER VENUS CONJUNCTION")`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.HeroV65TitleResolver_EnglishKeepsEventSpecificTitleBehavior(eventType: "SolarEclipse", title: "Total Solar Eclipse", expectedTitle: "TOTAL SOLAR ECLIPSE")`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_HookSelectionDryRunReturnsHooksWithoutGeneratingBlueprintOrImages`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_ImagesNonDryRunLoadsStoryAndBlueprintGeneratesImagesAndReviewDiagnostics`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.HeroV65TitleResolver_PlanetGroupingUsesCompactObjectHeadlineAndShortSubtitle`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_BlueprintStringPhaseTrimsNormalizesAndGeneratesStoryWhenMissing`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_StoryNonDryRunWritesHeroStoryOnly`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_MeteorStrategyDoesNotRequireGeminidsOrMeteorsAssetFiles`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_BlueprintSaveModeWritesBlueprintJsonOnlyAndUpdatesHeroStoryHook`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_HookSelectionNonDryRunDoesNotWriteBlueprintOrImages`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_SceneSelectionNonDryRunWritesHeroSceneManifestOnly`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetStoryAsync_DryRunReturnsPreviewOnlyAndUsesWhatWhereWhenWhySources`
- `Astronomy.MediaFactory.Tests.HeroAssetStoryGeneratorTests.GenerateHeroAssetsAsync_SceneSelectionPrefersNormalizedLongSceneAssetsOverStagedFinalAssets`
- `Astronomy.MediaFactory.Tests.ShortsVideoRenderServiceTests.RenderAsync_BindsNarrationToMatchingSceneId_NotByIndex`
- `Astronomy.MediaFactory.Tests.ShortsVideoRenderServiceTests.RenderAsync_Throws_WhenShortNarrationOrderDoesNotMatchVisualSceneOrder`
- `Astronomy.MediaFactory.Tests.ShortsVideoRenderServiceTests.RenderAsync_UsesSceneBasedNarrationSegments_WhenSegmentSynthesisSucceeds`
- `Astronomy.MediaFactory.Tests.NarrationInputNormalizerTests.SameNormalizerBuildsSafeContextForAstronomyFamilies(name: "Jupiter–Venus Hindi", language: "hi", objects: "Jupiter and Venus", time: "2026-08-12T00:00:00+00:00", direction: "W", separation: "1.63", region: "IN-RJ-UDAIPUR")`
- `Astronomy.MediaFactory.Tests.NarrationInputNormalizerTests.SameNormalizerBuildsSafeContextForAstronomyFamilies(name: "publish-window JSON", language: "en", objects: "Jupiter and Venus", time: "{\"recommendedPublishWindow\":\"2026-08-10T00:00:0"···, direction: "SE", separation: "1.63", region: "IN-RJ-UDAIPUR")`
- `Astronomy.MediaFactory.Tests.NarrationInputNormalizerTests.SameNormalizerBuildsSafeContextForAstronomyFamilies(name: "Mars–Jupiter English", language: "en", objects: "Mars and Jupiter", time: "2026-11-16T00:00:00+00:00", direction: "SE", separation: "1.19", region: "IN-RJ-UDAIPUR")`
- `Astronomy.MediaFactory.Tests.NarrationInputNormalizerTests.SameNormalizerBuildsSafeContextForAstronomyFamilies(name: "Jupiter–Venus English", language: "en", objects: "Jupiter and Venus", time: "2026-08-12T00:00:00+00:00", direction: "W", separation: "1.63", region: "IN-RJ-UDAIPUR")`
- `Astronomy.MediaFactory.Tests.NarrationInputNormalizerTests.SameNormalizerBuildsSafeContextForAstronomyFamilies(name: "verified local time", language: "en", objects: "Mars and Jupiter", time: "2026-11-16 05:30 +0530", direction: "SE", separation: "1.19", region: "IN-RJ-UDAIPUR")`
- `Astronomy.MediaFactory.Tests.NarrationInputNormalizerTests.SameNormalizerBuildsSafeContextForAstronomyFamilies(name: "no timing requirement", language: "en", objects: "Mars and Jupiter", time: "", direction: "SE", separation: "1.19", region: "IN-RJ-UDAIPUR")`
- `Astronomy.MediaFactory.Tests.NarrationInputNormalizerTests.SameNormalizerBuildsSafeContextForAstronomyFamilies(name: "raw UTC only", language: "en", objects: "Mars and Jupiter", time: "2026-11-16T00:00:00+00:00", direction: "SE", separation: "1.19", region: "IN-RJ-UDAIPUR")`
- `Astronomy.MediaFactory.Tests.NarrationInputNormalizerTests.SameNormalizerBuildsSafeContextForAstronomyFamilies(name: "deep-sky no observing window", language: "hi", objects: "Andromeda Galaxy", time: "", direction: "E", separation: "", region: "")`
- `Astronomy.MediaFactory.Tests.NarrationInputNormalizerTests.SameNormalizerBuildsSafeContextForAstronomyFamilies(name: "missing timezone", language: "en", objects: "Mars and Jupiter", time: "2026-11-16", direction: "SE", separation: "1.19", region: "IN-RJ-UDAIPUR")`
- `Astronomy.MediaFactory.Tests.NarrationInputNormalizerTests.SameNormalizerBuildsSafeContextForAstronomyFamilies(name: "Mars–Jupiter Hindi", language: "hi", objects: "Mars and Jupiter", time: "2026-11-16 05:30 +0530", direction: "SE", separation: "1.19", region: "IN-RJ-UDAIPUR")`
- `Astronomy.MediaFactory.Tests.CurrentAstronomyFamilyProfileCharacterizationTests.Characterizes_CurrentFamilyProfileMappings(eventType: "PlanetaryConjunction", expectedProfile: "PlanetaryConjunction")`
- `Astronomy.MediaFactory.Tests.CurrentAstronomyFamilyProfileCharacterizationTests.Characterizes_CurrentFamilyProfileMappings(eventType: "LunarEclipse", expectedProfile: "Eclipse")`
- `Astronomy.MediaFactory.Tests.CurrentAstronomyFamilyProfileCharacterizationTests.Characterizes_CurrentFamilyProfileMappings(eventType: "SolarEclipse", expectedProfile: "Eclipse")`
- `Astronomy.MediaFactory.Tests.CurrentAstronomyFamilyProfileCharacterizationTests.CurrentBehavior_UnsupportedOrAbsentFamilyProfilesThrow(eventType: "BlackHoleOrScientificExplainer", message: "Future astronomy family is not active in current r"···)`
- `Astronomy.MediaFactory.Tests.CurrentAstronomyFamilyProfileCharacterizationTests.CurrentBehavior_UnsupportedOrAbsentFamilyProfilesThrow(eventType: "PlanetGrouping", message: "Unsupported astronomy event type: PlanetGrouping")`
- `Astronomy.MediaFactory.Tests.CurrentSemanticFallbackCharacterizationTests.Characterizes_RawJsonGenericAdapterZhrAndDomainFallbacks`
- `Astronomy.MediaFactory.Tests.ThumbnailGeneratorServiceTests.Generates_Three_Thumbnails_And_Diagnostics`
- `Astronomy.MediaFactory.Tests.KnowledgeFoundation.TypedKnowledgeIntegrationTests.AddAstronomyTypedKnowledge_ResolvedOptionsRoundTripTypedPayloads`
- `Astronomy.MediaFactory.Tests.AnalyticsIntelligenceServiceTests.DurationBucketAnalysis_GroupsShortsAndReels`
- `Astronomy.MediaFactory.Tests.AnalyticsIntelligenceServiceTests.AstronomyObjectExtraction_UsesTitlesSeoHashtagsAndNarrationContext`
- `NarrationPreviewRequestTests.NarrationGenerationPhase14FamilyLevelNarrationValidatesForEnglishAndHindi(eventType: "NamedFullMoon", eventName: "Wolf Moon", shortTitle: "Wolf Moon", language: "en", metadata: { bestViewingWindowLocal = "2026-01-03 18:00–23:00 IST", eventDate = "2026-01-03", skyDirectionHint = "eastern sky near moonrise" })`
- `NarrationPreviewRequestTests.NarrationGenerationPhase14FamilyLevelNarrationValidatesForEnglishAndHindi(eventType: "NamedFullMoon", eventName: "Strawberry Moon", shortTitle: "Strawberry Moon", language: "en", metadata: { bestViewingWindowLocal = "2026-06-29 19:00–23:30 IST", eventDate = "2026-06-29", skyDirectionHint = "eastern sky near moonrise" })`
- `NarrationPreviewRequestTests.DeserializesReturnScenesBoolean`
- `NarrationPreviewRequestTests.DeserializesEmptyReturnScenesArrayAsDisabled`
- `NarrationPreviewRequestTests.NarrationGenerationHindiNamedFullMoonAcceptsLocalPeakTimeDevanagariDate`
- `NarrationPreviewRequestTests.NarrationGenerationFallsBackWhenEventNameIsNull`
- `NarrationPreviewRequestTests.NarrationGenerationLocalizesHindiMeteorDirectionAndRejectsEnglishDirectionLeakage`
- `NarrationPreviewRequestTests.NarrationGenerationHindiBestTimeDoesNotPrependDateWhenViewingWindowAlreadyIncludesDate`
- `NarrationPreviewRequestTests.NarrationGenerationFallsBackWhenEventTypeAndNameAreNull`
- `Astronomy.MediaFactory.Tests.Phase7ProductionApiPathSemanticContextTests.RealPhase7EntryPoint_PreservesTypedSemanticSources`
- `Astronomy.MediaFactory.Tests.AlertingTests.MonitorService_TriggersQueueBacklogAlert`
- `Astronomy.MediaFactory.Tests.SscIntelligenceEngineTests.DynamicFovCalculator_ComputesSpreadBasedFov_ForMultipleObjects`
- `Astronomy.MediaFactory.Tests.SscIntelligenceEngineTests.DynamicFovCalculator_DiffersBySceneIntent`
- `Astronomy.MediaFactory.Tests.SscIntelligenceEngineTests.SpatialCompositionAnalyzer_Classifies_Impossible_AndSuggestsSplitGroups`
- `Astronomy.MediaFactory.Tests.SscIntelligenceEngineTests.DynamicFovCalculator_MarksImpossibleGrouping_ForVeryDistantObjects`
- `Astronomy.MediaFactory.Tests.SscIntelligenceEngineTests.CameraCenterCalculator_UsesCircularMean_ForAzimuthWraparound`
- `Astronomy.MediaFactory.Tests.SscIntelligenceEngineTests.SscIntelligenceService_UsesFinalFramedAltitude_ForRender`
- `Astronomy.MediaFactory.Tests.SscIntelligenceEngineTests.DynamicBiasLimiter_ReducesBias_WhenPrimaryNearTopEdge`
- `Astronomy.MediaFactory.Tests.ContentMasterDataTests.SeedData_Contains_Content_Category_And_Utc_Timestamp`
- `Astronomy.MediaFactory.Tests.VisualQualityFrameworkTests.HeroPromptBuilder_ConsumesVisualQualityFramework`
- `Astronomy.MediaFactory.Tests.VisualQualityFrameworkTests.StoryFramePromptBuilders_ConsumeVisualQualityFrameworkWithoutRenderingChanges`
- `Astronomy.MediaFactory.Tests.AiAnalyticsUtcPersistenceTests.AnalyticsInitialization_Creates_Zero_Metric_Baseline_Rows`
- `Astronomy.MediaFactory.Tests.AiAnalyticsUtcPersistenceTests.AnalyticsInitialization_Converts_PublishedAtUtc_To_Utc`
- `Astronomy.MediaFactory.Tests.AstronomyAssetPlanningServiceTests.GenerateAssetPlansAsync_SkipsValidExistingAssetPlanWhenOverwriteFalse`
- `Astronomy.MediaFactory.Tests.AstronomyAssetPlanningServiceTests.GenerateAssetPlansAsync_SelectedPlanIds_LoadsExactImportedDraftPlanAndSavesDefaultRequirements`
- `Astronomy.MediaFactory.Tests.QuestionDrivenVisualComposerTests.GenerateQuestionDrivenVisualsAsync_DryRunReturnsCompletePreviewPlanWithoutWritingFiles`
- `Astronomy.MediaFactory.Tests.AstronomyAssetPlanningServiceTests.GenerateAssetPlansAsync_DryRunFalse_SavesWhenAssetPlanColumnsExist`
- `Astronomy.MediaFactory.Tests.CurrentSemanticPolicyCharacterizationTests.CurrentBehavior_OptionalRegisteredNoValueIsCoverageValidButRuntimeOmitted`
- `Astronomy.MediaFactory.Tests.CurrentSemanticPolicyCharacterizationTests.CurrentBehavior_EclipseRequiredCapabilitiesAreMarkedValidUsingUnrelatedPrimaryObjectAdapters`
- `Astronomy.MediaFactory.Tests.CurrentSemanticPolicyCharacterizationTests.CurrentBehavior_MissingRequiredSemanticFactMakesPhase7ValidatorBlockingIssue`
- `Astronomy.MediaFactory.Tests.CurrentSemanticPolicyCharacterizationTests.CurrentBehavior_CompleteEclipseResolutionStopsAtMissingOptionalMagnitudeCatalogRegistration`
- `Astronomy.MediaFactory.Tests.CurrentSemanticSourceRegistryCharacterizationTests.CurrentBehavior_AdapterSupportedCapabilityMismatchesArePreserved`
- `Astronomy.MediaFactory.Tests.QuestionDrivenVisualComposerTests.GenerateQuestionDrivenVisualsAsync_PlanetGroupingPropagatesInfographicMetadata`
- `Astronomy.MediaFactory.Tests.CurrentSemanticLanguageParityCharacterizationTests.CurrentBehavior_NamedFullMoonEnglishHindiFailForSameCatalogReasons`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastFoundationTests.ContextBuilder_Uses_Skyfield_BestMoonNight_Source_Of_Truth`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastFoundationTests.ScenePlanner_Uses_Object_Specific_And_Recommended_Night_Times`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastFoundationTests.ContextBuilder_Unknown_Region_Returns_Clear_Validation_Error`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastFoundationTests.ContextBuilder_Resolves_RegionId_Case_Insensitively(inputRegionId: "In-Rj-Udaipur")`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastFoundationTests.ContextBuilder_Resolves_RegionId_Case_Insensitively(inputRegionId: "in-rj-udaipur")`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastFoundationTests.ContextBuilder_Resolves_RegionId_Case_Insensitively(inputRegionId: "IN-RJ-UDAIPUR")`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastFoundationTests.ContextBuilder_Uses_Configured_Region_Only_Without_Custom_Dictionary`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastFoundationTests.Segment_Metadata_Path_Disables_Publishing_And_Analytics`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastFoundationTests.ContextBuilder_Maps_BestPlanet_And_BestNights_From_Sidecar_Objects`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.ThumbnailPromptBuilder_IncludesVisualDirectingProfileAndAntiDistortionRules(profile: "landscape", aspectRatio: "16:9", expectedDirector: "LandscapeDirector")`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.ThumbnailPromptBuilder_IncludesVisualDirectingProfileAndAntiDistortionRules(profile: "portrait", aspectRatio: "9:16", expectedDirector: "PortraitDirector")`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.ThumbnailPromptBuilder_IncludesVisualDirectingProfileAndAntiDistortionRules(profile: "square", aspectRatio: "1:1", expectedDirector: "SquareDirector")`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.LocalAssetCollage_VenusJupiterPoster_UsesPhotoCinematicValidationAndCleanText`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.LocalAssetCollage_RemovesCardStyleAndReportsCinematicObjectAnalysis`
- `Astronomy.MediaFactory.Tests.QuestionDrivenVisualComposerTests.GenerateEditorialAstronomyInfographicsAsync_DryRunPlansLongAndShortSceneApprovalVariants`
- `Astronomy.MediaFactory.Tests.Phase7ProductionDiSemanticBindingTests.ProductionResolver_Resolves_Identity_And_Science_Without_CountingEngine`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.ThumbnailPromptBuilder_UsesFamilyDirectorVocabularyWithoutRenderingLogic(family: "Meteor", expectedDirector: "MeteorDirector")`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.ThumbnailPromptBuilder_UsesFamilyDirectorVocabularyWithoutRenderingLogic(family: "Moon", expectedDirector: "MoonDirector")`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.ThumbnailPromptBuilder_UsesFamilyDirectorVocabularyWithoutRenderingLogic(family: "Eclipse", expectedDirector: "EclipseDirector")`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.ThumbnailPromptBuilder_UsesFamilyDirectorVocabularyWithoutRenderingLogic(family: "Planetary", expectedDirector: "PlanetaryDirector")`
- `Astronomy.MediaFactory.Tests.SemanticCapabilityArchitectureTests.BinocularGuidanceIsClassifiedAsObservationModeAlias`
- `Astronomy.MediaFactory.Tests.SemanticCapabilityArchitectureTests.RequiredZhrWithNoResolutionPathBlocksCoverage`
- `Astronomy.MediaFactory.Tests.SemanticCapabilityArchitectureTests.PlanetPairingCoverageEnumeratesFormatsRolesAndHasNoZeroPathCapabilities`
- `Astronomy.MediaFactory.Tests.SemanticCapabilityArchitectureTests.ZhrExistsInCatalogAndAliasesResolveCanonicalCapability`
- `Astronomy.MediaFactory.Tests.SemanticCapabilityArchitectureTests.SemanticSourceContainsNoMarsJupiterOrTitleSpecificResolverConditions`
- `Astronomy.MediaFactory.Tests.SemanticCapabilityArchitectureTests.NarrationGeneratorDoesNotContainPrivateNestedSemanticCapabilityResolver`
- `Astronomy.MediaFactory.Tests.SemanticCapabilityArchitectureTests.MeteorShowerCoverageTreatsOptionalZhrWithoutCurrentCandidateAsValid`
- `Astronomy.MediaFactory.Tests.WeeklyVisualIntentEngineTests.BuildAsync_NormalizesCollapsedShotDurationsAfterRenderSafePass`
- `Astronomy.MediaFactory.Tests.WeeklyVisualIntentEngineTests.BuildAsync_NormalizesEpisodeContainerWhenTimelineSegmentEpisodeTypeIsStale`
- `Astronomy.MediaFactory.Tests.WeeklyVisualIntentEngineTests.BuildAsync_CreatesProfessionalOverlayBasedVisualIntentPlan`
- `Astronomy.MediaFactory.Tests.CurrentKnownSemanticFailureCharacterizationTests.CurrentBehavior_SolarEclipseMapsToGenericEclipseAndRequiredCapabilitiesReceiveUnrelatedObjectAdapters`
- `Astronomy.MediaFactory.Tests.CurrentKnownSemanticFailureCharacterizationTests.CurrentBehavior_PlanetGroupingAliasMapsButProfileIsAbsent`
- `Astronomy.MediaFactory.Tests.HeroPromptMigrationServiceTests.V4_prompt_is_natural_language_and_preserves_constraints_without_replacing_production_prompt`
- `Astronomy.MediaFactory.Tests.SkyAlertSubscriptionTests.Generation_CreatesPending_And_PreventsDuplicates`
- `Astronomy.MediaFactory.Tests.SkyAlertSubscriptionTests.EmailDisabled_KeepsPending`
- `Astronomy.MediaFactory.Tests.EventFamilyResolverTests.Resolve_MapsKnownEventTypesToExpectedFamily(eventType: "Constellation", expected: SpecialEvent)`
- `Astronomy.MediaFactory.Tests.EventFamilyResolverTests.Resolve_SpecialEventProfileUsesSubtypeGuidanceWithoutValidatedFamilyLeakage(eventType: "Constellation", selectedProfile: "SpecialEvent:Constellation", requiredVisualElement: "star pattern lines", requiredOverlayElement: "direction guide")`
- `Astronomy.MediaFactory.Tests.PromptComposerV2Tests.AzureProviderAdapter_preserves_sections_and_inlines_negative_constraints`
- `Astronomy.MediaFactory.Tests.PromptComposerV2Tests.PromptComposer_can_produce_AzureImage_prompt_package_without_provider_call`
- `Astronomy.MediaFactory.Tests.ManualAnalyticsIngestionServiceTests.InitializeForPipelineRunAsync_does_not_dispose_db_connection_before_follow_up_queries`
- `Astronomy.MediaFactory.Tests.ManualAnalyticsIngestionServiceTests.InitializeForPipelineRunAsync_creates_thumbnail_rows_for_long_and_short`
- `Astronomy.MediaFactory.Tests.CurrentSemanticCapabilityCatalogCharacterizationTests.CurrentBehavior_AliasesResolveToCurrentCanonicalCapability(alias: "EventDateOrWindow", expected: "EventDate")`
- `Astronomy.MediaFactory.Tests.CurrentSemanticCapabilityCatalogCharacterizationTests.CurrentBehavior_AliasesResolveToCurrentCanonicalCapability(alias: "ObservationMode", expected: "ObservationMode")`
- `Astronomy.MediaFactory.Tests.CurrentSemanticCapabilityCatalogCharacterizationTests.CurrentBehavior_AliasesResolveToCurrentCanonicalCapability(alias: "ZHR", expected: "Zhr")`
- `Astronomy.MediaFactory.Tests.CurrentSemanticCapabilityCatalogCharacterizationTests.CurrentBehavior_AliasesResolveToCurrentCanonicalCapability(alias: "ZenithalHourlyRate", expected: "Zhr")`
- `Astronomy.MediaFactory.Tests.CurrentSemanticCapabilityCatalogCharacterizationTests.Characterizes_DirectCanonicalRegistrations(id: "ObservationMode")`
- `Astronomy.MediaFactory.Tests.CurrentSemanticCapabilityCatalogCharacterizationTests.Characterizes_DirectCanonicalRegistrations(id: "Zhr")`
- `Astronomy.MediaFactory.Tests.CurrentSemanticCapabilityCatalogCharacterizationTests.Characterizes_DirectCanonicalRegistrations(id: "PrimaryObjects")`
- `Astronomy.MediaFactory.Tests.CurrentSemanticCapabilityCatalogCharacterizationTests.CurrentBehavior_ProfileReferencedCapabilitiesAbsentFromCatalog(id: "CulturalNameContext")`
- `Astronomy.MediaFactory.Tests.CurrentSemanticCapabilityCatalogCharacterizationTests.CurrentBehavior_ProfileReferencedCapabilitiesAbsentFromCatalog(id: "Magnitude")`
- `Astronomy.MediaFactory.Tests.VisualCreativeDirectorTests.PlanetPairing_refinement_recommends_balanced_relationship_prominence`
- `Astronomy.MediaFactory.Tests.VisualCreativeDirectorTests.PlanetPairing_treats_conjunction_relationship_as_hero`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PublishingMode_DryRun_WritesPayloadWithoutUpload`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PipelineOrchestrator_UploadsThumbnailToYouTube_WhenVideoUploadSucceeds`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PublishingMode_Disabled_SkipsUpload`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PipelineOrchestrator_Continues_WhenThumbnailGenerationFails`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PipelineOrchestrator_SkipsYouTubeShortPublish_WhenPublishToYouTubeFalse`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PipelineOrchestrator_Continues_WhenThumbnailGenerationReturnsNull`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PipelineOrchestrator_UsesMonetizedDescription_WhenMonetizationSucceeds`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PublishingMode_Private_UsesPrivatePrivacyStatus`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PublishingMode_Public_RequiresExplicitPublicMode`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PipelineOrchestrator_DoesNotFail_WhenInstagramVerificationTimesOutAfterUpload`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PipelineOrchestrator_FallsBack_WhenMonetizationFails`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PipelineOrchestrator_Continues_WhenBlobOrYouTubeUploadFails`
- `Astronomy.MediaFactory.Tests.PublishingFlowTests.PipelineOrchestrator_SkipsPublishStages_WhenPublishArtifactsAreMissing`
- `Astronomy.MediaFactory.Tests.ContentPlanBatchGenerationServiceTests.ExecuteContentPlanWithProductionPipelineAsync_RebuildOutputs_PartialRangeSuccessIgnoresDownstreamCompletion`
- `Astronomy.MediaFactory.Tests.ContentPlanBatchGenerationServiceTests.ExecuteContentPlanWithProductionPipelineAsync_RebuildOutputs_ExpandsRequestedRangeForPrerequisites`
- `Astronomy.MediaFactory.Tests.ContentPlanBatchGenerationServiceTests.GenerateFromPlansAsync_ProductionPipeline_InvokesVisualIntelligenceAndWritesDiagnosticsUnderPlanOutput`
- `Astronomy.MediaFactory.Tests.CelestialAssetIngestionServiceTests.EmptyFolderGetsPopulatedAndMetadataWritten`
- `Astronomy.MediaFactory.Tests.CategoryRequirementAndVisualStrategyTests.Pipeline_Run_Endpoint_Remains_Unchanged`
- `Astronomy.MediaFactory.Tests.DocumentaryPerformerDeterministicCorrectnessTests.LanguageValidator_NormalizesFamily_AndScriptRatioStaysBounded(family: "hi", requested: "hi-IN", narration: "सूर्यास्त के बाद बृहस्पति और शुक्र पश्चिमी आकाश मे"···)`
- `Astronomy.MediaFactory.Tests.QuestionDrivenNarrationGeneratorTests.GenerateQuestionDrivenNarrationAsync_AllowsDbApprovedProductionPlanWhenCategoryIsNotRareEventAlert`
- `Astronomy.MediaFactory.Tests.QuestionDrivenNarrationGeneratorTests.GenerateQuestionDrivenNarrationAsync_AllowsDbApprovedProductionPlanRequest`
- `Astronomy.MediaFactory.Tests.QuestionDrivenNarrationGeneratorTests.GenerateQuestionDrivenNarrationAsync_WritesNarrationAndReviewWhenDryRunIsFalse`
- `Astronomy.MediaFactory.Tests.QuestionDrivenNarrationGeneratorTests.GenerateQuestionDrivenNarrationAsync_ProductionMeteorShowerUsesStrategyIntelligenceInsteadOfStalePilotPlan`
- `Astronomy.MediaFactory.Tests.QuestionDrivenNarrationGeneratorTests.GenerateQuestionDrivenNarrationAsync_ReplacesCopiedSourceAnswersBeforeReviewAndSave`
- `Astronomy.MediaFactory.Tests.QuestionDrivenNarrationGeneratorTests.GenerateQuestionDrivenNarrationAsync_DryRunReturnsValidNarrationWithoutWritingFiles`
- `Astronomy.MediaFactory.Tests.QuestionDrivenNarrationGeneratorTests.GenerateQuestionDrivenNarrationAsync_AllowsNamedFullMoonProductionPlanAndUsesMoonIntelligence`
- `Astronomy.MediaFactory.Tests.QuestionDrivenNarrationGeneratorTests.GenerateQuestionDrivenNarrationAsync_AllowsDbApprovedRareEventVideoPlanWithProductionStatusAndNoExternalEventId`
- `Astronomy.MediaFactory.Tests.ContentExperimentServiceTests.EvaluateRecentExperiments_RotatesToUntestedVariantAfterInterval`
- `Astronomy.MediaFactory.Tests.PlatformMetadataFormatterTests.FormatTarget_ForYouTubeShorts_UsesHookAsTitleLikeCaptionAndMinimalCta`
- `Astronomy.MediaFactory.Tests.YouTubeShortsValidationTests.ValidateBeforeUploadAsync_WritesValidationJsonWithEligibilityFalseForLandscapeVideo`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.ObservationTimingResolvesFromSemanticAlternatives(eventIntelJson: "{\"peakUtc\":\"2026-11-16T00:00:00Z\"}", expectedSource: "Production Event Intelligence", expectedField: "peakUtc")`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.ObservationTimingResolvesFromSemanticAlternatives(eventIntelJson: "{\"localPeakTime\":\"before dawn on November 16, a"···, expectedSource: "Production Event Intelligence", expectedField: "localPeakTime")`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.ObservationTimingResolvesFromSemanticAlternatives(eventIntelJson: "{\"bestViewingWindowLocal\":\"2026-11-16 04:30–06:"···, expectedSource: "Production Event Intelligence", expectedField: "bestViewingWindowLocal")`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.PlanetPairingDoesNotFabricateAngularSeparation`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.MissingOptionalBinocularGuidanceWarnsOnly`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.OptionalZhrWithNoSourceValueDoesNotBlockMeteorShower`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.ConflictingAngularSeparationSelectsAuthorityAndWarns`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.RequiredFactAvailableInDocumentaryContractWins`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.ApparentPairingScienceUsesCanonicalDomainKnowledge`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.PlanetPairingApparentPairingScienceResolvesFromDomainKnowledge`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.DuplicateMissingFactReportsCollapsePerBeat`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.VerifiedUpstreamZhrResolvesWhenPresent`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.MissingRequiredTimingFactBlocksTimingBeat`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.ZhrIsNotFabricatedFromGeminidsTitle`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverTests.ProviderFailureLeavesDescriptiveApparentPairingScienceBlockingErrorWithoutFiller`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.LocalAssetCollage_WritesTransparentAssetSelectionDiagnostics`
- `Astronomy.MediaFactory.Tests.FamilyProfileValidationV1Tests.RuntimeUsesV1FamilyCatalogThroughApprovedResolverBoundary`
- `Astronomy.MediaFactory.Tests.KnowledgeFoundation.TypedObservationalKnowledgeTests.ObservationalQuantity_EnforcesLocalInvariants`
- `Astronomy.MediaFactory.Tests.MeteorShowerProductionParityTests.GeminidsProductionRequest_CharacterizesMeteorActivityLifecycleThroughSemanticResolution`
- `Astronomy.MediaFactory.Tests.Rc2CreativeStoryboardTests.Phase7_BuildsCreativeStoryboardAndDiagnosticsFromEditorialArtifacts`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_KnownCinematicMotionHintsUseDedicatedHandlersWithoutWarnings(motionHint: "guided_pan_across_group_with_object_sequence_empha"···, expectedHandler: "GroupedObjectPanRenderer")`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_KnownCinematicMotionHintsUseDedicatedHandlersWithoutWarnings(motionHint: "montage_crossfade", expectedHandler: "WeeklyMontageRenderer")`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_KnownCinematicMotionHintsUseDedicatedHandlersWithoutWarnings(motionHint: "episode_montage", expectedHandler: "WeeklyMontageRenderer")`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_KnownCinematicMotionHintsUseDedicatedHandlersWithoutWarnings(motionHint: "guided_pan_across_group", expectedHandler: "GroupedObjectPanRenderer")`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_KnownCinematicMotionHintsUseDedicatedHandlersWithoutWarnings(motionHint: "weekly_montage", expectedHandler: "WeeklyMontageRenderer")`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_KnownCinematicMotionHintsUseDedicatedHandlersWithoutWarnings(motionHint: "pan_sequence", expectedHandler: "GroupedObjectPanRenderer")`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_KnownCinematicMotionHintsUseDedicatedHandlersWithoutWarnings(motionHint: "episode_montage_crossfade_with_night-by-night_prog"···, expectedHandler: "WeeklyMontageRenderer")`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_DryRunReturnsPreviewsWithoutWritingFiles`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_ReadsAllRecipesAndWritesCapabilityPerRecipeWithoutRendering`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_UnknownMotionUsesDefaultHandlerAndWarning`
- `Astronomy.MediaFactory.Tests.RenderCapabilityMatrixServiceTests.GenerateRenderCapabilities_AllKnownRenderModesMapToHandlersWithoutBlocking`
- `Astronomy.MediaFactory.Tests.PipelineStageInstrumentationTests.RunAsync_RecordsStages_AndFallbackFailure`
- `Astronomy.MediaFactory.Tests.PipelineStageInstrumentationTests.RunAsync_ContinuesFallback_WhenFailureRecordingIsCanceled`
- `Astronomy.MediaFactory.Tests.PipelineStageInstrumentationTests.RunAsync_PublishesStageAlerts_ForSlowAndFailedStages`
- `Astronomy.MediaFactory.Tests.RuleBasedOptimizationServiceTests.Apply_Safe_Rules_Mutates_Only_Allowed_Fields`
- `Astronomy.MediaFactory.Tests.CinematicThumbnailServiceTests.CinematicThumbnail_SetsFallbackFalse_WhenComposedFromFallbackCandidate`
- `Astronomy.MediaFactory.Tests.CinematicThumbnailServiceTests.CinematicThumbnail_FallsBackToExtractedFrame_WhenCompositionFails`
- `Astronomy.MediaFactory.Tests.CinematicThumbnailServiceTests.CinematicThumbnail_GeneratesLongAndShortOutputsInThumbnailsDirectory`
- `Astronomy.MediaFactory.Tests.CinematicThumbnailServiceTests.HindiHook_ComposesThumbnailWithoutTofuFailure`
- `Astronomy.MediaFactory.Tests.MetaPublishingTests.InstagramMediaCall_ReceivesPublicCoverUrl`
- `Astronomy.MediaFactory.Tests.MetaPublishingTests.FacebookLong_UsesFinalVideoAndLongThumbnail_WithoutReelEndpoint`
- `Astronomy.MediaFactory.Tests.MetaPublishingTests.PlatformPublishingAssetsReport_ContainsFinalProductionMatrix`
- `Astronomy.MediaFactory.Tests.MetaPublishingTests.FacebookPublishing_DoesNotUsePublicMediaStorage`
- `Astronomy.MediaFactory.Tests.MetaPublishingTests.UnsupportedAsset_DoesNotInvokeFacebookOrInstagram`
- `Astronomy.MediaFactory.Tests.MetaPublishingTests.InstagramPublicMediaUpload_WritesConfiguredBlobPath`
- `Astronomy.MediaFactory.Tests.MetaPublishingTests.AssetAll_PublishesFacebookAndInstagramReelsOnly`
- `Astronomy.MediaFactory.Tests.ObjectKnowledgeAggregateProjectionTests.ObjectKnowledge_Field_Projections_Remain_Available(field: "IdentificationPattern", expected: "Belt")`
- `Astronomy.MediaFactory.Tests.ObjectKnowledgeAggregateProjectionTests.ObjectKnowledge_Field_Projections_Remain_Available(field: "ScientificIdentity", expected: "IAU-recognized")`
- `Astronomy.MediaFactory.Tests.ObjectKnowledgeAggregateProjectionTests.ObjectKnowledge_Field_Projections_Remain_Available(field: "ScientificImportance", expected: "sky navigation")`
- `Astronomy.MediaFactory.Tests.ObjectKnowledgeAggregateProjectionTests.ObjectKnowledge_Field_Projections_Remain_Available(field: "Name", expected: "Orion")`
- `Astronomy.MediaFactory.Tests.ObjectKnowledgeAggregateProjectionTests.ObjectKnowledge_Field_Projections_Remain_Available(field: "MajorStars", expected: "Betelgeuse")`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverV1MigrationTests.Legacy_Aliases_With_Different_Policies_Use_One_Engine_Call_Per_Policy_Scope`
- `Astronomy.MediaFactory.Tests.RequiredSemanticFactResolverV1MigrationTests.Legacy_Aliases_With_Identical_Policy_Share_One_Engine_Call_And_Project_Both_Fact_Types`
- `Astronomy.MediaFactory.Tests.Rc2NarrationV5OrchestrationTests.Phase8_RangeRequest_RunsNarrationV5AndAddsOutputsToResponseAndManifest`
- `NarrationPreviewPlanHydrationTests.NarrationGenerationHydratesPlanAndEventIntelligenceWhenPlanIdIsProvided`
- `Astronomy.MediaFactory.Tests.SemanticSourceAdaptersV1Tests.Meteor_FullMoon_Eclipse_Occultation_And_Safety_Rules`
- `Astronomy.MediaFactory.Tests.ObservationTimeServiceSelectionTests.SelectSceneTimes_UsesFillerScenes_WhenOnlyOneVisibleObjectExists`
- `Astronomy.MediaFactory.Tests.ThumbnailCinematicAiPhase3Tests.ExistingPublishingResolver_IsUnaffectedByCinematicAiThumbnailFiles`
- `Astronomy.MediaFactory.Tests.ThumbnailCinematicAiPhase3Tests.CinematicThumbnail_FallsBackToPhaseOneFrame_WhenIntegrityValidationFails`
- `Astronomy.MediaFactory.Tests.KnowledgeFoundation.KnowledgeFoundationSerializationAndDiTests.Json_configuration_is_idempotent_and_preserves_task1_serialization`
- `Astronomy.MediaFactory.Tests.KnowledgeFoundation.KnowledgeFoundationSerializationAndDiTests.Architecture_boundary_has_no_later_task_or_infrastructure_leakage`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.ThumbnailStrategy_UsesFeedbackSignalsToPromoteLayouts`
- `Astronomy.MediaFactory.Tests.ThumbnailGenerationTests.ThumbnailStrategy_UsesExperimentFeedbackHintsForVariants`
- `Astronomy.MediaFactory.Tests.WeeklySkyForecastV2FinalMediaTests.FinalMediaOrchestrator_Renders_FinalAssets_WithoutPublishing`
- `Astronomy.MediaFactory.Tests.CurrentRequiredSemanticFactCrossFamilyCharacterizationTests.Characterizes_StructurallySuccessfulFamilies(eventType: "PlanetPairing", intel: "{\"eventType\":\"PlanetPairing\",\"objectPair\":[\"···, observation: "{\"direction\":\"east\",\"localPeakTime\":\"dawn\""···)`
- `Astronomy.MediaFactory.Tests.CurrentRequiredSemanticFactCrossFamilyCharacterizationTests.Characterizes_StructurallySuccessfulFamilies(eventType: "MeteorShower", intel: "{\"eventType\":\"MeteorShower\",\"eventTitle\":\"G"···, observation: "{\"bestViewingWindowLocal\":\"2026-12-14 00:00-05:"···)`
- `Astronomy.MediaFactory.Tests.PromptBuilderTests.PromptAssembler_ProducesLandscapeRichness_AndSquareInformationBudget`
- `Astronomy.MediaFactory.Tests.PromptBuilderTests.PromptAssembler_FiltersSections_ByPlatformStorytellingStrategy`
- `Astronomy.MediaFactory.Tests.PromptBuilderTests.Build_ShouldContainEventAndLocation`
- `Astronomy.MediaFactory.Tests.PromptBuilderTests.ThumbnailPromptBuilder_InjectsDistinctCompositionProfiles_PerAspectRatio`
- `Astronomy.MediaFactory.Tests.PromptBuilderTests.ThumbnailPromptBuilder_InjectsDistinctPlatformStorytellingStrategies_PerAspectRatio`
## Warnings and remaining work

- Restore reported `NU1510` for `System.Net.Http.Json` and `NU1903` for vulnerable `SQLitePCLRaw.lib.e_sqlite3` 2.1.11.
- The evaluator and routing suites remain skeletal (3 and 2 tests), rather than the required comprehensive production-logic suites.
- No committed-input integration class exists, so its filter matched zero tests.
- Story Frame, Phase 6, Phase 4, Phase 5, and full-project matrices are not green.
- The complete-project failures include substantial environment/media fixture failures as well as assertion regressions; none are represented as passed.

P6.2 remains incomplete until the exhaustive evaluator suite, real-route routing suite, focused integration suite, reason-code artifact tests, cancellation/immutability matrix, and all regression fixes are implemented and rerun green.

**Remaining P6.3 work (not started):** certified variant-specific frame mapping/bijection and one-frame-per-committed-scene alignment. P6.3 must not begin until P6.2 passes.

## Final verdict

PHASE6_COMMITTED_INPUT_BOUNDARY_STILL_FAILING
