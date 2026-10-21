# Staged migration implementation plan

Audit date: 2026-10-21. Branch: `RecoveryBrach-18-05-26`. Source commit: `0e5ba735191036fdfac8c35c789805b83f03c785`. Audit only; no migration, model installation, generation, publication, or certification run was performed.

## Scope and stop condition

This is a staged implementation proposal only. Do not execute it as part of this audit. Preserve the current architecture and historical media; no database migration or published-record rewrite is planned merely to change inference/storage providers. An approved implementation request must define the canary scope before any phase generation or publishing regression.

## Exact first implementation task

**Implement and test a provider-selected local narration adapter for `INarrationPerformer`, without changing phase orchestration or producing canonical media.** Reuse NarrationProviderCall/Result and classified failure metadata. Add pinned-model/endpoint/context/timeout/concurrency configuration; route local selection to Ollama's supported API rather than Azure deployment URLs. Add focused mocked-transport tests covering valid/malformed JSON, schema/semantic rejection, missing model, deadline/cancellation, language/variant preservation, prompt checksum and truthful invocation diagnostics. Make Azure option/startup validation conditional on the selected text provider while retaining equivalent required-local-provider validation. Verify no Azure HTTP request in local mode and keep paid fallback forbidden. Keep all other capabilities unchanged.

Prerequisites before merge/activation: reviewed Qwen model/quantization provenance/license and a measured prompt-budget corpus. The implementation can begin with HTTP fakes while those materials are gathered; production local generation cannot be approved without them. M0 evidence remains incomplete, so this first task is **proposed, not authorized or started**.

Files expected to change in that separate task: `Backend/src/Astronomy.MediaFactory.ContentGen/NarrationPerformer.cs` only if the current contract needs a minimal compatible extension; new local implementation in the same project; `Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs`; configuration options/validation classes; focused tests. Do not edit RC2 phase owners or artifact contracts just to route text.

## Staged plan and exit gates

| Stage | Work and affected evidence | Exit / stop gate | Effort estimate |
|---|---|---|---|
| M0 Discovery and baseline | Inventory 01/phase matrix 02; authorized read-only Azure English/Hindi audio + scripts; exact model/voice/image/aligner licenses; source corpus; resource benchmark plan; copy/hash historical approvals/media for read-only checks | No provider approval until baseline provenance, commercial-use terms and test corpus are available. Do not regenerate certified history to obtain a baseline. | 3–5 engineer-days plus outside evidence wait |
| M1 Text AI | Existing INarrationPerformer/typed script/metadata interfaces; separate AIOptimization HTTP path; selected-provider guards; local model pilot | Valid structured English/Hindi narration passes existing semantic/acceptance gates, model/parameters logged, zero Azure egress, bounded resources; legacy deterministic fallback behavior preserved only where already allowed. | 5–8 days |
| M5 Storage and public staging (move earlier) | Filesystem archive + IPublicMediaStorageService HTTPS staging; DI/RC2 Azure-only gates; signed URL renewal, retention and uncertainty handling | Externally verified TLS/media fetch and expiry/renewal tests; originals/checkpoints/OAuth unaffected; no production post created. Public origin is a concrete prerequisite independent of AI quality. | 4–7 days |
| M2 English/Hindi TTS | Reuse existing speech/weekly/bulk/documentary abstractions; neutral Phase15 scene boundary; voice/rate/pause mapping, codecs and provenance | Paired Azure listening comparison, reviewed voice rights, exact physical audio checks and Phase14/15 authority tests. No silent voice-language substitution. | 8–12 days + bilingual listening |
| M3 Transcription / SRT (conditional) | Keep Phase16 weighted timing as baseline; add local transcription QA and/or forced alignment only if measured subtitle failures require it | Approved text reconstructed exactly; no duplicate SRT authority/burn-in; aligner licenses and Hindi coverage verified. If current timing passes, no new aligner dependency is required. | 2–4 days if needed |
| M4 Images | Shared local image boundary for D03–D06; purpose-specific Phase12/13 independent backgrounds; scientific imagery and overlays retained | Reviewed model rights and GPU/CPU feasibility; three thumbnail aspects and six gallery roles pass current guards, no fabricated astronomy or fallback masquerading as success. | 5–10 days |
| M6 End-to-end RC2 | Fresh isolated test PlanId only; Phase1–20 in en/hi, requested-output variants, failure/resume/reuse/overwrite checks; Phase19/20 checksum verification | New outputs pass existing certification with truthful provenance; historical artifact read/approval checksums unchanged; selected mode works with all Azure credentials unavailable. | 5–8 days |
| M7 Publishing regression | YouTubeLong/Short, InstagramPost, Facebook photo and legacy reel/thumbnail scope; mock/sandbox transport first; expiry, uncertain-response, crash/resume and token refresh tests | Existing approval/package identity and remote IDs preserved; no duplicate posts/uploads. Real non-public test publication only under separately explicit authorization if mock/provider sandbox cannot establish required behavior. | 3–5 days |

Totals: approximately 35–59 engineer-days (7–12 weeks), plus outside baseline/licensing/hardware/hosting decisions. This is not a delivery promise. M5 is deliberately earlier than the suggested original order because public-media availability can block publishing independently of local AI.

## Cross-cutting changes required

- DI and conditional readiness: stop requiring unused Azure credentials when local providers are selected; keep strict local model/storage health and explicit failure. ProductionStartupValidator, AzureConfigurationValidation, PublicMediaStorage validators and OperationsConfigHealthCheck all matter.
- Replace all direct client boundaries, not just interface registration. AIOptimization, Gallery, legacy Hero, Visual Lab and weekly TTS have separate implementations. See D01–D13.
- Keep Phase15 scene IDs, text checksums and Phase14 lineage while introducing truthful local model/voice/codec policy identities for new artifacts. Do not reuse Azure-labeled request hashes for a different voice/model.
- Do not rewrite accepted knowledge/narration, certified imagery or Phase19/20 packages. Preserve version-compatible readers and only certify fresh test artifacts under new policy versions.
- Public staging must be durable, public over HTTPS and secure; raw local path/localhost is not a replacement for Meta fetch. Preserve provider derivatives, refreshable object keys, checkpoints and remote uncertainty handling.
- No embedding/database schema change is justified by this discovery. Do not turn a provider migration into a retrieval or orchestration rewrite.

## Rollback and release policy

Every canary is new PlanId/media with explicit provider selection. Before rollout, store config/model digests and verified copies of historical package/approval/publication metadata. If a local provider fails, stop new generation or select another **already certified local** model; Azure subscription recovery is not an assumed rollback. Continue reading old approved artifacts without regenerating them. Never retry an uncertain remote write by creating a new publication record; reconcile existing remote identity/checkpoints first. Independent storage URL renewal must not reset remote publication state.

## Evidence and authority owners

- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs): Provider registration and selected capability guards.
- [`Backend/src/Astronomy.MediaFactory.ContentGen/NarrationPerformer.cs`](../../Backend/src/Astronomy.MediaFactory.ContentGen/NarrationPerformer.cs): Exact first-task interface.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Optimization/AIOptimizationService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Optimization/AIOptimizationService.cs): Independent chat HTTP client.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs): Phase15 provider coupling and canonical dispatch.
- [`Backend/src/Astronomy.MediaFactory.Rendering/WeeklySkyForecastAzureTtsSynthesizer.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/WeeklySkyForecastAzureTtsSynthesizer.cs): Independent weekly speech client.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/MatureThumbnailCandidatePublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/MatureThumbnailCandidatePublisher.cs): Required image semantics, not generic scene reuse.
- [`Backend/src/Astronomy.MediaFactory.Rendering/Phase13GalleryAuthority.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/Phase13GalleryAuthority.cs): Gallery background generation/authority.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2PublishingExecutionService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2PublishingExecutionService.cs): Storage gates, checkpoint/approval and remote reconciliation.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Configurations/Rc2PublishingApprovalConfiguration.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Configurations/Rc2PublishingApprovalConfiguration.cs): Approval persistence contract.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Configurations/Rc2PublishingPublicationConfiguration.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Configurations/Rc2PublishingPublicationConfiguration.cs): Publication persistence contract.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase19VideoQaAuthorityPublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase19VideoQaAuthorityPublisher.cs): Technical certification remains final media gate.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase20PublishingAuthorityPublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase20PublishingAuthorityPublisher.cs): Manifest/package authority remains unchanged.
