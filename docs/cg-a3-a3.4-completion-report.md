# CG-A3 A3.4 closure completion report

## Delivered closure

Five scoped asynchronous bindings now connect the bridge to existing production capabilities:

| Provider ID | Binding | Existing operation |
|---|---|---|
| `Stellarium` | `StellariumDocumentaryVisualProviderBinding` | `StellariumVisualGenerationService.GenerateSingleVisualAsync` (which delegates to the existing `PrepareVisualsAsync` script/capture path) |
| `AzureOpenAICinematicImage` | `AzureOpenAICinematicDocumentaryVisualProviderBinding` | `IAICinematicImageGenerator.GenerateAsync`, implemented by `AzureOpenAICinematicImageGenerator` |
| `AstronomyInfographic` | `AstronomyInfographicDocumentaryVisualProviderBinding` | `IAstronomyInfographicRenderer.RenderAsync`, implemented by `AstronomyInfographicRenderer` |
| `FileVisualAsset` | `FileVisualAssetDocumentaryVisualProviderBinding` | `FileVisualAssetProvider.SelectExistingAssetAsync` |
| `CelestialAsset` | `CelestialAssetDocumentaryVisualProviderBinding` | `ICelestialAssetProvider.GetAssetAsync`, implemented by `CelestialAssetProvider` |

The narrow Stellarium operation builds an isolated one-subject legacy context and then uses the same scene composer, script builder, process execution, capture resolution, timeout configuration, and logging as the existing caller. Selection requires one scene-ID match; the sole result is accepted only when the existing collection contract is unambiguous. It does not mutate a caller-owned astronomy context.

Every external/cache result is copied with asynchronous streams and a durable flush to a deterministic provider-native name below the attempt directory. Bindings neither retry, finalize, hash, register, route fallback, nor map O2.18 results. Caller cancellation is rethrown. Provider failures use stable categories and do not expose credentials or raw provider payloads.

## Composition and hardening

`AddMediaFactory` invokes `AddDocumentaryProductionBridge` and registers exactly one binding for every stable provider ID after the concrete provider dependencies are registered. The adapter constructor enumerates registrations once and rejects duplicate IDs in sorted deterministic order rather than silently overwriting them.

`DocumentaryPathComparison` supplies OS-native casing (`OrdinalIgnoreCase` on Windows, `Ordinal` elsewhere) and a separator boundary. The adapter, workspace manager, and binding copy boundary share it. Its comparison overload permits Windows casing and `workspace` versus `workspace-other` behavior to be tested on every OS.

## Verification

Existing adapter tests cover routing, fallback eligibility and semantic equivalence, response invariants, and mapper behavior. New path tests cover native owned children, Windows-style case folding, and sibling-prefix rejection. Both the adapter and Infrastructure projects build successfully with .NET 10 (with pre-existing warnings). The test assembly builds, but this container's `dotnet test` runner rejects the produced test assembly argument, so the new tests could not execute here. The real-byte end-to-end test suite requested by the closure was **not completed in this change**. No Stellarium process, Azure/OpenAI request, other paid-provider request, Azure Speech call, FFmpeg render, or FFprobe call was executed.

## Known limitations and A3.5 readiness

Fake-service unit coverage for all provider failure categories and the complete real-infrastructure adapter matrix remains outstanding. Real providers have not been smoke-tested. Because all required tests have not run and the complete integration suite is absent, this report cannot truthfully assert closure readiness.

**NOT READY FOR A3.5**

✓ Certified CG-A2 core was not modified

✓ All five existing visual capabilities are concretely bound

✓ Stellarium binding reuses the existing script and capture engine

✓ Azure/OpenAI binding reuses the existing image generator

✓ Infographic binding reuses the existing renderer

✓ Local binding reuses the existing file provider

✓ Celestial binding reuses the existing celestial provider

✓ No provider algorithm was duplicated

✓ No synchronous CG-A2 visual provider was implemented

✓ No blocking async call was introduced

✓ Provider IDs are unique and duplicate registration is rejected

✓ Provider requests preserve logical identity and prompt

✓ Each successful provider operation returns one owned artifact

✓ Deterministic provider-native filenames are used

✓ Cross-platform path comparison was hardened

✓ Sky simulation cannot silently degrade to illustration

✓ Non-equivalent fallback cannot succeed

✓ Caller cancellation propagates

✓ Failed artifacts are not registered by the adapter

✓ Visual diagnostics remain sanitized

✓ No Azure Speech, subtitle, FFmpeg video, real FFprobe, storage, or publishing behavior was added

✓ No paid-provider test was executed

✗ Complete fake-provider integration tests and an all-tests-passed result remain required

✓ A3.5 readiness was explicitly decided
