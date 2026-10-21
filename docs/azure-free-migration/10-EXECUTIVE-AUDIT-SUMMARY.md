# Executive Azure-independence audit summary

Audit date: 2026-10-21. Branch: `RecoveryBrach-18-05-26`. Source commit: `0e5ba735191036fdfac8c35c789805b83f03c785`. Audit only; no migration, model installation, generation, publication, or certification run was performed.

## Decision

**AUDIT INCOMPLETE.** The requested ten audit documents have been produced, and source-based dependency/phase discovery is complete for the inspected commit. Readiness evidence is still missing: exact candidate weight/voice/aligner/diffusion licensing and hashes, an accessible Azure English/Hindi audio baseline, the mandatory paired human listening comparison, target-workload inference benchmarks, and a verified public HTTPS staging deployment. This is not approval to migrate or a claim that local substitutes preserve all features.

No production code, dependency declarations, configuration, database schema, orchestration, certified media, canonical artifacts, publishing endpoint or social record was changed. No live publishing, phase generation or model download/installation occurred. Only this documentation directory was added. There was no environment configuration draft update in this audit.

## Final report — requested 20 answers

1. **Total Azure dependencies:** **13 implementation entries**, grouped into **8 capabilities across 6 service families**. Count definition and every class/method/input/output/config/mandatory/runtime/risk field are in [inventory 01](01-AZURE-DEPENDENCY-INVENTORY.md). Eight Azure-related package declarations are not eight proven runtime services.
2. **Active Azure AI dependencies:** Reachable production wiring uses Azure OpenAI text, Azure OpenAI images and Azure Speech. **Observed successful live activity: Unknown / not tested.** Current cloud machine has no inspected application credential binding. Do not confuse registered or enabled code with live production telemetry.
3. **Active infrastructure dependencies:** Azure archival/public staging implementations are registered; current Instagram staging explicitly requires AzureBlob. Entra identity is optional, Key Vault and Application Insights are conditional and unconfigured in inspected settings. Actual remote deployment use is Unknown. PostgreSQL, Docker .NET images and IIS/NSSM deployment are not inherently Azure subscriptions.
4. **RC2 phases affected:** Direct AI edges in **7, conditionally 8, 12, 13, 15**. Phases 9–11, 14, 16–20 have upstream media/authority dependency; all can also be blocked by global Azure startup/configuration gates. Phase20 does not itself publish remotely; Instagram public staging is later execution. See [matrix 02](02-RC2-PHASE-DEPENDENCY-MATRIX.md).
5. **Critical workflows:** New governed narration, scene TTS, independent thumbnail/gallery image generation, selected cinematic scene imagery, and externally fetchable Instagram media. Legacy weekly TTS, raw HTTP Hero/VisualLab/optimization and archive/recovery paths must not be missed.
6. **Replacement for each:** Text → local Ollama/llama.cpp through existing interfaces; Speech → reviewed local English/Hindi TTS plus codec/pause adaptation; image → reviewed local diffusion only for permitted atmosphere plus retained factual renderers; archive → durable filesystem/optional S3; public staging → signed read-only public HTTPS; identity/vault → no mandatory Azure auth, secure existing social secret handling; monitoring → existing logs/optional OTEL. Full per-entry mapping is in 01 and [architecture 05](05-PROVIDER-INDEPENDENT-ARCHITECTURE.md).
7. **English TTS recommendation:** Kokoro-82M with upstream example af_heart as a provisional pilot; Piper as a reviewed-voice alternative. Not approved without baseline/listening/license evidence.
8. **Hindi TTS recommendation:** Kokoro Hindi language h with a pinned reviewed Hindi voice; compare against an appropriately licensed Hindi Piper/Indic specialist candidate. hf_alpha is a voice name to verify, not an approved mapping. No candidate has passed native-speaker comparison here. See [TTS 04](04-TTS-ENGLISH-HINDI-EVALUATION.md).
9. **Local LLM recommendation:** Ollama + Qwen2.5-7B-Instruct Q4_K_M, concurrency=1 and initial 8k context on this CPU-limited host. Choose immutable artifact only after prompt-budget, factual/JSON and bilingual benchmarks; exact model/quantization license verification remains open.
10. **Image AI recommendation:** Retain certified scientific sources/geometry and deterministic overlays. Pilot ComfyUI + a reviewed SDXL-class model for allowed atmosphere/backgrounds on suitable measured GPU. No checkpoint selected/approved. CPU throughput here is not established; simply disabling Phase12/13 image generation does not preserve features.
11. **Storage recommendation:** Durable private filesystem archival plus IPublicMediaStorageService public HTTPS signed staging with stable keys/renewal, backup and retry-aware retention. Optional MinIO only if S3 capabilities justify additional operations/license work. Localhost is insufficient for Meta.
12. **Hardware feasibility:** 5 visible logical CPUs but **4-core quota**, **32 GiB memory cap**, ~30 GiB disk free, no exposed GPU. Plausible for queued 7B quantized text and small TTS pilots; production diffusion not proven. GPU/VRAM of actual user's deployment UNKNOWN. See [hardware/cost 07](07-HARDWARE-AND-COST-ANALYSIS.md).
13. **Risks:** Critical TTS/language/voice/pause/timing equivalence; image quality/science/fallback policy; provider IDs embedded in authority; hardcoded Azure startup/storage guards; public HTTPS availability and uncertain publication retries; unverified exact model/voice licenses; CPU/disk bottlenecks. Never forge historical provenance/checksums or reset publishing checkpoints.
14. **Estimated effort:** Approximately **35–59 engineer-days / 7–12 weeks**, plus outside baseline, hardware/hosting, licensing and bilingual review waits. Estimate only, no implementation started.
15. **Expected recurring cost:** No mandatory paid AI API proposed. Existing hardware inference still consumes electricity/storage/Internet. Illustrative CPU energy ~$4.32/month at 120W×8h/day and $0.15/kWh; optional HTTPS VPS ~$5–$15/month plus storage/domain/egress. Actual current cloud billing, tariffs and usage UNKNOWN. Free software/local inference/free hosted quota are different from zero operating cost.
16. **Required code changes:** Provider implementations/DI, separate direct HTTP boundaries, selected-provider startup/readiness, Phase15 neutral synthesis/voice/policy validation, all speech consumers, Azure-only public-storage gates, secure filesystem URL renewal. Reuse equivalent abstractions; no RC2 boundary rewrite or embedding feature required. See 05 and [plan 08](08-MIGRATION-IMPLEMENTATION-PLAN.md).
17. **Required configuration changes:** Per-capability local endpoint/model/voice digests, explicit no-paid-fallback policy, bounded context/concurrency, selected storage/public TLS/expiry settings, secure signing/social credentials, host-specific tools/paths. Existing Azure settings retained as optional legacy configuration, not blindly deleted. [Config 06](06-CONFIGURATION-MIGRATION.md) is illustrative, not implemented.
18. **Required tests:** Typed adapter failures and JSON/semantic integrity, English/Hindi paired TTS human review, codec/pause checks, exact SRT/scene lineage, all image aspects/roles, fresh isolated Phase1–20/Phase19/20 authority checks, public media TLS/expiry/renewal, OAuth/checkpoint/remote uncertainty and no duplicate publication. [Certification 09](09-REGRESSION-AND-CERTIFICATION.md) records proposed thresholds and unrun outcomes.
19. **Sequence:** M0 evidence/baseline → M1 local text and early M5 public staging → M2 English/Hindi TTS → conditional M3 alignment/QA → M4 reviewed image replacement → M6 fresh end-to-end certification → M7 publishing regression with mocks/sandbox first. No live writes without separate explicit authorization.
20. **Exact first implementation task:** Implement a provider-selected Ollama INarrationPerformer with mocked transport/contract tests, pinned-model/config guards and truthful invocation diagnostics, without touching orchestration or canonical media. Keep Azure validation conditional on selection and no paid fallback. This is proposed for a future approved task; details/prerequisites in 08.

## Missing evidence and how to close it

- Supply authorized **read-only** Azure English/Hindi baseline audio plus matching scripts, voice/rate/language and codec metadata; do not supply credentials in chat. Run paired candidate/listening benchmarks in a later approved evaluation workspace.
- Obtain official exact model/voice/aligner/image model cards/licenses and hashes. Hugging Face reads failed via the current network route; no network setting was changed or bypassed. Runtime licenses alone are insufficient for commercial approval.
- Confirm production-target CPU/GPU/VRAM/storage and required daily throughput; only this cloud task's resources were inspected. Measure real local latency/memory/disk and validate proposed deadlines.
- Select/provision a durable public HTTPS staging origin and verify external provider fetch/renewal. Existing YouTube/Meta credentials, account permissions and provider terms are not proven by Git access.
- After separately approved implementation, run current-branch tests and fresh non-publishing RC2 certification, then safe publishing regression. No audit document claims these have passed.

## Report index

- [01-AZURE-DEPENDENCY-INVENTORY.md](01-AZURE-DEPENDENCY-INVENTORY.md)
- [02-RC2-PHASE-DEPENDENCY-MATRIX.md](02-RC2-PHASE-DEPENDENCY-MATRIX.md)
- [03-FREE-AI-PROVIDER-EVALUATION.md](03-FREE-AI-PROVIDER-EVALUATION.md)
- [04-TTS-ENGLISH-HINDI-EVALUATION.md](04-TTS-ENGLISH-HINDI-EVALUATION.md)
- [05-PROVIDER-INDEPENDENT-ARCHITECTURE.md](05-PROVIDER-INDEPENDENT-ARCHITECTURE.md)
- [06-CONFIGURATION-MIGRATION.md](06-CONFIGURATION-MIGRATION.md)
- [07-HARDWARE-AND-COST-ANALYSIS.md](07-HARDWARE-AND-COST-ANALYSIS.md)
- [08-MIGRATION-IMPLEMENTATION-PLAN.md](08-MIGRATION-IMPLEMENTATION-PLAN.md)
- [09-REGRESSION-AND-CERTIFICATION.md](09-REGRESSION-AND-CERTIFICATION.md)

**Stop after audit and document generation. Do not implement migrations until explicitly approved.**
