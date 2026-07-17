# A. Executive Summary

Evidence classification key: **Confirmed from production code**, **Confirmed from test code**, **Confirmed from configuration/DI**, **Strongly supported by multiple code paths**, **Inferred**, **Not yet found**.

- **Branch precheck:** requested branch was `RecoveryBrach-18-05-26`, but this checkout is on `work` at `c15e6d4 fixed test case issue`. Discovery was performed without switching to `main`. **Confirmed from command output.**
- **Current architecture summary:** Phase 7 is orchestrated by `ProductionPipelineExecutionService.PhaseGenerateNarrationPlanAsync`, which validates prerequisite Phase 4-6 files, writes runtime composition diagnostics, delegates artifact generation to `NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync`, then validates required Phase 7 outputs. **Confirmed from production code.**
- **Confirmed semantic source of truth:** V1 executable family profiles live in `AstronomyFamilyProfileCatalogV1.CreateProfiles`; these are converted through `AstronomyFamilyProfileV1CompatibilityAdapter` into the legacy `AstronomyFamilyProfile` consumed by `RequiredSemanticFactResolver`. **Confirmed from production code/configuration.**
- **Current Phase 7 execution model:** narration artifacts are written before semantic resolution; semantic resolution happens once for a combined long/short `RequiredSemanticFactResolutionInput`; then realization, prompt composition, transcript generation, narration writes, and required-output validation follow if no pre-prompt semantic exception occurs. **Confirmed from production code.**
- **First likely production divergence:** realistic API-shaped Meteor Shower requests use `ContentStrategy=LocalViewingGuide`, while `BuildMeteorActivityFromRequest` returns `null` unless `request.ContentStrategy == "MeteorShower"`. With current `ReadMeteorActivity` only extracting root `zhr`, this can produce no `MeteorActivityValue` before adapter invocation. Status: **Strongly supported**. This precedes canonical resolution, projection, and beat retention.
- **Output-path risks:** `MeteorActivityLifecycleDiagnostics.Write*` writes fixed relative paths such as `narration-v5/meteor-activity-context-diagnostics.json`, resolved through `Path.GetFullPath(path)`, so they depend on process current directory rather than the plan output root. **Confirmed from production code.**
- **Stale-artifact risks:** Phase 7 required-output validation checks existence/length/JSON root of files under `<outputRoot>/narration-v5`; it does not compare write time, execution id, or phase start time. **Confirmed from production code.**
- **Realistic-fixture gaps:** controlled tests build `ContentStrategy="MeteorShower"`, `SourceExternalEventId="geminids-2026"`, and timezone values, while the failing request has `ContentStrategy="LocalViewingGuide"`, `SourceExternalEventId="meteor-shower-geminids-2026"`, and `TimeZone=null`. **Confirmed from test code and user-supplied production shape.**
- **Duplicate source-of-truth risks:** family requirements appear in V1 profiles, a legacy `AstronomyFamilyProfileCatalog` dictionary, legacy capability maps, source-policy catalog, adapter registry default constructor, DI explicit adapter list, and Phase 7 output manifest. **Strongly supported by multiple code paths.**
- **Discovery confidence level:** high for Phase 7, semantic registration, MeteorActivity lifecycle, projections, output paths, and tests named below; medium for full phases 1-6 artifact inventory due repository breadth.

# B. Exact File/Class Inventory

| Area | Repository-relative path | Namespace | Type | Method/property | Responsibility | Registration/consumer | Authority level | Evidence classification |
|---|---|---|---|---|---|---|---|---|
| DI semantic runtime | `Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | `Astronomy.MediaFactory.Infrastructure.Extensions` | `ServiceCollectionExtensions` | `AddSemanticRuntime`, `AddProductionSemanticRuntimeV1` | Registers family catalog, compatibility adapter, family resolver, source policy catalog, adapters, registry, engine, resolver, realizer | API `AddMediaFactory` | Runtime-authoritative DI | Confirmed from configuration/DI |
| V1 family profile catalog | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Families/AstronomyFamilyProfileCatalogV1.cs` | `...Semantics.Families` | `AstronomyFamilyProfileCatalogV1` | `CreateProfiles`, `Validate`, `TryGet`, `ResolveEventType` | Defines active V1 families and required/optional semantic capabilities | DI and family resolver | Semantic source of truth for V1 family contracts | Confirmed from production code |
| MeteorShower V1 profile | same | same | same | `Profile("MeteorShower", ...)` | Requires `EventIdentity`, `EventWindow`, `ObservationDirection`, `MeteorActivity`, `DomainScientificKnowledge`; optional observation/equipment/editorial | Converted to legacy runtime | V1 source of truth | Confirmed from production code |
| Planet grouping/conjunction profile | same | same | same | `Profile("PlanetGrouping"...)`, `Profile("PlanetPairing"...)` | Planet conjunction appears represented by active PlanetPairing/PlanetGrouping taxonomy plus aliases | Converted to legacy runtime | V1 source of truth, with naming alias risk | Confirmed from production code |
| V1-to-legacy profile conversion | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Families/Compatibility/AstronomyFamilyProfileV1CompatibilityAdapter.cs` | `...Families.Compatibility` | `AstronomyFamilyProfileV1CompatibilityAdapter` | `Convert`, `Map` | Converts canonical V1 capabilities to legacy required/optional fact terms | `AstronomyFamilyProfileResolver` | Runtime bridge; competing legacy model remains | Confirmed from production code |
| Legacy profiles | `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | `...Orchestration.RC2` | `AstronomyFamilyProfileCatalog` | static `Profiles` dictionary | Legacy family required facts, optional facts, forbidden terminology | Compatibility fallback / tests / characterization | Competing source of truth | Confirmed from production code |
| Source policies | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Catalog/SemanticSourcePolicyCatalogV1.cs` | `...Sources.Catalog` | `SemanticSourcePolicyCatalogV1` | `CreatePolicies`, `TryGet`, `EvaluateSource` | Policy, evidence categories, source priority, missing behavior, derivation rules | DI singleton, engine/evaluator | Runtime source policy truth | Confirmed from production code |
| MeteorActivity policy | same | same | same | `P(SemanticCapabilityVocabularyV1.MeteorActivity, ...)` | Allows ProductionEventIntelligence, ObservationMetadata, DomainKnowledge; `CombineStructuredFields`; `BlockRequired`; `NoZhrFabrication` | Resolver/engine | Runtime-authoritative policy | Confirmed from production code |
| Adapter contracts | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters/Contracts/SemanticSourceAdapterContractsV1.cs` | `...Adapters.Contracts` | records/interfaces | `ISemanticSourceAdapterV1`, `SemanticSourceAdapterContextV1`, `MeteorActivityValue`, `ProductionEventIntelligenceSourceV1` | Typed adapter context/value contract | All adapters/resolver | Runtime contract truth | Confirmed from production code |
| Event adapters | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters/Event/SemanticSourceAdaptersV1.cs` | `...Adapters.Event` | `MeteorActivitySourceAdapterV1` etc. | `TryExtract` | Extracts typed candidates from source context | Registry | Runtime extraction | Confirmed from production code |
| Adapter registry | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters/Registry/SemanticSourceAdapterRegistryV1.cs` | `...Adapters.Registry` | `SemanticSourceAdapterRegistryV1` | ctor, `GetAdapters`, `TryGetByAdapterId`, `CertifyAgainstPolicies` | Validates and exposes adapters by canonical capability | DI and default constructors | Runtime registry; duplicate explicit list exists | Confirmed from production code |
| Compatibility mapper | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/LegacyRequiredSemanticFactCompatibilityMapper.cs` | `...Semantics` | `LegacyRequiredSemanticFactCompatibilityMapper` | `Map`, `ProjectStructuredValue` | Projects canonical V1 facts into legacy `ResolvedSemanticFact` | `RequiredSemanticFactResolver.Project` | Runtime projection truth | Confirmed from production code |
| Required resolver | `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | `...Orchestration.RC2` | `RequiredSemanticFactResolver` | `Resolve`, `EnumerateRequirementOccurrences`, `CreateOccurrence`, `CreateAdapterContext`, `Project` | Builds semantic requests, invokes V1 engine once per scope, projects to beats | DI as `IRequiredSemanticFactResolver` | Runtime semantic resolver | Confirmed from production code |
| Phase 7 orchestrator | `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` | `...Persistence` | `ProductionPipelineExecutionService` | `PhaseGenerateNarrationPlanAsync` | Phase 7 composition, diagnostics, generator call, failure preservation | Phase registry | Runtime phase authority | Confirmed from production code |
| Narration generator | `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | `...Orchestration.RC2` | `NarrationGeneratorV5` | `BuildAndWriteDiagnosticsAsync` | Writes Phase 7 narration artifacts and performs semantic resolution | Phase 7 orchestrator | Runtime artifact authority | Confirmed from production code |
| Meteor lifecycle diagnostics | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Diagnostics/MeteorActivityLifecycleDiagnostics.cs` | `...Production.Narration.Diagnostics` | `MeteorActivityLifecycleDiagnostics` | `WriteContext`, `RecordAdapter`, `RecordResolution`, `RecordProjection`, `RecordBeat`, `Write` | Best-effort MeteorActivity lifecycle JSON diagnostics | Adapter/resolver/generator | Diagnostic-only; path risk | Confirmed from production code |
| Phase 7 required outputs | `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` | `...Persistence` | `ProductionPipelineExecutionService` | `Phase7RequiredOutputManifest`, `EvaluatePhase7RequiredOutput` | Required file existence/length/JSON-root validation | After phase success | Validator source; stale risk | Confirmed from production code |
| Meteor catalog | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Catalog/MeteorShowerKnowledgeCatalogV1.cs` | `...Sources.Catalog` | `MeteorShowerKnowledgeCatalogV1` | `FindByCanonicalShowerIdentity`, `Normalize` | Stable meteor shower radiant/parent/ZHR lookup for Geminids, Perseids, others | `BuildMeteorActivityFromRequest` | Metadata source | Confirmed from production code |
| Production intelligence writer | `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` | `...Persistence` | `ProductionPipelineExecutionService` | `PhaseBuildProductionIntelligenceAsync`, `WriteProductionIntelligenceAsync`, `WritePlanInputAsync` | Writes `plan-input/production-event-intelligence.json` | Phase 2 | Plan-scoped artifact source | Confirmed from production code |
| Production intelligence builder | `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/AstronomyQuestionEngine.cs` | `...Persistence` | `AstronomyQuestionEngine` | `BuildProductionEventIntelligence` | Builds event intelligence from DB event, metadata, region, timezone | Question set build | Upstream data model | Confirmed from production code |
| Runtime composition diagnostics | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Diagnostics/RuntimeCompositionDiagnostics.cs` | `...Production.Narration.Diagnostics` | `RuntimeCompositionDiagnostics` | `ValidateServiceRegistrations`, `Build`, `WriteAsync` | DI/runtime identity diagnostics and guardrails | DI/Phase 7 | Runtime diagnostic authority | Confirmed from production code |
| Meteor tests | `Backend/tests/Astronomy.MediaFactory.Tests/MeteorShowerExecutableFamilyCoverageTests.cs` | tests | `MeteorShowerExecutableFamilyCoverageTests` | multiple tests | Controlled MeteorActivity context/adapter/resolution/projection/retention | xUnit | Test evidence, not production-shaped | Confirmed from test code |
| Composition tests | `Backend/tests/Astronomy.MediaFactory.Tests/ApiHostRuntimeCompositionDiagnosticsTests.cs`, `Phase7ProductionDiSemanticBindingTests.cs` | tests | test classes | multiple tests | Confirm real DI has expected resolver/adapter/engine | xUnit | Test evidence | Confirmed from test code |

# C. Current Planet Conjunction Execution Chain

1. Request enters RC2 execution in `ProductionPipelineExecutionService`; phase registry maps phase 2 to `PhaseBuildProductionIntelligenceAsync`, phase 3 to questions, phase 4/5/6 Chronicle builders, and phase 7 to `PhaseGenerateNarrationPlanAsync`. **Confirmed from production code.**
2. Phase 2 writes `plan-input/content-plan-production-request.json` and `plan-input/production-event-intelligence.json`; `ValidatePlanetConjunctionPhase2` blocks if local peak, direction, best-viewing window, angular separation, or Venus/Jupiter objects are missing for conjunction-like intelligence. **Confirmed from production code.**
3. Phase 3 invokes `AstronomyQuestionEngine.GenerateQuestionAnswersAsync`; `BuildProductionEventIntelligence` chooses primary/secondary objects and fields like local peak, best window, direction, and angular separation from `AstronomyEventIntelligence` metadata. **Confirmed from production code.**
4. Phase 4 creates story graph through `SceneIntentBuilder.BuildAndWriteStoryGraphAsync`. **Confirmed from production code.**
5. Phase 5 creates editorial artifacts through `SceneIntentBuilder.BuildAndWriteDiagnosticsAsync`. **Confirmed from production code.**
6. Phase 6 creates documentary contracts through `CreativeStoryboardBuilder.BuildAndWriteDiagnosticsAsync` and validates/enriches scene plans. **Confirmed from production code.**
7. Phase 7 validates Phase 4-6 prerequisites, verifies semantic runtime composition, writes runtime diagnostics, and calls `NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync`. **Confirmed from production code.**
8. `NarrationGeneratorV5` writes shared narration artifacts, reads documentary contracts, `production-event-intelligence.json`, observation metadata, story graph, resolves canonical identity/family, validates semantic registry coverage, constructs one `RequiredSemanticFactResolutionInput`, calls `IRequiredSemanticFactResolver.Resolve` once, realizes facts, composes prompt, generates long/short scripts/narration, then writes diagnostics. **Confirmed from production code.**
9. Planet conjunction semantic facts are handled through V1 profile requirements for PlanetPairing/PlanetGrouping and legacy mappings (`AstronomicalObjects` → `PrimaryObjects`, `AngularSeparation` → `AngularRelationship`, domain knowledge mappings). **Confirmed from production code.**
10. Best realistic Planet Conjunction fixtures found: narration preview examples for Jupiter/Venus and Mars/Jupiter in `NarrationPreviewRequestTests`, and multiple production/certification-style tests around asset planning, scene assets, final narration, and DI. **Confirmed from test code.**

# D. Current Meteor Shower Execution Chain

Phases 1-6 are shared with the Planet Conjunction flow except family-specific content strategies and scene/visual rules.

Phase 7 MeteorActivity lifecycle:

1. **Source context population:** `NarrationGeneratorV5` reads `plan-input/production-event-intelligence.json`, contracts, observation metadata, story graph, extracts request, and passes all to `RequiredSemanticFactResolutionInput`. The resolver's `CreateAdapterContext` constructs `SemanticSourceAdapterContextV1`; `meteorActivity = ReadMeteorActivity(input.ProductionEventIntelligence) ?? BuildMeteorActivityFromRequest(request, productionEventWindow)`. **Confirmed from production code.**
   - **Earliest realistic divergence:** `BuildMeteorActivityFromRequest` returns null unless `request.ContentStrategy` equals `MeteorShower`; actual request uses `LocalViewingGuide`. Status: **Strongly supported**.
2. **Approved adapter registration:** DI registers `MeteorActivitySourceAdapterV1`; registry default constructor also includes it. Adapter ID is `v1.meteor-activity.production-event-intelligence`. **Confirmed from configuration/DI.**
3. **Approved source-policy availability:** `SemanticSourcePolicyCatalogV1` has `MeteorActivity` policy with `ProductionEventIntelligence`, `ObservationMetadata`, and `AstronomyDomainKnowledgeProvider`, minimum `Strong`, `CombineStructuredFields`, block-required, `NoZhrFabrication`. **Confirmed from production code.**
4. **Adapter invocation:** V1 engine invokes adapters returned by registry for the requested capability. Meteor adapter reads `context.ProductionEventIntelligence?.MeteorActivity`. **Confirmed from production code.**
5. **Candidate emission:** Meteor adapter emits a `MeteorActivityValue` candidate only when typed context has `MeteorActivity`; otherwise `SourceUnavailable`/`ValueMissing`. **Confirmed from production code.**
6. **Canonical capability resolution:** `RequiredSemanticFactResolver.Resolve` groups requirement occurrences by semantic scope and calls `_semanticResolutionEngine.Resolve(g.First().Request!)` once per unique scope. **Confirmed from production code.**
7. **Compatibility projection:** `RequiredSemanticFactResolver.Project` calls `LegacyRequiredSemanticFactCompatibilityMapper.Map`; mapper projects `MeteorActivityValue.Radiant/RadiantConstellation` and `PeakWindow/ActivityWindow` and assigns derivation rule IDs like `V1Projection.MeteorActivity.Radiant` and `V1Projection.MeteorActivity.PeakWindow`. **Confirmed from production code.**
8. **Beat retention:** resolver adds projected required and optional facts to `ResolvedBeatFacts`; missing required facts block the beat. Meteor `RecordBeat` is called for MeteorActivity-derived requirements. **Confirmed from production code.**
9. **Narration artifact generation:** if no Meteor parity exception and no blocking realization, generator writes normalization, prompt, script, long/short narration, and diagnostics. **Confirmed from production code.**
10. **Artifact validation:** Phase 7 required-output validator checks manifest paths after the generator returns. It is not reached if the Meteor parity exception is thrown. **Confirmed from production code.**

# E. Shared Phase 7 Artifact Inventory

| Artifact | Path | Producer | Validator | Classification | Short/Long | Family scope | Plan-scoped | Execution-scoped | Stale-file risk |
|---|---|---|---|---|---|---|---|---|---|
| Narration plan | `<outputRoot>/narration-v5/narration-plan.json` | `NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync` | Phase7RequiredOutputValidator | required | both | shared | yes | no | yes |
| Narration briefs | `<outputRoot>/narration-v5/narration-briefs.json` | same | Phase7RequiredOutputValidator | required | both | shared | yes | no | yes |
| Knowledge contract/diagnostics | `<outputRoot>/narration-v5/knowledge/*.json` | `KnowledgeFormatter` via generator | Not in required manifest found | diagnostic/contract | both | shared | yes | no | yes |
| Editorial brief contract/diagnostics | `<outputRoot>/narration-v5/editorial-brief/*.json` | `EditorialBriefInterpreter` via generator | Not in required manifest found | diagnostic/contract | both | shared | yes | no | yes |
| Producer notes contract/diagnostics | `<outputRoot>/narration-v5/producer-notes/*.json` | `ProducerNotesComposer` via generator | Producer notes contract path helper exists; not in required manifest found | diagnostic/contract | both | shared | yes | no | yes |
| Raw narrative long | `<outputRoot>/narration-v5/raw-narrative/long/raw-narrative.json` | `RawNarrativeGenerator.Build` | Phase7RequiredOutputValidator | required | long | shared | yes | no | yes |
| Raw narrative short | `<outputRoot>/narration-v5/raw-narrative/short/raw-narrative.json` | `RawNarrativeGenerator.Build` | Phase7RequiredOutputValidator | required | short | shared | yes | no | yes |
| Raw narrative diagnostics | `<outputRoot>/narration-v5/raw-narrative/raw-narrative-diagnostics.json` | generator | Phase7RequiredOutputValidator | required diagnostic | both | shared | yes | no | yes |
| Scene fact cards long | `<outputRoot>/narration-v5/scene-fact-cards/long/scene-fact-cards.json` | `SceneFactCardGenerator.Build` | Phase7RequiredOutputValidator | required | long | shared | yes | no | yes |
| Scene fact cards short | `<outputRoot>/narration-v5/scene-fact-cards/short/scene-fact-cards.json` | `SceneFactCardGenerator.Build` | Phase7RequiredOutputValidator | required | short | shared | yes | no | yes |
| Documentary script long/short | `<outputRoot>/narration-v5/documentary-script/{long,short}/documentary-script.json` | `LlmDocumentaryTranscriptionist.Transcribe` | Phase7RequiredOutputValidator | required | per-format | shared | yes | no | yes |
| Narration long/short/root | `<outputRoot>/narration-v5/{long,short}/narration.json`, `<outputRoot>/narration-v5/narration.json` | generator | Phase7RequiredOutputValidator | required | both/root | shared | yes | no | yes |
| Prompt preview/diagnostics/quality | `<outputRoot>/narration-v5/prompt-preview.md`, `prompt-diagnostics.json`, `prompt-quality.json` | `NarrationPromptComposer.ComposeAndWriteAsync` | Phase7RequiredOutputValidator | required | both | shared | yes | no | yes |
| Runtime composition diagnostics | `<outputRoot>/narration-v5/runtime-composition-diagnostics.json` | `RuntimeCompositionDiagnostics.WriteAsync` | Declared generated file, not in manifest snippet | diagnostic | both | shared | yes | no | possible |
| Semantic diagnostics | `<outputRoot>/narration-v5/required-semantic-fact-diagnostics.json`, `semantic-capability-diagnostics.json`, `semantic-source-context-presence.json` | generator after resolver | Not in manifest found | diagnostic | both | shared | yes | no | can be absent on early parity failure |
| Meteor lifecycle diagnostics | process CWD `narration-v5/meteor-activity-*.json` | `MeteorActivityLifecycleDiagnostics` | none | diagnostic | both | MeteorShower lifecycle | no | no | high |
| Required-output diagnostics | `<outputRoot>/narration-v5/required-output-validation-diagnostics.json` | `WritePhase7RequiredOutputValidationDiagnosticsAsync` | n/a | diagnostic | both | shared | yes | no | yes |
| Phase 7 validation | `<outputRoot>/validation/phase-07-validation.json` and/or generator preflight diagnostics | phase validation logic | phase validation | diagnostic/blocking | both | shared | yes | no | yes |
| Preserved diagnostics | `<outputRoot>/validation/phase-07-preserved-diagnostics/*` | `PreservePhase7DiagnosticEvidenceForOverwrite` | none found | failure diagnostic | both | shared | yes | no | mix risk |

# F. Family-Specific Artifact Differences

- **PlanetConjunction:** Phase 2 has explicit conjunction validation for local peak, direction, best viewing window, angular separation, Venus/Jupiter. Phase 7 uses shared narration artifacts; semantic requirements come through PlanetPairing/PlanetGrouping legacy compatibility. **Confirmed from production code.**
- **MeteorShower:** Phase 7 has additional lifecycle diagnostics and a hard parity assertion that requires retained `Radiant` and `PeakWindow` facts for `MeteorShower`. MeteorActivity projection is expected to derive those legacy facts from canonical `MeteorActivity`. **Confirmed from production code.**
- **Meteor shower catalog coverage:** Geminids and Perseids records exist with radiant constellations and ZHR values. **Confirmed from production code.**
- **Mismatch:** V1 profile requires canonical `MeteorActivity`, legacy profile also lists optional `Radiant/Zhr/DarkSkyGuidance`; adapter context construction currently gates derived MeteorActivity on `ContentStrategy == "MeteorShower"`, not `EventType == "MeteorShower"`. **Strongly supported.**

# G. Output-Path Map

- **Configured media root / plan root:** `ProductionPipelineExecutionService.BuildProductionExecutionContext` combines the supplied `planRoot` with subdirectories: `question-engine`, `hero`, `thumbnails`, `narration`, `tts`, `video-assembly`, `validation`. **Confirmed from production code.**
- **Plan input root:** `<outputRoot>/plan-input`, used for `content-plan-production-request.json`, `production-event-intelligence.json`, and diagnostics. **Confirmed from production code.**
- **Narration V5 directory:** `<outputRoot>/narration-v5`, built by `BuildNarrationV5Root` and directly in `NarrationGeneratorV5`. **Confirmed from production code.**
- **Validation directory:** `<outputRoot>/validation`, built in execution context and used for validation and preserved diagnostics. **Confirmed from production code.**
- **Preserved diagnostics directory:** `<outputRoot>/validation/phase-07-preserved-diagnostics`. **Confirmed from production code search.**
- **Process-relative diagnostics directory:** `narration-v5` under `Path.GetFullPath(relativePath)`, therefore under `Environment.CurrentDirectory` (for API likely `Backend/src/Astronomy.MediaFactory.Api` when run from the API project). **Confirmed from production code.**
- **Escaping writes found:** `meteor-activity-context-diagnostics.json`, `meteor-activity-adapter-diagnostics.json`, `meteor-activity-resolution-diagnostics.json`, `meteor-activity-projection-diagnostics.json`, `meteor-activity-beat-assignment-diagnostics.json`. **Confirmed from production code.**

# H. Validation Map

| Validator | Registration | Invocation | Phase | Boundary | Severity | Failure behavior | Current-execution awareness |
|---|---|---|---|---|---|---|---|
| `RuntimeCompositionDiagnostics.ValidateServiceRegistrations` | called in `AddProductionSemanticRuntimeV1` | startup/DI build | global/7 | before | blocking | throws on bad resolver registration | n/a |
| `ValidatePhase7ChronicleCoreInputs` | direct method | start of `PhaseGenerateNarrationPlanAsync` | 7 | before | blocking | throws if Phase 4-6 files missing | checks existing files only |
| semantic registry coverage | generator direct call to `SemanticDefaults.SemanticCapabilitySourceRegistry.ValidateCoverageDetailed` | before resolver | 7 | before | blocking | throws if invalid semantic registrations | registry state only |
| `ValidateFullProductionSemanticInput` | generator direct call | before resolver | 7 | before | blocking | throws on missing full production semantic input | input presence only |
| MeteorActivity parity assertion | generator direct `if MeteorShower && (radiant==0 || peakWindow==0)` | after resolver, before semantic diagnostics write | 7 | during | blocking | throws `InvalidOperationException` | current resolver result, but reads plan-scoped context diagnostic path that may not exist |
| `NarrationRealizationValidator` | direct static | after resolver | 7 | before prompt | blocking via combined validation | throws if blocking/cannot realize | current result |
| `RequiredSemanticFactPhase7Validator` | direct static | after resolver | 7 | before prompt | blocking via combined validation | throws on blocking semantic facts | current result |
| `NarrationContextPurityValidator` | direct static | before prompt | 7 | before prompt | blocking | throws on purity failures | current generated context |
| `Phase7RequiredOutputValidator` manifest | direct method | after generator returns | 7 | after | blocking if required missing/empty/invalid | throws via phase validation | existing file checks, no timestamp/execution id |
| Phase 2 PlanetConjunction validator | direct method | `PhaseBuildProductionIntelligenceAsync` | 2 | after write | blocking | throws on missing conjunction fields | current intelligence object |
| `SemanticSourcePolicyRegistryConsistencyValidatorV1` | DI singleton | tests/possible composition diagnostics | semantic | before/runtime | blocking if invoked | throws/returns errors | registry/policy state |
| `SemanticSourceAdapterRegistryV1.CertifyAgainstPolicies` | registry method | tests/diagnostics | semantic | before/runtime | errors in validation result | non-throw unless caller enforces | registry/policy state |

# I. Test Coverage Map

| Test | Fixture type | Production-shaped | Real DI | API host | Artifacts generated | What it proves | Coverage gap |
|---|---|---:|---:|---:|---:|---|---|
| `MeteorShowerExecutableFamilyCoverageTests` | controlled MeteorShower | no | some tests yes | no | no/limited | MeteorActivity context, adapter candidate, canonical resolution, Radiant/PeakWindow projection, beat retention | Uses `ContentStrategy="MeteorShower"`, often `SourceExternalEventId="geminids-2026"`, timezone present |
| `SemanticSourceAdaptersV1Tests` | unit adapter | no | no | no | no | adapter emits/rejects typed values | Does not cover API request shape |
| `SemanticSourcePolicyCatalogV1Tests` | policy unit | no | no | no | no | MeteorActivity policy has domain rules | Does not prove runtime source context |
| `SemanticSourcePolicyRegistryConsistencyValidatorV1Tests` | registry consistency | no | no | no | no | policy/adapter consistency | Does not run Phase 7 |
| `ApiHostRuntimeCompositionDiagnosticsTests` | API-host services | partial | yes | service collection | no | exactly one resolver and expected runtime types | Does not run Geminids production request |
| `Phase7ProductionDiSemanticBindingTests` | production DI semantic binding | partial | yes | no | limited | resolver/engine/adapter binding | Fixture drift from failing API shape possible |
| `Phase7SemanticSourceContextIntegrationTests` | resolver input integration | partial | no/default | no | no | source context population integration | Not full API host |
| `NarrationPreviewRequestTests` | request preview fixtures | closer for Geminids/Perseids/PlanetConjunction | no | likely controller/service | no | preview metadata for meteor showers and conjunctions | Not RC2 Phase 7 RebuildOutputs |
| `CurrentSemanticArchitectureCharacterizationTests` | source/architecture characterization | n/a | no | no | no | current architecture guardrails | Characterizes, does not execute production |
| Planet conjunction certification-style tests | DB/service fixtures | partial | mixed | no | yes for some services | Planet conjunction success patterns across planning/assets/narration | Not a single end-to-end phases 1-7 proof found in this pass |

Fixture differences explicitly identified: controlled Meteor fixtures use `Category=Astronomy`, `ContentStrategy=MeteorShower`, `SourceExternalEventId=geminids-2026`, `TimeZone=America/New_York`, `VerificationStatus=Verified`; failing shape uses `Category=RareEventAlert`, `ContentStrategy=LocalViewingGuide`, `SourceExternalEventId=meteor-shower-geminids-2026`, `TimeZone=null`, `VerificationStatus=Approximate`, `RegionId=IN-RJ-UDAIPUR`. **Confirmed from test code/user production shape.**

# J. Confirmed Architectural Gaps

## Confirmed

- Process-relative MeteorActivity lifecycle diagnostic writes escape the plan output root. **Confirmed from production code.**
- Phase 7 required-output validation is file-presence/length/JSON-root based and not execution-aware. **Confirmed from production code.**
- Family contract information is distributed across V1 profiles, V1-to-legacy adapter, legacy profile catalog, legacy capability map, source policies, adapter registry, and output manifest. **Confirmed from production code.**
- MeteorActivity projection rule IDs are `V1Projection.MeteorActivity.Radiant` and `V1Projection.MeteorActivity.PeakWindow`. **Confirmed from production code.**
- Current checkout branch is not the requested branch. **Confirmed from command output.**

## Strongly supported

- Realistic API MeteorShower can diverge at source context population because `BuildMeteorActivityFromRequest` checks `ContentStrategy == "MeteorShower"` rather than EventType/family. **Strongly supported.**
- Existing controlled fixtures do not prove the failing API-shaped request. **Strongly supported.**
- Existing Meteor beat-assignment exception is later than the earliest likely lifecycle divergence. **Strongly supported.**
- Existing preserved diagnostics can mix files from separate executions if destination is not cleared and no execution timestamp/id manifest is enforced. **Strongly supported; exact preservation details require deeper code lines in Step 2.**

## Inferred

- API current directory is likely `Backend/src/Astronomy.MediaFactory.Api` in production runs, making process-relative `narration-v5` appear there. **Inferred from .NET hosting conventions and diagnostic code using current directory.**
- PlanetConjunction production success relies on legacy compatibility preserving old terms rather than a single family execution contract. **Inferred from code paths.**

## Not yet found

- A single authoritative family execution contract covering profiles, sources, projections, artifacts, validators, and diagnostics. **Not yet found.**
- An execution-scoped artifact manifest for Phase 7 outputs with start timestamp/execution id and stale-file rejection. **Not yet found.**
- A full API-host RC2 RebuildOutputs test using the exact failing Geminids shape. **Not yet found.**

# K. Recommended Implementation Sequence for Step 2 Onward

1. Add read-only/current-execution diagnostics to show source context population, including `EventType`, `ContentStrategy`, `SourceExternalEventId`, `PrimaryObjects`, `ShortTitle`, `TimeZone`, and MeteorActivity presence, without changing resolution behavior.
2. Move/copy lifecycle diagnostics into plan-scoped paths while keeping old writes temporarily for comparison; do not use moved diagnostics for decisions initially.
3. Add a realistic API-shaped Geminids regression test with `ContentStrategy=LocalViewingGuide`, `SourceExternalEventId=meteor-shower-geminids-2026`, `TimeZone=null`, `VerificationStatus=Approximate`, `RegionId=IN-RJ-UDAIPUR`.
4. Assert lifecycle ordering: source context population → adapter invocation → candidate → canonical → projection → beat retention.
5. Only after the failing stage is proven, adjust MeteorActivity source context eligibility/normalization in the smallest safe way preserving Planet Conjunction behavior.
6. Introduce execution-scoped Phase 7 artifact manifest/timestamps to eliminate stale validation risk.
7. Consolidate family execution contract as a later step, after behavioral characterization is locked.

# L. Files Required for the Next Implementation Chat

- `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Diagnostics/MeteorActivityLifecycleDiagnostics.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Families/AstronomyFamilyProfileCatalogV1.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Families/Compatibility/AstronomyFamilyProfileV1CompatibilityAdapter.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/LegacyRequiredSemanticFactCompatibilityMapper.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters/Event/SemanticSourceAdaptersV1.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters/Registry/SemanticSourceAdapterRegistryV1.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Catalog/SemanticSourcePolicyCatalogV1.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Catalog/MeteorShowerKnowledgeCatalogV1.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs`
- `Backend/tests/Astronomy.MediaFactory.Tests/MeteorShowerExecutableFamilyCoverageTests.cs`
- `Backend/tests/Astronomy.MediaFactory.Tests/Phase7ProductionDiSemanticBindingTests.cs`
- `Backend/tests/Astronomy.MediaFactory.Tests/ApiHostRuntimeCompositionDiagnosticsTests.cs`
- `Backend/tests/Astronomy.MediaFactory.Tests/NarrationPreviewRequestTests.cs`

# M. Discovery Command Log

- `git status`, `git branch --show-current`, `git log -1 --oneline`: repository was clean, branch `work`, HEAD `c15e6d4 fixed test case issue`.
- `find .. -name AGENTS.md -print`: no `AGENTS.md` discovered in or above the workspace.
- `git grep -n` for requested tokens (`RequiredSemanticFactResolver`, `PhaseGenerateNarrationPlanAsync`, `MeteorActivityLifecycleDiagnostics`, `ISemanticSourceAdapterV1`, `MeteorActivityValue`, `ProductionEventIntelligence`, `phase-07-preserved-diagnostics`, `narration-v5`, etc.): located resolver/generator in `NarrationGeneratorV5.cs`, phase orchestration in `ProductionPipelineExecutionService.cs`, lifecycle diagnostics, adapters, catalogs, policies, and tests.
- `find Backend/src/.../Semantics -type f | sort`: enumerated semantic catalog/profile/policy/adapter files.
- `nl -ba ... | sed -n ...`: inspected line-numbered production files for DI, profile catalog, policy catalog, adapter registry, compatibility mapper, lifecycle diagnostics, Phase 7 orchestration, Phase 7 manifest, production intelligence writing/building, and resolver/generator lifecycle.
- `rg -n ... Backend/src Backend/tests`: identified tests, artifact path strings, diagnostics, PlanetConjunction/MeteorShower references, and validation paths.

# N. Unresolved Questions

- What exact process current directory is used by the deployed API service when the failing production run executes?
- What were the exact Phase 1-6 generated files for the failing Geminids execution, including actual `content-plan-production-request.json` and `production-event-intelligence.json` contents?
- Was any process-relative `Backend/src/Astronomy.MediaFactory.Api/narration-v5` directory present before the failing run, and if so from which execution?

# Machine-Readable Appendix

```json
{
  "branch": "work",
  "requestedBranch": "RecoveryBrach-18-05-26",
  "headCommit": "c15e6d4 fixed test case issue",
  "discoveryOnly": true,
  "productionCodeModified": false,
  "reportFileCreated": "docs/discovery/family-execution-contract-step-1-discovery.md",
  "semanticSourceOfTruth": {
    "status": "confirmed",
    "files": [
      "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Families/AstronomyFamilyProfileCatalogV1.cs",
      "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Families/Compatibility/AstronomyFamilyProfileV1CompatibilityAdapter.cs"
    ]
  },
  "phase7": {
    "orchestratorFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs"],
    "resolverFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs"],
    "adapterRegistryFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters/Registry/SemanticSourceAdapterRegistryV1.cs"],
    "sourcePolicyFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Catalog/SemanticSourcePolicyCatalogV1.cs"],
    "projectionFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/LegacyRequiredSemanticFactCompatibilityMapper.cs"],
    "validatorFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Diagnostics/RuntimeCompositionDiagnostics.cs"],
    "pathBuilderFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Diagnostics/MeteorActivityLifecycleDiagnostics.cs"],
    "diagnosticFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Diagnostics/MeteorActivityLifecycleDiagnostics.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Diagnostics/RuntimeCompositionDiagnostics.cs"]
  },
  "families": {
    "PlanetConjunction": {
      "profileFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Families/AstronomyFamilyProfileCatalogV1.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs"],
      "adapterFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters/Event/SemanticSourceAdaptersV1.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters/Knowledge/KnowledgeAdaptersV1.cs"],
      "policyFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Catalog/SemanticSourcePolicyCatalogV1.cs"],
      "projectionFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/LegacyRequiredSemanticFactCompatibilityMapper.cs"],
      "artifactProducerFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs"],
      "validatorFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs"],
      "testFiles": ["Backend/tests/Astronomy.MediaFactory.Tests/NarrationPreviewRequestTests.cs", "Backend/tests/Astronomy.MediaFactory.Tests/FinalNarrationServiceTests.cs", "Backend/tests/Astronomy.MediaFactory.Tests/SceneAssetsV3ServiceTests.cs"]
    },
    "MeteorShower": {
      "profileFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Families/AstronomyFamilyProfileCatalogV1.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs"],
      "adapterFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters/Event/SemanticSourceAdaptersV1.cs"],
      "policyFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Catalog/SemanticSourcePolicyCatalogV1.cs"],
      "projectionFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/LegacyRequiredSemanticFactCompatibilityMapper.cs"],
      "artifactProducerFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs"],
      "validatorFiles": ["Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs", "Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs"],
      "testFiles": ["Backend/tests/Astronomy.MediaFactory.Tests/MeteorShowerExecutableFamilyCoverageTests.cs", "Backend/tests/Astronomy.MediaFactory.Tests/NarrationPreviewRequestTests.cs", "Backend/tests/Astronomy.MediaFactory.Tests/SemanticSourceAdaptersV1Tests.cs"]
    }
  },
  "confirmedEscapingWrites": [
    "narration-v5/meteor-activity-context-diagnostics.json",
    "narration-v5/meteor-activity-adapter-diagnostics.json",
    "narration-v5/meteor-activity-resolution-diagnostics.json",
    "narration-v5/meteor-activity-projection-diagnostics.json",
    "narration-v5/meteor-activity-beat-assignment-diagnostics.json"
  ],
  "confirmedStaleArtifactRisks": [
    "Phase7RequiredOutputValidator checks existing files by path, length, and JSON root only",
    "MeteorActivity lifecycle diagnostics are process-relative and not execution-scoped",
    "Preserved diagnostics destination lacks confirmed execution-id manifest in this discovery"
  ],
  "realisticFixtureGaps": [
    "ContentStrategy LocalViewingGuide versus controlled MeteorShower",
    "SourceExternalEventId meteor-shower-geminids-2026 versus controlled geminids-2026",
    "TimeZone null versus controlled America/New_York",
    "VerificationStatus Approximate versus controlled Verified",
    "RegionId IN-RJ-UDAIPUR versus controlled fixtures"
  ],
  "earliestLikelyProductionDivergence": {
    "stage": "Source context population before adapter invocation",
    "status": "strongly-supported",
    "evidence": [
      "BuildMeteorActivityFromRequest returns null unless ContentStrategy == MeteorShower",
      "Actual API-shaped request has ContentStrategy == LocalViewingGuide",
      "ReadMeteorActivity only extracts root zhr from production-event-intelligence JSON"
    ]
  },
  "filesNeededForStep2": [
    "Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs",
    "Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs",
    "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Diagnostics/MeteorActivityLifecycleDiagnostics.cs",
    "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Catalog/MeteorShowerKnowledgeCatalogV1.cs",
    "Backend/tests/Astronomy.MediaFactory.Tests/MeteorShowerExecutableFamilyCoverageTests.cs",
    "Backend/tests/Astronomy.MediaFactory.Tests/Phase7ProductionDiSemanticBindingTests.cs"
  ]
}
```
