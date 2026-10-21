# M1-02 — Ollama RC2 runtime certification

Status: **NOT CERTIFIED — local Windows .NET inference and scientific review pending.**

The user confirmed that the API and Ollama run only on their local machine. This change prepares repository code, regression checks and a disposable local runner. No cloud Ollama/API service was started. Mock responses and a dry run are not real model inference or end-to-end Phase 7 certification.

Working branch: `codex/m1-01-ollama-narration-pilot`; starting revision: `7cdfbbbb225a277fee45a6f5e38077a06c8f39fd`. Azure remains the default and its implementation is preserved. No Phase 19/20, social publishing, certified PlanIds or publishing records were changed.

## 1. Actual provider and invocation paths

Paths below are relative to `Backend/src/`.

| Concern | Exact file and methods |
| --- | --- |
| Provider contract | `Astronomy.MediaFactory.ContentGen/NarrationPerformer.cs`: `INarrationPerformer.PerformAsync`, `InvokeAsync`; existing call/result DTOs unchanged |
| Local provider | `Astronomy.MediaFactory.ContentGen/OllamaNarrationPerformer.cs`: `PerformAsync`, `InvokeAsync`, `ResolveDigestAsync`, `SendAsync` |
| Azure provider | `Astronomy.MediaFactory.ContentGen/AzureOpenAiContentGenerationService.cs`: `PerformAsync`, `InvokeAsync`, `RequestCompletionAsync`; unchanged |
| DI | `Astronomy.MediaFactory.Infrastructure/Extensions/NarrationAIServiceCollectionExtensions.cs`: `AddNarrationAI` |
| Options | `Astronomy.MediaFactory.Contracts/NarrationAIOptions.cs`; `Astronomy.MediaFactory.Infrastructure/Configuration/NarrationAIOptionsValidator.cs`: `Validate` |
| Phase 7 entry | `Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`: `ExecutePhase7NarrativeLifecycleAsync` |
| Narrative lifecycle | `Astronomy.MediaFactory.Infrastructure/DocumentaryBlueprint/DocumentaryNarrativeLifecycleIntegrationService.cs`: `ExecuteAsync`, `InvokeGeneratorAsync` |
| Generator | `Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs`: `BuildAndWriteDiagnosticsAsync`, `ParseProviderNarration`; invokes `INarrationPerformer.InvokeAsync` |
| Evidence projection | `Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationPromptComposer.cs`: `ProviderSemanticProjection.Project`, `AssessMeaningfulContext`, `NarrationPromptComposer.Compose` |

Phase 8 remains the scene visual/assets path in `ProductionPipelineExecutionService`, including `RenderPhase8SceneVisualVariantsAsync`, using Phase 7 authority through `IPhase8AuthorityLoader`. It is not another narration provider selection point. The runner does not enter either phase lifecycle or publish authority.

## 2. DI selection

`AddNarrationAI` resolves `INarrationPerformer` to `OllamaNarrationPerformer` only for explicit Ollama configuration, otherwise to the existing Azure implementation. Ollama does not resolve the Azure provider or require Azure OpenAI credentials. Invalid provider configuration fails validation; transport/model/output errors do not select another paid provider.

The isolated runner uses that production registration with no Azure credentials. An Azure resolution trap and an HTTP allowlist permit only the configured Ollama origin's `/api/tags` and `/api/chat`. Redirects are disabled. Other API startup dependencies, including storage/speech, are outside this isolated registration; this does not certify an entirely Azure-free API startup.

## 3. Model and digest

- Configured tag: `qwen2.5:7b-instruct-q4_K_M`.
- Configured digest: `845dbda0ea48ed749caafd9e6037047aa19acfcfd82e704d7ca97d631a0b697e`.
- Real locally observed digest in this task: **NOT RUN**.

The production provider verifies the exact installed tag/digest before and after inference, rejects missing/mismatched/cloud models, and never pulls a model. Test digests are simulated and are not evidence of the installed model.

## 4. Effective configuration

`Backend/narration-ollama.runtime.example.json` is an explicit local example, not automatically loaded into the API. Existing application defaults remain Azure. The runner loads the supplied file then `RC2_CERT_` environment overrides and records the resulting configuration.

| Setting | Local example |
| --- | --- |
| Provider / endpoint | Ollama / `http://127.0.0.1:11434` |
| Timeout | 1500 seconds total per provider call, including queue, tag checks and inference |
| Shared concurrency | 1 |
| Context window | 8192 tokens |
| Maximum configured output | 4096 tokens |
| Isolated EN/HI request output | 768 tokens per request |
| Retries | 0; no repeated long inference or paid fallback |

At the user-reported 4.6 generated tokens/second, 4096 tokens alone take about 891 seconds; the timeout allows additional prefill and HTTP work. This is a bound, not a latency guarantee. Input token estimation is approximate. To use these settings in the API, merge the `NarrationAI` section into its normal configuration or use its normal `NarrationAI__...` environment keys. `RC2_CERT_` applies only to this runner.

## 5–6. English and Hindi .NET inference

Both real inference results: **NOT RUN — execute locally**. The user previously reported successful English inference and grounded Hindi inference with `DoneReason=stop`, 77.51 seconds, 282 generated tokens and 4.60 tokens/second. Those results are not outputs from this repository's .NET runner and do not establish RC2 contract compliance.

Run from the repository root on Windows with the installed model and .NET 10 SDK:

```powershell
dotnet run --project Backend/tools/Astronomy.MediaFactory.NarrationCertification -- --config Backend/narration-ollama.runtime.example.json --dry-run
dotnet run --project Backend/tools/Astronomy.MediaFactory.NarrationCertification -- --config Backend/narration-ollama.runtime.example.json --live
```

No API startup is required. The runner invokes the production composer, semantic projection, DI-selected provider and existing prose/language validators. Each language gets a new disposable PlanId. Hindi evidence is independently localized from the catalog, never read from an English canonical PlanId. All outputs go to a newly created `%TEMP%/rc2-ollama-certification-*` directory printed by the runner, with evidence hashes, requests, responses, validations, provider events, effective configuration and `certification-summary.json`. No database, canonical artifact reader/writer, TTS, video or publishing service is registered.

Dry-run exit zero means only preparation succeeded. Live exit zero means the bounded automated checks passed and scientific review remains required; it does not mean certified. Invalid output returns exit 1; configuration failures return 2. Ctrl+C cancels the call. The summary always sets `certified=false` so heuristic checks cannot silently grant authority.

## 7. Structured JSON and contract validation

Production transport validates JSON, exact required fields, scene count/order, nonempty narration, response model, stop reason, digest consistency and bounded response size. The isolated runner additionally requires the existing one-scene `Short` shape, complete terminal punctuation, language/prose validation and 45–100 words. Existing phase contract records were not changed.

The disposable scene has a nominal 35-second target. Reported speech duration is a reading-rate estimate (English 140 words/minute, Hindi 125), not measured audio; the word gate gives approximately 19–43 seconds English and 22–48 seconds Hindi. Exact audio timing and full production duration acceptance remain unverified without a full local phase test; no TTS is invoked.

## 8. Scientific factual validation

The production path already carries `NarrationVerifiedFact` values, realization `SpeakableFacts`, `ScientificBoundaries` and observation details through semantic projection into the prompt. The prompt now explicitly requires preservation of supplied objects, relationships and qualifications; forbids invented quantities, distances, dates and visibility; distinguishes constellation/galaxy/nebula; and requires complete sentences without filling missing evidence from model memory.

The isolated source is `Astronomy.MediaFactory.Core/Constellations/ConstellationFamily.cs`, `OrionConstellationKnowledgeProvider.GetOrion`: classification/educational statements, the apparent-versus-physical proximity qualification, and M42's emission-nebula taxonomy. The fixture checks the source taxonomy and hashes its selected evidence. It does not substitute model knowledge for catalog data or independently certify the catalog itself.

Checks reject missing evidence before inference, unsupported numerical/distance claims, missing required classifications/qualifications and selected explicit galaxy contradictions. Hindi terminology includes तारामंडल, आकाशगंगा and नीहारिका. English script validation now rejects empty or predominantly Hindi output, which previously could pass the English gate.

These are closed-corpus heuristic checks, not universal scientific entailment. They can reject valid paraphrases and cannot prove that every unsupported qualitative claim or internal sentence error is absent. Every live output still requires a scientific read against its evidence. General unsupported-claim rejection across all RC2 families remains a blocker; this pilot does not silently mark that requirement complete.

## 9. Latency

Real English/Hindi .NET latency: **NOT RUN**. The local runner records wall-clock time and structured provider events, separating failures from validated responses. User-reported Hindi timing above is contextual only. Generation speed is unrelated to spoken audio duration. Client peak working set is recorded; Ollama server/model memory remains unknown.

## 10. Azure OpenAI call count

Repository dry run: **0** Azure resolutions, **0** Ollama chat attempts, **0** blocked HTTP requests. Azure HTTP calls are zero by construction in this isolated process, which has only the guarded Ollama client and an Azure resolution trap. Mocked provider tests use fake HTTP. These results do not assert telemetry for the user's Windows API. Live counters/events must be reviewed after local execution.

## 11. Tests and build

- .NET solution build: passed, 0 errors; existing warnings remain.
- Selected regression run: **113 passed, 2 failed, 115 total** across `GroundedOllamaCertificationTests`, `OllamaNarrationPerformerTests`, `NarrationAIRegistrationTests`, `LanguageOutputValidatorTests`, and `NarrationProviderOutputTests`.
- All **17 new grounding/certification cases passed**, including mocked English/Hindi production-provider requests, evidence propagation, independent language ownership, missing evidence, JSON/schema failures, unsupported distances, Hindi classification and wrong/empty output language.
- Existing local transport/DI coverage includes optional Azure selection, no Azure credentials for Ollama, invalid models/digests, invalid JSON, cancellation, timeout, concurrency and no fallback.
- Both failures also reproduce against the unchanged pre-M1 baseline test assembly: `HindiValidator_PassesDevanagariWithApprovedEnglishTerms` and `VocabularyLedPromptContainsSafetyBoundaryAndNaturalTransition`. They remain unresolved and are not counted as passes.
- Dry run passed with independent PlanIds, expected provider/configuration and no HTTP inference. Real model tests were intentionally not run in the cloud.

Local validation artifacts are under `/workspace/.setup/logs/`: `m1-02-tests.log`, `m1-02-results/m1-02.trx`, `m1-02-baseline.log`, `m1-02-build.log`. These are workspace diagnostics, not committed certification outputs.

## 12. Remaining M1 blockers

1. Run both local .NET inference cases; retain actual observed digest, responses, latency and counters.
2. Review English/Hindi factual completeness and unsupported claims against evidence; automated term matching alone is insufficient.
3. Demonstrate full Phase 7 production authority/acceptance and duration behavior with disposable local inputs if end-to-end RC2 certification is required; this runner certifies only its isolated seam.
4. Resolve or explicitly disposition the two existing regression failures before declaring the selected suite green.

## 13. M2 TTS readiness

**Not certified ready.** Repository preparation is available, but M1 live results and scientific/phase acceptance remain pending. No M2 TTS, voice, audio, video or publishing work was started.
