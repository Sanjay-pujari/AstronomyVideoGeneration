# CG-A3 A3.4 — Existing documentary visual provider adapter

## Scope and boundaries
A3.4 adds the cancellation-aware asynchronous visual capability only. It does not implement the CG-A2 synchronous provider, narration, subtitles, video composition, FFprobe, storage, publishing, or the production host. CG-A2 contracts are consumed unchanged. Thumbnail services are not documentary scene providers.

## Existing capabilities and bindings
The five concrete bindings are `StellariumDocumentaryVisualProviderBinding`, `AzureOpenAICinematicDocumentaryVisualProviderBinding`, `AstronomyInfographicDocumentaryVisualProviderBinding`, `FileVisualAssetDocumentaryVisualProviderBinding`, and `CelestialAssetDocumentaryVisualProviderBinding`. They call, respectively, `StellariumVisualGenerationService.GenerateSingleVisualAsync`, `IAICinematicImageGenerator.GenerateAsync`, `IAstronomyInfographicRenderer.RenderAsync`, `FileVisualAssetProvider.SelectExistingAssetAsync`, and `ICelestialAssetProvider.GetAssetAsync`. They do not recreate provider algorithms. Bindings receive an immutable translation containing logical IDs, the unmodified English prompt, dimensions, format, attempt, scene/correlation identity, and an owned output directory. No paid-provider test was executed.

`GenerateSingleVisualAsync` is the narrow operation added to the existing Stellarium rendering layer. It creates an isolated single-subject context and delegates to the existing `PrepareVisualsAsync` path, retaining its script builder, scene/camera composition, process runner, configured directories/timeouts, capture resolver, and logging. It selects exactly one capture by scene ID; it accepts a sole collection member only because that result is unambiguous and rejects zero or multiple matches.

## Deterministic routing matrix
| Asset type | Primary | Ordered fallback |
|---|---|---|
| SkySimulationImage | Stellarium | none |
| TelescopeViewImage | Stellarium | CelestialAsset, only when representative fallback is enabled |
| StarChartImage | AstronomyInfographic | Stellarium |
| ScientificDiagramImage | AstronomyInfographic | AzureOpenAICinematicImage, only when enabled and never certification-equivalent |
| HistoricalIllustrationImage | AzureOpenAICinematicImage | FileVisualAsset |
| VisualImage | AzureOpenAICinematicImage | FileVisualAsset, CelestialAsset |

Provider IDs are stable constants: `Stellarium`, `AzureOpenAICinematicImage`, `AstronomyInfographic`, `FileVisualAsset`, and `CelestialAsset`. Unknown and nonvisual values fail explicitly. SkySimulation does not silently degrade to generated illustration. TelescopeView representative fallback is explicitly labelled.

## Semantic fallback
Fallback requires global permission, route permission, an eligible stable primary failure, and semantic equivalence. Availability, timeout/rate-limit, process failure, invalid response/output, and dimension failures are eligible. Configuration, authentication, policy rejection, unsupported requests, and missing scientific inputs are ineligible. A generated scientific diagram is non-equivalent, so neither Shadow nor Certified mode reports it as successful/certified. Shadow may be extended to retain such provider output diagnostically, but this implementation does not invoke it. There is no adapter retry; O2.18 `MaximumVisualAttempts` owns retries.

## Workspace and artifact flow
Each provider invocation is one semantic operation and writes below its deterministic attempt `tmp`. External provider/cache paths are asynchronously copied and flushed to `provider-stellarium`, `provider-azure-openai`, `provider-infographic`, `provider-local`, or `provider-celestial` with the requested extension. Escaping output paths are rejected. Exactly one path must be returned; missing/ambiguous responses fail. The image inspector decodes actual bytes with ImageSharp and accepts PNG/JPEG only. Native output must already match requested format and dimensions: no new crop, stretch, letterbox, fit, fill, or canvas policy was invented. Mismatch fails as `DimensionMismatch`.

Containment uses the shared `DocumentaryPathComparison`: ordinal-ignore-case on Windows and ordinal elsewhere, always with a normalized trailing separator so a `workspace-other` sibling cannot match `workspace`. The adapter, workspace finalization, and binding ownership checks use the same rule.

The A3.3 workspace manager atomically finalizes the one image to the deterministic scene visual path. The physical inspector computes SHA-256 through the A3.3 checksum service and creates `sha256:` ContentIdentity. The adapter adds measured dimensions, validates the descriptor, and registers it idempotently. Media-only descriptor fields remain null. One O2.18 asset plan produces one final visual artifact.

## Result mapping, failure, diagnostics
The focused mapper preserves AssetId, asset type/format, correlation, provider, attempt, ContentIdentity, byte length, measured dimensions, and checksum; failed mapping preserves the plan and correlation. Stable failures include adapter/configuration/provider/auth/rate-limit/timeout/policy/invalid-response, dependency/process, missing/empty/invalid output, dimension, checksum, source, and filesystem codes.

One sanitized diagnostic is written for successful attempts with execution/logical identity, routing and fallback, requested/measured image properties, final owned path, hash/identity, duration, outcome, prompt SHA-256 and prompt length. Full prompts and credentials are not logged. Caller cancellation propagates as `OperationCanceledException`; provider transport/capture timeouts are normalized without adding retries.

## DI and tests
`AddDocumentaryProductionBridge` registers visual options, pure router, fallback policy, real-byte inspector, asynchronous adapter, typed registry, and result mapper. `AddMediaFactory` invokes it and registers the five bindings after their existing concrete services. Adapter construction rejects duplicate provider IDs deterministically.

Tests cover the A3.3 foundation, router and fallback rules, mapper invariants, and cross-platform path comparison. External Stellarium/Azure calls are deliberately excluded. Complete fake-provider full-flow coverage remains outstanding and is recorded in the completion report.

## Known limitations
Real-provider smoke tests are deferred. Complete binding fake-service and full-adapter integration coverage is also outstanding. No normalization occurs because no existing universal still-image composition policy can be safely inferred.
