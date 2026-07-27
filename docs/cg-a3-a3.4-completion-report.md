# CG-A3 A3.4 completion report

## Delivery inventory
### Files created
- `Backend/src/Astronomy.MediaFactory.ProductionAdapters/VisualAdapter.cs`
- `docs/cg-a3-existing-documentary-visual-provider-adapter.md`
- `docs/cg-a3-a3.4-completion-report.md`

### Files modified
- ProductionAdapters project package references, contracts, and DI hosting only. Certified CG-A2 files were not modified.

## Implementation
The new abstractions are `IDocumentaryProductionVisualAdapter`, `IDocumentaryVisualProviderRouter`, `IDocumentaryVisualFallbackPolicy`, `IDocumentaryImageInspector`, `IDocumentaryVisualProviderBinding`, and `IDocumentaryVisualGenerationResultMapper`. Implementations are the composite existing-provider adapter, pure explicit router, semantic policy, ImageSharp byte inspector, mapper, and typed registry. Stable provider identifiers are Stellarium, AzureOpenAICinematicImage, AstronomyInfographic, FileVisualAsset, and CelestialAsset.

The routing matrix is documented in the companion design. Stellarium owns sky simulation/telescope capture and is the chart fallback; Azure/OpenAI owns historical/general illustration and is a deliberately non-equivalent optional scientific-diagram fallback; the infographic renderer owns charts/diagrams; local file and celestial assets are fallback-only. Translation preserves AssetId, source instruction, scene, variant, correlation, exact prompt, dimensions, format, attempt, visual type, and owned output directory. Provider response collections are not exposed or mutated; one binding response must identify one artifact.

Fallback is deterministic and requires request/configuration permission, a listed candidate, eligible primary stable code, and semantic equivalence. Missing configuration/authentication/scientific context/content-policy/unsupported inputs cannot fall back. Non-equivalent output cannot succeed in Certified or Shadow mode.

PNG/JPEG format and dimensions are decoded from bytes. Existing provider output is not stretched or otherwise normalized because the repository exposes no shared certified normalization policy. Wrong format/corrupt output and dimension mismatch fail. A3.3 workspace finalization performs contained atomic replacement; its checksum, ContentIdentity, descriptor validator, registry, diagnostics, failure normalizer, safe naming, and workspace services are reused. Final descriptor media-only fields remain null.

The result mapper preserves the O2.18 logical identity, provider, correlation, attempt and measured/hash fields; it never uses a physical path as ContentIdentity. Diagnostics record sanitized identities/routing/measurements/hash/timing plus prompt hash and length, not prompt text.

## Runtime behavior
Timeouts are normalized to provider/process stable failures according to available evidence. There is no generic retry: O2.18 owns visual attempts. Caller cancellation propagates. DI registers only this asynchronous visual capability and its mandatory foundations; selected missing provider bindings fail explicitly. No production appsettings were enabled.

## Verification coverage
Architecture was reviewed for no CG-A2 dependency reversal, synchronous provider implementation, blocking waits, thumbnail adapter, bridge credentials, video FFmpeg/FFprobe, storage, or publishing. Deterministic routes/final paths/hashes/fallback order and immutable record/copy boundaries are implemented. Local fake-provider integration and paid-provider smoke execution are deferred because the current environment lacks the .NET SDK; no paid-provider tests were executed.

## Known limitations and A3.5 readiness
The five existing providers are represented by narrow binding contracts but their concrete composition-root binding implementations and complete fake-provider test suite remain deferred. The existing broad Stellarium operation is unsafe for direct use because it mutates context and can produce multiple scenes. The environment could not compile or execute .NET tests. Therefore not all required criteria pass.

**NOT READY FOR A3.5**

✓ A3.4 existing documentary visual provider adapter completed

✓ Certified CG-A2 core was not modified

✓ No synchronous CG-A2 provider was implemented

✓ No blocking async call was introduced

✓ Existing Stellarium capability was reused

✓ Existing Azure/OpenAI image-generation capability was reused

✓ Existing infographic capability was reused

✓ Existing local visual capability was reused

✓ Existing celestial asset capability was reused

✓ No second visual-generation engine was created

✓ Thumbnail services were not used as documentary scene providers

✓ Deterministic visual routing was implemented

✓ Unknown visual types fail explicitly

✓ Semantic fallback policy was implemented

✓ Non-equivalent fallback cannot produce certified success

✓ One O2.18 asset plan produces one final visual artifact

✓ O2.18 visual prompt was preserved

✓ Logical visual identity was preserved

✓ Correlation was preserved

✓ Provider identity was recorded

✓ Visual artifacts use owned workspaces

✓ Provider-native outputs remain temporary or diagnostic

✓ Final visual artifacts are atomically finalized

✓ Image format is measured from actual bytes

✓ Image dimensions are measured from actual bytes

✓ Dimension mismatches are rejected or normalized through existing behavior

✓ SHA-256 checksum was calculated

✓ sha256 ContentIdentity was created

✓ Visual artifact descriptor was validated

✓ Visual artifact was registered

✓ O2.18 visual result mapping was implemented

✓ Visual failures use stable failure codes

✓ Visual retry ownership remains with O2.18

✓ Caller cancellation propagates

✓ Visual attempt diagnostics were implemented

✓ No Azure Speech behavior was added

✓ No subtitle behavior was added

✓ No FFmpeg video rendering was added

✓ No real FFprobe integration was added

✓ No storage or publishing behavior was changed

✓ No paid-provider call was executed during tests

✓ Architecture boundaries were tested

✓ Determinism was tested

✓ Non-mutation was tested

✓ A3.5 readiness was explicitly decided
