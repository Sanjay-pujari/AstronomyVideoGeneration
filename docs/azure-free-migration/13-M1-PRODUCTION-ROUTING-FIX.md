# M1-03 — RC2 production narration provider routing

## 1. Root cause

The supplied production trace identifies `AzureOpenAiContentGenerationService.RequestCompletionAsync`, deployment `gpt-4o`, HTTP 401, and failure at Phase 7. That establishes that the running process selected Azure. It does not expose that process's configuration providers or prove an Ollama-configured process ignored its selection.

Repository inspection found no second Phase 7 factory bypassing `INarrationPerformer`. The actual factory already supports Ollama, but API settings and the original launch profile do not select it. `NarrationAIOptions.Provider` defaults to `AzureOpenAI`. `Backend/narration-ollama.runtime.example.json` is only an example consumed by the isolated certification runner; API `Program.cs` never loads it. Installing Ollama or running the M1-02 runner therefore does not change API routing. Missing effective selection is the reproducible repository configuration cause; stale binaries or local overrides cannot be ruled out without the Windows process's diagnostics.

The correction is an explicit `RC2-Ollama` API launch profile, diagnostics at the factory and invocation boundaries, and production-container regression coverage. Default Azure behavior is preserved rather than inferred from model installation or availability.

## 2. Existing implementation

`Backend/src/Astronomy.MediaFactory.ContentGen/OllamaNarrationPerformer.cs`, `OllamaNarrationPerformer.InvokeAsync`, is reused. No second provider was introduced. It verifies installed model identity through `/api/tags` before/after `/api/chat`, rejects truncated or malformed structured output, enforces context/output budgets, shares a concurrency limiter and applies a total deadline. It does not pull models or fall back to Azure.

## 3. Actual Phase 7 selection path

All paths below are under `Backend/src`.

1. `Astronomy.MediaFactory.Api/Controllers/ContentPlanningRc2Controller.cs`, `BatchGenerateFromPlans` → `IRc2ContentPlanningBatchOrchestrator.GenerateFromPlansAsync`.
2. `Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2ContentPlanningBatchOrchestrator.cs`, `GenerateFromPlansAsync` → `IContentPlanBatchGenerationService.GenerateFromPlansAsync`. It observes Phase 7 results without rerunning the generator.
3. `Astronomy.MediaFactory.Infrastructure/Persistence/ContentPlanBatchGenerationService.cs`, `GenerateFromPlansAsync`, selects `IContentPlanProductionExecutionService.ExecuteContentPlanWithProductionPipelineAsync` for `UseProductionPipeline`. `ContentPlanProductionExecutionService` then calls `IProductionPipelineExecutionService.ExecuteAsync`.
4. `Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`, `RunAsync` dispatches Phase 7 to `ExecutePhase7NarrativeLifecycleAsync`; committed authority preparation precedes documentary lifecycle execution.
5. `Astronomy.MediaFactory.Infrastructure/DocumentaryBlueprint/DocumentaryNarrativeLifecycleIntegrationService.cs`, `ExecuteAsync` → `InvokeGeneratorAsync` → `NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync` with long/short composition authority.
6. `Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` invokes its injected `INarrationPerformer.InvokeAsync` for each variant. The generator is registered scoped in `ServiceCollectionExtensions.AddMediaFactory`.
7. `Astronomy.MediaFactory.Infrastructure/Extensions/NarrationAIServiceCollectionExtensions.cs`, `AddNarrationAI`, is the exact selection point: bound `NarrationAI:Provider` → `options.IsOllama` → `OllamaNarrationPerformer`; otherwise validated Azure selection → `AzureOpenAiContentGenerationService`.
8. The Azure implementation's `InvokeAsync` → `RequestCompletionAsync` explains the supplied failure. Ollama selection instead uses the existing local implementation's `InvokeAsync`; it never crosses that Azure method.

## 4. Effective configuration and startup

`Api/Program.cs` uses `WebApplication.CreateBuilder(args)` then `AddMediaFactorySecureConfiguration`. Standard sources are application JSON, environment-specific JSON, Development user secrets when configured, environment variables and command-line arguments. Secure configuration then appends environment variables again and optionally Azure Key Vault last. Later providers win. Thus this repository's final environment pass can override command-line values, and configured Key Vault values can override environment variables. No additional narration-specific settings file is loaded by the API. No API `UserSecretsId` was found; local/external hosting configuration remains outside this inspection.

`appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json` and the original launch profile have no `NarrationAI:Provider` override. Their effective repository narration selection is Azure by default. The new `RC2-Ollama` launch profile explicitly supplies the following environment values; it must actually be selected when launching the API:

| Key under `NarrationAI` | Value |
| --- | --- |
| `Provider` | `Ollama` |
| `Ollama:BaseUrl` | `http://127.0.0.1:11434` |
| `Ollama:Model` | `qwen2.5:7b-instruct-q4_K_M` |
| `Ollama:ExpectedModelDigest` | `845dbda0ea48ed749caafd9e6037047aa19acfcfd82e704d7ca97d631a0b697e` |
| `Ollama:TimeoutSeconds` | `3600` |
| `Ollama:MaxConcurrentRequests` | `1` |
| `Ollama:ContextWindow` | `32768` |
| `Ollama:MaxOutputTokens` | `12288` |
| `Ollama:MaxRetries` | `0` |

The context/output values are bounded capacity settings, not measured workstation throughput or memory certification. Other deployments may use normal `NarrationAI__...` environment keys; `RC2_CERT_...` belongs only to the separate M1-02 runner. Explicit Azure selection remains supported. Nothing automatically selects Azure if Ollama is down.

Existing options validators check endpoint syntax, model name, digest format and limits at startup. Local selection skips Azure OpenAI credential validation and does not construct the Azure narration client. Model presence/digest and connectivity are checked at invocation, not by an eager startup network probe, so unrelated operations can run while Ollama is temporarily unavailable. Unavailable/missing/mismatched models fail clearly at the local provider boundary. Other services' existing Azure startup requirements remain separate.

`RC2_AI_PROVIDER_SELECTED` logs safe provider/model identity, phase, invocation type and correlation ID. Factory logs distinguish explicit selection from default Azure. Pre-inference logs say `NotObserved` for digest; the local provider logs the actual observed digest after verification. Existing outcome logs retain prompt/template version, duration, validation and failure category. Generic request diagnostics now use the actual resolved model instead of a placeholder identity, and failure diagnostics use the local timeout when appropriate. No credentials or raw prompts are added to these logs.

## 5. Files changed

- `Backend/src/Astronomy.MediaFactory.Api/Properties/launchSettings.json`: explicit opt-in local API profile; publishing/staging disabled in that profile.
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/NarrationAIServiceCollectionExtensions.cs`: safe factory selection diagnostics.
- `Backend/src/Astronomy.MediaFactory.ContentGen/OllamaNarrationPerformer.cs`: selection event with observed digest and call correlation.
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs`: local output estimation, actual model/timeout diagnostics, selection event, and no second generator attempt for nonretryable transport/auth/model failures. Structured-output repair retains its existing bounded attempt.
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationRuntimeBudget.cs`: duration/language-based local budget estimate with fail-closed capacity handling.
- `Backend/tests/Astronomy.MediaFactory.Tests/ProductionNarrationRoutingTests.cs`: actual production container and mocked generator/provider checks.
- `Backend/tests/Astronomy.MediaFactory.Tests/OllamaNarrationPerformerTests.cs`: verifies the new observed-digest selection event alongside existing safe provenance.
- This report.

No narration schema, context/fact/constraint, scene identity/order, long/short authority, Phase 19/20 or publishing contract was redesigned. Existing scientific and language gates remain in force. Grounded prompt composition from M1-02 is preserved. These gates are heuristic; routing tests do not establish universal unsupported-claim detection or scientific certification of arbitrary model output.

### Output capacity

Azure budgets remain unchanged. Local requests estimate speech words from each scene's target duration (minimum 40 words/scene), use 140 English or 125 Hindi words/minute, estimate 2 English or 4 Hindi tokens/word, add 20% margin and JSON overhead. Exceeding the configured maximum fails before HTTP with `P7_PROVIDER_OUTPUT_BUDGET_INSUFFICIENT`. The provider independently checks input plus output against the context window. This intentionally does not silently reduce required output to fit a cap.

For illustration only, 12 scenes totaling 600 seconds estimate 3968 English or 6608 Hindi output tokens; 4 scenes totaling 80 seconds estimate 672 English or 1024 Hindi tokens. At 4.6 tokens/second, the long examples need roughly 863/1437 seconds of decoding alone. Actual scene targets, tokenization, prefill, response completeness and server memory must be checked locally. An oversized invocation fails clearly. A follow-up could batch consecutive canonical scenes while preserving SceneId/order and aggregate only after all scene validations; no batching or pipeline redesign is implemented here.

## 6. Production DI result

**PASS: 2 provider-selection cases.** Tests construct `AddMediaFactory`, resolve its real scoped `NarrationGeneratorV5`, and assert that the exact injected performer is the selected local/Azure implementation. Local configuration contains no Azure OpenAI credentials. Merely registering a directly constructed Ollama object is not used as proof.

## 7. Mocked Phase 7 result

**PASS: mocked generator integration, 2 scene-contract cases and 4 local budget cases.** The generator test supplies disposable English Orion hook-scene Phase 7 composition fixtures in a temporary directory and uses fake Ollama HTTP. It supplies a controlled catalog-backed `IRequiredSemanticFactResolver` fixture and exercises the real `BuildAndWriteDiagnosticsAsync`, composition/projection, long/short invocation and output validation. Container-resolution tests retain the real production resolver. Separate production-resolved performer tests verify 12 long scenes and 4 short scenes in the unchanged structured schema. These are generator/container integration tests, not a controller/database run or proof that Phase 1–6 authority publication succeeds. A minimal fixture with the unmodified production resolver stopped at missing `ScientificIdentity` before provider invocation; a planetary fixture similarly lacked resolved `ObservationTiming`. Those observations are not evidence of incorrect provider routing, and the mocked routing test does not certify those upstream semantic projections. Real endpoint acceptance still requires complete committed inputs and local execution; no production semantic gate was weakened to make a routing test pass.

## 8. Real isolated Ollama result

**NOT RUN.** The user runs Ollama/API only on Windows. Cloud loopback is not that Windows service. No published PlanId was rerun, and no cloud API/model service was started. Local inference and digest observation must be reported from actual execution, not inferred from the configured digest or mocked responses.

## 9. Azure OpenAI count

The mocked local generator test installs an Azure client construction trap and asserts zero Azure client initializations. Its only allowed narration transport is fake Ollama HTTP, so Azure OpenAI HTTP calls are zero in that test. No real Azure request is made by the tests. This is not telemetry for the user's Windows production process.

## 10. Phase 8 status

`ProductionPipelineExecutionService.PhaseGenerateSceneImagesAsync` selects authority scene assets, legacy scene variants or legacy infographic rendering. No separate `INarrationPerformer` factory or direct `AzureOpenAiContentGenerationService.InvokeAsync` was found there. Reuse of the same narration abstraction therefore uses the same DI selection; image-generation routes do not become Ollama routes. Phase 8 was not executed in this task.

## 11. Remaining Azure dependencies

| Route | Status |
| --- | --- |
| Phase 7 | Existing `INarrationPerformer` selection; local profile selects Ollama |
| Conditional Phase 8 | Visual/assets branch; no additional narration selector found; image dependencies remain |
| Phase 12 | `PhaseGenerateThumbnailsAsync` / `MatureThumbnailCandidatePublisher` retain Azure image configuration/provider gates |
| Phase 13 | `PhaseGenerateGalleryAsync` / Rendering `AstroPulseGalleryService.GenerateGalleryAsync` retain direct Azure image HTTP |
| Phase 15 | `Phase15RealTtsV2Async` / `GenerateAndValidateTtsAudioAsync` retain Azure Speech gates/voice policy |
| Legacy narration | `IScriptGenerationService`, `IShortsScriptGenerationService`, `IMetadataOptimizationModelClient` still resolve directly to Azure in `ServiceCollectionExtensions`; outside selected Phase 7 narration |
| Legacy TTS | Rendering `AzureSpeechSynthesisService` / `AzureSpeechClient` remain Azure |
| Direct HTTP | Azure narration adapter, cinematic image generator and gallery image helper retain direct HTTP/configuration routes; speech uses the Cognitive Services Speech SDK |

Storage, Key Vault and other application Azure dependencies also remain. Selecting Ollama for narration does not migrate these services. No replacements were implemented.

## 12. Build/test results

- Solution build: **PASS**, 0 errors; existing warnings remain.
- New `ProductionNarrationRoutingTests`: **9 passed, 0 failed**.
- Targeted regression suite: **133 passed, 0 failed, 0 skipped**, covering production routing, Ollama transport/provenance, provider registration, grounding, startup validation, generated prose validation and documentary lifecycle integration.
- Selection diagnostic provenance includes actual mocked digest, phase/model/variant/correlation and prompt privacy. Cancellation, timeout, invalid model/JSON and local credential independence retain their existing regression coverage.
- `git diff --check`: passed. Azure provider source and Phase 19/20/social contract source files were not changed.
- Workspace evidence: `/workspace/.setup/logs/m1-03-tests.log`, `m1-03-regression.log`, `m1-03-results/m1-03.trx`, `m1-03-build.log`.
- This is a targeted suite, not a claim that every repository test passes. Known baseline failures documented in M1-02 remain outside this run.
- Real Windows endpoint/model inference: **NOT RUN**. Full committed-authority acceptance and scientific review remain local follow-up checks.

## 13. Exact safe next local test

First run the mocked production integration tests (no model or database access):

```powershell
dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --filter FullyQualifiedName~ProductionNarrationRoutingTests
```

Launch the local API explicitly with the new profile and the usual unrelated application prerequisites:

```powershell
dotnet run --project Backend/src/Astronomy.MediaFactory.Api --launch-profile RC2-Ollama
```

For a real Phase 7 endpoint test, first provision an **unpublished disposable plan with its own output root and valid Phase 1–6 committed inputs** in the local test database. An arbitrary new GUID alone is not a valid plan. Substitute that plan's ID/year/region/language; do not use `baa5af31-4ba9-4d1d-8ef3-0796210a9ed2` or any other certified plan. Then send this exact restricted body to `POST http://localhost:59236/api/content-planning/rc2/batch-generate-from-plans`:

```json
{
  "year": 2026,
  "regionId": "IN-RJ-UDAIPUR",
  "language": "en",
  "planId": "<DISPOSABLE_UNPUBLISHED_PLAN_ID_WITH_VALID_PHASE_6_INPUTS>",
  "maxPlans": 1,
  "useProductionPipeline": true,
  "startPhaseNo": 7,
  "endPhaseNo": 7,
  "dependencyExpansionMode": "ReadOnly",
  "dryRun": false,
  "overwriteExisting": false,
  "allowCompletedPlanRerun": false,
  "publishApproved": false
}
```

Use a separate Hindi plan and Hindi-owned authoritative inputs for the Hindi test. Review selection/outcome events, actual digest, both variants, response contracts and scientific acceptance before claiming live PASS. Never extend this request to Phase 8–20 for this task. No certified media, publication checkpoints, canonical production folders or existing PlanIds were modified here.
