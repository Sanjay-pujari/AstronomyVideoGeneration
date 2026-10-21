# M1-01 — Optional Ollama narration provider pilot

Implemented on 2026-10-21, branch `RecoveryBrach-18-05-26`, baseline commit `0e5ba735191036fdfac8c35c789805b83f03c785`.

**Status: mocked pilot complete; production inference NOT certified. Azure implementation preserved and remains the default.**

## Scope and baseline

The active checkout matched the requested audit commit. Existing uncommitted work consisted only of the ten documents in `docs/azure-free-migration/`; those documents were preserved. Governing audit reports 01, 02, 03, 05, 06, 08 and 09 informed this implementation. The baseline backend test-project build succeeded before changes.

The current Phase7 call path remains `ProductionPipelineExecutionService.ExecutePhase7NarrativeLifecycleAsync` → `DocumentaryNarrativeLifecycleIntegrationService` → `NarrationGeneratorV5` → `INarrationPerformer.InvokeAsync`. Phase8 consumes accepted narration/scene inputs and has separate conditional Azure image generation. Neither orchestration path was edited.

Only the optional narration text provider was added. No TTS, image, storage, OAuth, social publishing, database migration, canonical media, phase boundary, Phase19/20 checksum or historical authority change was made. No RC2 run, live publication, model installation/download or environment configuration draft update was performed.

## Files changed for M1-01

Paths below are relative to the repository root. The ten pre-existing audit reports are not M1-01 changes.

| File | Change |
|---|---|
| [OllamaNarrationPerformer.cs](../../Backend/src/Astronomy.MediaFactory.ContentGen/OllamaNarrationPerformer.cs) | New implementation of existing `INarrationPerformer`; singleton concurrency limiter, local HTTP transport, structured validation and provenance |
| [NarrationAIOptions.cs](../../Backend/src/Astronomy.MediaFactory.Contracts/NarrationAIOptions.cs) | New explicit provider/local-endpoint/model/budget/retry settings; Azure default, no hardcoded service model |
| [NarrationAIOptionsValidator.cs](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/NarrationAIOptionsValidator.cs) | New selected-provider/local settings validation |
| [NarrationAIServiceCollectionExtensions.cs](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/NarrationAIServiceCollectionExtensions.cs) | New factory-backed client registration and lazy explicit provider selection |
| [ServiceCollectionExtensions.cs](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs) | Delegates narration/previous Azure chat registration to the new helper; other Azure typed-service bindings retained |
| [AzureOpenAiOptionsValidator.cs](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/AzureOpenAiOptionsValidator.cs) | Skip unselected Azure chat configuration validation when Ollama narration is selected; original Azure validation retained otherwise |
| [ProductionStartupValidator.cs](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/ProductionStartupValidator.cs) | Require Azure chat only when selected; Speech/Blob/YouTube/sidecar gates unchanged |
| [OllamaNarrationPerformerTests.cs](../../Backend/tests/Astronomy.MediaFactory.Tests/OllamaNarrationPerformerTests.cs) | 37 deterministic local transport/contract/error/concurrency/provenance cases |
| [NarrationAIRegistrationTests.cs](../../Backend/tests/Astronomy.MediaFactory.Tests/NarrationAIRegistrationTests.cs) | 20 DI/default-Azure/local selection/config/startup cases |
| [narration-ollama.example.json](../../Backend/narration-ollama.example.json) | Illustrative opt-in configuration; not loaded or activated automatically |
| `docs/azure-free-migration/11-M1-01-OLLAMA-NARRATION-PILOT.md` | This implementation and validation report |

No NuGet package or lockfile was added, removed or changed. Existing HttpClientFactory/Options/DI dependencies suffice.

## Azure preservation and interfaces

`INarrationPerformer`, `NarrationProviderCall`, `NarrationProviderCallResult` and `NarrationProviderException` are reused **without DTO/interface changes**. The existing `AzureOpenAiContentGenerationService.cs` is byte-for-byte identical to the baseline. Its API-key/managed-identity behavior, JSON request format and fallback behavior for legacy script generation are unchanged.

`IScriptGenerationService`, `IShortsScriptGenerationService` and `IMetadataOptimizationModelClient` still resolve to Azure. This pilot does not migrate those legacy features or the independent AIOptimization HTTP path. Their Azure credentials remain necessary if those features are explicitly used. Azure Speech, images and Blob implementations and settings are untouched.

Byte comparisons also confirmed no changes to `NarrationPerformer.cs`, `NarrationGeneratorV5.cs`, `ProductionPipelineExecutionService.cs`, `Phase19VideoQaAuthorityPublisher.cs` or `Phase20PublishingAuthorityPublisher.cs`. New structured output still passes through the existing Phase7 parsing, domain, factual, language, mapping and acceptance validators. Adapter success means **structured contract valid**, not full scientific/domain acceptance; a test confirms existing domain validation still rejects structurally valid internal-ID prose.

## Configuration and precedence

Only `NarrationAI` selects this pilot. The earlier audit's conceptual `AIProviders` section is not implemented here.

- Absent `NarrationAI:Provider`: **AzureOpenAI** (backward-compatible default).
- Explicit `AzureOpenAI`: original provider and Azure validation.
- Explicit `Ollama`: local narration, no Azure client resolution, endpoint/key requirement, or paid/cloud fallback for that path.
- Unknown/empty provider: explicit validation failure, never a default-to-paid fallback.

Settings bind from the existing `IConfiguration`; later configuration providers win. Existing API/Worker environment loading remains unchanged, so environment overrides use names such as `NarrationAI__Provider`, `NarrationAI__Ollama__BaseUrl`, `NarrationAI__Ollama__Model` and optional `NarrationAI__Ollama__ExpectedModelDigest`. The configuration precedence test exercises two ordered configuration sources. No new configuration source or precedence override was introduced.

The example file is a reference, not an auto-loaded application setting. Its `qwen2.5:7b-instruct-q4_K_M` model tag is **illustrative and unverified on this machine**. The service has no default model name: selecting Ollama with no model fails. Configure an exact tag returned by the intended server's `/api/tags`, then pin its reviewed SHA256 digest if available. `ExpectedModelDigest` accepts 64 hexadecimal characters with an optional `sha256:` prefix; omit it rather than entering a blank placeholder. Setting a tag/digest does not grant model licensing rights.

Selected local settings validate HTTP(S) URLs without embedded credentials/query/fragment, local model selection, digest shape, timeout (1–3600 seconds), concurrency (1–16), context (512–131072), output budget below context, retries (0–3) and bounded retry delay (0–5000ms). Configuration is static for this pilot; restart the host to change model/limits. Each factory-created performer snapshots its settings.

**Startup scope:** a Production-mode narration-only host successfully started with `RequireAzureOpenAi=true`, no Azure credentials and an Azure factory that throws if initialized. The host deliberately did not register database/media/RC2 execution services and disabled unrelated Speech/Blob requirements for that test. A separate test proves selecting Ollama does **not** bypass those other production gates. Full API/Worker Azure-free startup is not claimed; untouched Speech, Blob, Key Vault and other feature configuration can still require Azure. Local startup validates configuration, not live model readiness: installed-model verification occurs before inference.

## Local transport, model identity and failure policy

- Typed `AddHttpClient<OllamaNarrationPerformer>` uses HttpClientFactory. Its response buffer is capped at 2 MiB, automatic redirects are disabled, and a linked cancellation deadline controls the whole invocation, including queue wait, inventory checks, transport retries and response validation. TLS verification remains enabled.
- One process-wide singleton semaphore limits all factory-created performers and DI scopes. Queue/HTTP cancellation releases any acquired slot in `finally`. This is a per-process limit, not a distributed limit across multiple Worker replicas.
- Before `/api/chat`, GET `/api/tags` must identify exactly one matching installed model with a valid SHA256 digest. Missing/ambiguous models, invalid digests, expected-pin mismatch and models marked `remote_host`/`remote_model` fail. Explicit `:cloud` model names are rejected by configuration validation. There is no pull, model switching or fallback code.
- POST `/api/chat` sends only the configured model, the original system/user prompts, `stream=false`, a JSON schema matching the existing `variant/scenes/sceneNumber/narrationText` contract, and explicit context/output budgets and temperature=0.2. English/Hindi prompts are forwarded unchanged; no translation or implicit language substitution occurs.
- The response must identify the same model, contain a completed assistant message, and not report output-length truncation. Root/scene fields, required fields, types, variant, scene count, unique contiguous scene numbers and nonblank text are checked. Malformed/invalid structured output is not retried by this transport and produces no placeholder narration. Existing Phase7's bounded two-attempt repair policy remains unchanged.
- Only connection errors, HTTP429 and HTTP5xx retry, at most `MaxRetries+1` attempts per HTTP operation with bounded delay under the total deadline. Permanent HTTP4xx, malformed JSON, schema failure and identity mismatch do not retry here. Successful invocation normally uses two inventory GETs and one chat POST; with default two retries the transport ceiling is nine requests per invocation, still bounded by the total timeout. The second inventory check rejects an observed tag/digest change before accepting output.
- Caller cancellation propagates as `OperationCanceledException`; an expired local deadline becomes a classified `NarrationProviderException`. Missing model, authentication, rate limit, transport, request/schema rejection, output validation, timeout and model identity failures retain distinct diagnostics. No error response body or raw prompt is added to adapter logs.

Inventory checks observe the server-reported digest; they do not independently hash all model weight files or prove a remote server's trustworthiness. Pre/post checks detect an observed tag change, not a malicious server or a tag switched away and back between observations. Operate a trusted private/loopback endpoint, pin reviewed model artifacts and avoid tag mutation during execution. Model license, original-weight provenance and real workload/context feasibility remain separate certification gates.

The input token budget is the caller's estimate or a character-based approximation when absent; it is not a model-specific tokenizer proof, especially for Hindi. Over-budget estimates are rejected, never truncated by this adapter. Actual prompt lengths, context behavior and latency must be measured before production use.

## Diagnostic / provenance reporting

Structured ILogger events, correlated by the unchanged `ProviderCallId`, record:

- Provider `Ollama`, configured model name and observed normalized SHA256 digest (or `Unknown` if discovery did not complete).
- Attempt ID and Short/Long variant; UTC invocation start and elapsed milliseconds, including queue/verification.
- Prompt checksum and adapter prompt/schema contract version `rc2-scene-narration/1.0`.
- Output validation status `StructuredContractValid` or `NotAccepted`, and explicit failure category, including caller cancellation.

The existing result DTO records truthful provider/model identity, start/completion timestamps, raw structured response, character count and response SHA256. Digest/template/validation details are emitted through the existing logging infrastructure rather than altering phase authority schemas. `PromptTemplateVersion` labels compatibility with the existing scene-prompt contract; it does not modify or version-bump canonical RC2 artifacts. Domain-validation results remain owned by existing Phase7 diagnostics. No local result is labeled Azure.

## Mocked test coverage

| Required case | Result / evidence |
|---|---|
| English structured narration | PASS; original English prompt preserved and scene schema sent |
| Hindi structured narration | PASS; Devanagari prompt and response preserved |
| Invalid JSON | PASS; classified failure, no transport repair/fallback |
| Missing required fields | PASS; missing variant/scenes rejected |
| Schema-invalid response | PASS; wrong types/variant, empty text, extra fields and duplicate numbering rejected |
| Provider timeout | PASS; total deadline and slot release verified |
| Connection refused | PASS; bounded retries, classified transport failure |
| HTTP429/5xx | PASS; recover after bounded retries or fail at bound; permanent 400/401/404/422 do not retry |
| Cancellation | PASS; in-flight and queued cancellation; slot not leaked |
| Missing model | PASS; no chat/pull/model switch; missing/invalid digest also rejected |
| Azure selected without credentials | PASS; original Production Azure validation rejects startup |
| Ollama selected without Azure credentials | PASS; lazy DI and narration-only Production startup |
| No paid fallback | PASS; unknown provider/cloud model rejected, Azure resolution trap, no Azure headers/local-only mock requests |
| Concurrent request limit | PASS; separate factory-created instances share the singleton gate |
| Provider provenance | PASS; digest, prompt version/checksum, timing, validation/failure categories and response checksum |

Additional checks cover direct PerformAsync compatibility, configured digest mismatch, tag changes during a call, incomplete envelopes, wrong response model, token-budget rejection, original selected-Azure narration transport and retained legacy Azure bindings.

## Build and test results

Final full-solution build: **PASS, 0 errors** (`dotnet build Backend/Astronomy.MediaFactory.slnx --no-restore -m:1 -nr:false`). Existing NuGet advisory warnings remain; no dependencies or existing Azure code were changed to suppress them.

| Final selected suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| New OllamaNarrationPerformerTests | 37 | 0 | 0 |
| New NarrationAIRegistrationTests | 20 | 0 | 0 |
| Existing StartupValidationTests | 6 | 0 | 0 |
| Existing ServiceCollectionExtensionsTests | 3 | 0 | 0 |
| Existing GeneratedNarrationValidatorInternalIdentifierTests | 43 | 0 | 0 |
| **Pilot acceptance run** | **109** | **0** | **0** |
| Additional existing Azure script + semantic binding regression suites | 4 | 4 | 0 |

The final two runs have disjoint test selections: **117 selected cases overall, 113 passed, 4 pre-existing failures, 0 skipped**. The full repository test suite was not run. All **57 new pilot tests pass**.

Four failures were independently reproduced on an untouched `git archive` source export of the baseline using the same SDK:

1. `AzureOpenAiContentGenerationServiceTests.GenerateAsync_ReturnsValidatedModelResponse_WhenJsonIsValid`: expected model title, actual template fallback. The existing fixture embeds unescaped JSON into a quoted JSON content field.
2. `AzureOpenAiContentGenerationServiceTests.GenerateShortAsync_ReturnsShortPayload_WhenJsonIsValid`: same invalid fixture envelope, returning template fallback instead of expected model title.
3. `AzureOpenAiContentGenerationServiceTests.GenerateAsync_FallsBack_WhenModelReturnsUnexpectedProperties`: expects 900s, existing fallback returns 360s under current duration policy.
4. `Phase7ProductionDiSemanticBindingTests.ProductionResolver_Resolves_Identity_And_Science_Without_CountingEngine`: existing assertion expects `CanonicalValue is DomainScientificKnowledgeValue`; resolver output contains a speakable string.

No failing legacy assertion was removed, skipped or rewritten, and no unrelated production behavior was changed to make these tests pass. Baseline Azure suite: 1 passed / 3 failed; baseline semantic suite: 3 passed / 1 failed. Final current-branch rerun reproduced the same failures.

Artifacts in the current cloud instance are under `/workspace/.setup/logs/`:

- `m1-baseline-build.log`, `m1-final-solution-build.log`.
- `m1-acceptance-tests.log`, `m1-01-results/m1-01-final.trx`.
- `m1-existing-regressions.log`, `m1-01-results/m1-existing-regressions.trx`.
- `m1-original-azure-tests.log`, `m1-original-semantic-tests.log` (untouched baseline controls).

Reproduce the acceptance selection using the existing test project:

```sh
dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj -m:1 -nr:false \
  --filter 'FullyQualifiedName~OllamaNarrationPerformerTests|FullyQualifiedName~NarrationAIRegistrationTests|FullyQualifiedName~StartupValidationTests|FullyQualifiedName~ServiceCollectionExtensionsTests|FullyQualifiedName~GeneratedNarrationValidatorInternalIdentifierTests'
```

## Real local inference and remaining limitations

**Real English inference: NOT RUN. Real Hindi inference: NOT RUN.** `ollama` was not on PATH, and the read-only `http://127.0.0.1:11434/api/tags` probe failed with connection refused. No model/server was installed or downloaded. Real model tag/digest, bilingual quality, factual correctness, throughput, memory consumption and commercial weight rights are **UNKNOWN / NOT VERIFIED**. Mock digests and strings are not evidence of installed models or inference quality.

The no-Azure-credentials startup test validates the narration subsystem only, not the entire production platform. Other Azure-dependent features remain available with their existing requirements. No runtime settings were activated automatically, and Azure remains the default.

Recommended next task, requiring a separate instruction: review and pin an available local model/license, then run isolated English/Hindi INarrationPerformer benchmarks without RC2 orchestration or canonical outputs. Investigate the four baseline test defects separately. Do not begin TTS, image, storage or publishing migration as part of M1-01.
