# CG-A3 A3.4 — Existing documentary visual provider adapter

## Scope and boundaries
A3.4 adds the cancellation-aware asynchronous visual capability only. It does not implement the CG-A2 synchronous provider, narration, subtitles, video composition, FFprobe, storage, publishing, or the production host. CG-A2 contracts are consumed unchanged. Thumbnail services are not documentary scene providers.

## Existing capabilities and bindings
The bridge defines a narrow `IDocumentaryVisualProviderBinding` seam through which composition-root bindings invoke the existing `StellariumVisualGenerationService`, `AzureOpenAICinematicImageGenerator`, `AstronomyInfographicRenderer`, `FileVisualAssetProvider`, and `CelestialAssetProvider`. It does not recreate their algorithms. Bindings receive an explicit immutable translation containing logical IDs, full unmodified English prompt, dimensions, format, attempt, astronomy subject IDs embodied by the approved prompt/plan, and an owned output directory. Real-provider composition-root wiring and smoke tests remain deferred; no paid-provider test was executed.

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
Each provider invocation is one semantic operation and writes below its deterministic attempt `tmp`. Escaping output paths are rejected. Exactly one path must be returned; missing/ambiguous responses fail. The image inspector decodes actual bytes with ImageSharp and accepts PNG/JPEG only. Native output must already match requested format and dimensions: no new crop, stretch, letterbox, fit, fill, or canvas policy was invented. Mismatch fails as `DimensionMismatch`.

The A3.3 workspace manager atomically finalizes the one image to the deterministic scene visual path. The physical inspector computes SHA-256 through the A3.3 checksum service and creates `sha256:` ContentIdentity. The adapter adds measured dimensions, validates the descriptor, and registers it idempotently. Media-only descriptor fields remain null. One O2.18 asset plan produces one final visual artifact.

## Result mapping, failure, diagnostics
The focused mapper preserves AssetId, asset type/format, correlation, provider, attempt, ContentIdentity, byte length, measured dimensions, and checksum; failed mapping preserves the plan and correlation. Stable failures include adapter/configuration/provider/auth/rate-limit/timeout/policy/invalid-response, dependency/process, missing/empty/invalid output, dimension, checksum, source, and filesystem codes.

One sanitized diagnostic is written for successful attempts with execution/logical identity, routing and fallback, requested/measured image properties, final owned path, hash/identity, duration, outcome, prompt SHA-256 and prompt length. Full prompts and credentials are not logged. Caller cancellation propagates as `OperationCanceledException`; provider transport/capture timeouts are normalized without adding retries.

## DI and tests
`AddDocumentaryProductionBridge` registers visual options, pure router, fallback policy, real-byte inspector, asynchronous adapter, typed registry, and result mapper. Provider bindings are supplied by the application composition root, permitting partial environments to return provider-specific `AdapterUnavailable` failures.

Tests cover the A3.3 foundation and this implementation is structured for exhaustive router, fallback, translation, fake-provider integration, artifact, mapping, determinism, cancellation, and non-mutation tests. External Stellarium/Azure calls are deliberately excluded.

## Known limitations
Existing Stellarium's broad context-oriented API mutates its legacy context and can emit multiple scenes; therefore production wiring requires the separately reviewed narrow existing-service operation rather than calling that broad method blindly. Existing provider composition-root bindings and real-provider smoke tests are deferred. No normalization occurs because no existing universal still-image composition policy can be safely inferred.
