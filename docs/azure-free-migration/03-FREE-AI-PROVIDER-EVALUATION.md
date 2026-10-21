# Free AI provider evaluation

Audit date: 2026-10-21. Branch: `RecoveryBrach-18-05-26`. Source commit: `0e5ba735191036fdfac8c35c789805b83f03c785`. Audit only; no migration, model installation, generation, publication, or certification run was performed.

## Primary recommendation and limits

**Provisional text stack: Ollama + Qwen2.5-7B-Instruct, Q4_K_M quantization, one request at a time, initially 8,192 tokens of context.** Choose the exact immutable model/quantization digest only after prompt-size and English/Hindi astronomy benchmarks. The measured machine has a four-core CPU quota, 32 GiB cgroup RAM cap, no exposed NVIDIA device/tool, and about 30 GiB available disk. A 7B quantized model is a plausible CPU batch candidate, not a demonstrated production-throughput result. No inference benchmark was run and no weights were installed.

Start with the existing `INarrationPerformer` interface, not a new parallel ITextGenerationProvider. Preserve NarrationProviderCall IDs, prompt checksums, variant isolation, scene-count/token budgets, error categories and invocation evidence. Current Azure JSON-object mode is weaker than a full JSON schema; local grammar support still needs all existing semantic/acceptance validators.

## Runtime comparison

| Runtime | English/Hindi and facts | JSON, prompts, context and tools | Hardware / latency / concurrency | Licensing / decision |
|---|---|---|---|---|
| Ollama + Qwen2.5-7B-Instruct Q4_K_M | Multilingual candidate; English/Hindi narration naturalness and astronomy terminology unmeasured. Facts must come from existing certified knowledge, not model memory. | Translate current system/user messages; use structured/schema output where supported; retain parsing and semantic checks. Context configured explicitly; measure actual longest Long/Short requests before increasing. Tool support varies by model/template/version; current chat request does not send tools. | Approximate 4–6 GiB weights; reserve 8–12 GiB total including KV/runtime and leave memory for .NET/Postgres/FFmpeg. CPU batch operation only; tokens/sec and first-token latency UNKNOWN. Start concurrency=1. | Ollama MIT verified. Official Qwen repo README states Apache-licensed open weights; exact model card and selected quantization provenance not retrieved. Preferred pilot, not approved deployment. |
| llama.cpp + same reviewed GGUF | Same model quality, not an independent accuracy improvement. | Grammar/JSON constraints and explicit server parameters; implement message/template mapping and request/error translation. Do not expect Azure deployment URLs to work unchanged. | CPU/AVX paths fit observed CPU instruction set; can control thread count and KV memory. Begin ≤4 inference threads; serialize with video rendering. Benchmark instead of assuming faster than Ollama. | MIT verified; GGUF license must match original model and converter provenance. Preferred lower-level fallback runtime. |
| vLLM / other GPU inference server | Model-dependent English/Hindi quality. | OpenAI-like endpoint is useful but not identical to Azure HTTP; schema/tool support version-dependent. | Appropriate for a dedicated measured GPU and higher concurrency; no exposed GPU here, so not current-machine recommendation. | Pin and review chosen runtime/model release before adoption; not evaluated/installed here. |
| Optional hosted free quotas | Provider-dependent; data leaves machine. | Rate limits, retention and model changes can break repeatability. | Quota/availability not guaranteed; cannot unblock a required zero-paid workflow reliably. | No hosted account/terms/quota verified; never a mandatory provider or automatic paid fallback. |

Temperature 0 or low temperature, fixed seed where supported, model digest, prompt version and decoding parameters improve repeatability; they do **not** guarantee byte-identical output across hardware/runtime releases. The canonical authority checksum must remain a checksum of actual accepted content. Do not counterfeit old Azure model identity.

No evidence that current RC2 requires tool/function calling or embeddings: text requests send two messages, temperature=0.2, max_tokens and JSON-object response format. Preserve that simpler contract first. Long narration may exceed 8k context; reject over-budget requests explicitly or certify a larger window, rather than silently truncate governed facts or scenes.

## Image evaluation and actual replacement scope

Fresh Phase12 and Phase13 output cannot be preserved by simply disabling AI imagery. Current providers generate independent per-aspect thumbnail backgrounds and six gallery backgrounds with deterministic factual overlays. Phase11 instead reuses certified Phase8 rasters. Phase8's astronomy/scientific geometry remains owned by verified knowledge, Skyfield/catalog projection/Stellarium-oriented renderers, not diffusion.

| Candidate | Appropriate use | Compatibility and resources | Decision |
|---|---|---|---|
| Existing factual astronomy sources + ImageSharp/FFmpeg + scientific renderers | Accurate sky geometry, labels, actual telescope/spacecraft images, deterministic composites; maintain provenance and usage rights | Lowest extra inference cost; existing source/cache and geometry certification must remain. NASA/third-party imagery is not automatically unrestricted: track credits and exceptions. | Retain for factual authority; does not automatically replace mandatory independent Phase12/13 image generation. |
| ComfyUI + a reviewed SDXL-class checkpoint | Non-factual cinematic atmosphere/backgrounds explicitly allowed by existing prompts | A queued local image provider can return image bytes/path into existing staging. Render at model-native dimensions then deterministic aspect conversion only if quality/geometry gates allow. On this CPU-only machine, practical throughput UNKNOWN and likely unsuitable for routine multi-image production. Plan a separately measured 12–16 GiB GPU pilot, not a guarantee. | Conditional pilot only. ComfyUI GPLv3 verified; exact diffusion checkpoint license, safety restrictions and weight origin NOT verified. No model approved. |
| CPU-only diffusion | Small offline feasibility experiment | Competes with LLM/FFmpeg; no measured latency or memory profile; full gallery/thumbnail workload unbenchmarked. | Do not promise feature-equivalent production throughput. |
| Generated astronomical diagrams/precise sky maps | Would fabricate scientific observations/alignments | Conflicts with current image prompt restrictions and scientific certification. | Reject. |

The main D03 image generator and independent raw HTTP clients in Hero, Gallery and Visual Lab all need discovery coverage. Replacing only `IAICinematicImageGenerator` leaves D04–D06 calls. Image analysis, captioning, enhancement and visual prompt composition were not confirmed as Azure AI Vision tasks; much is deterministic local code. Do not invent a Vision migration.

## Embeddings / search

No embedding generation, Azure Search client, vector index or pgvector storage was found. References reject accidentally configured embedding chat deployments. Therefore no embedding/index migration is required now.

If later source-grounded retrieval is approved: evaluate sentence-transformers (engine Apache-2.0 verified), multilingual BGE-M3 (exact model license/card not retrieved), and pgvector (PostgreSQL-style license verified). Version source corpus, tokenizer/model digest, normalization, vector dimension, distance metric and index. Never insert a new model's vectors into an old incompatible index; build a side-by-side index from preserved documents, validate bilingual retrieval, then switch the index alias. This is a future capability, not a required migration stage.

## Upstream license evidence and open questions

Read-only retrieval on the audit date succeeded for the links below. These are live upstream main/master documents, not immutable release attestations; implementation must pin commits, weight digests and applicable notices. MIT/Apache/BSD allow commercial use subject to notices and other terms. GPL/AGPL software can be used commercially but distribution/network-source obligations require review; an engine license does not grant model/data/voice rights.

| Item | Evidence / result | Commercial-use assessment |
|---|---|---|
| Ollama | [LICENSE](https://raw.githubusercontent.com/ollama/ollama/main/LICENSE), HTTP 200: MIT | Keep copyright/license notices; model rights separate. |
| llama.cpp | [LICENSE](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/LICENSE), 200: MIT | Same separation from weights. |
| Qwen2.5 | [official README License Agreement](https://raw.githubusercontent.com/QwenLM/Qwen2.5/main/README.md), 200 | Repository states Apache open weights; exact Qwen2.5-7B-Instruct model card, tokenizer and quantized redistributor artifacts still need verification. Do not generalize to every Qwen release. |
| Kokoro runtime | [LICENSE](https://raw.githubusercontent.com/hexgrad/kokoro/main/LICENSE), 200: Apache-2.0; [README](https://raw.githubusercontent.com/hexgrad/kokoro/main/README.md), 200 | README explicitly describes Apache-licensed weights and Hindi language code h. Exact voice/weight revision still unverified. |
| Piper current maintained engine | [COPYING](https://raw.githubusercontent.com/OHF-Voice/piper1-gpl/main/COPYING), 200: GPLv3; [README](https://raw.githubusercontent.com/OHF-Voice/piper1-gpl/main/README.md), 200 | Do not rely on the historical rhasspy/piper MIT license for this distribution. Every voice MODEL_CARD and training-data restriction needs review. |
| faster-whisper / Whisper | [faster-whisper LICENSE](https://raw.githubusercontent.com/SYSTRAN/faster-whisper/master/LICENSE), [Whisper LICENSE](https://raw.githubusercontent.com/openai/whisper/main/LICENSE), both 200: MIT | Pin engine/converted model and notices; do not assume all diarization/alignment companion models share MIT terms. |
| WhisperX | [LICENSE](https://raw.githubusercontent.com/m-bain/whisperX/main/LICENSE), 200: BSD-2-Clause | Its aligner and diarization dependencies have separate licenses/access rules. No alignment-model approval established. |
| ComfyUI | [LICENSE](https://raw.githubusercontent.com/Comfy-Org/ComfyUI/master/LICENSE), 200: GPLv3 | Does not license diffusion checkpoints, LoRAs, custom nodes or datasets. |
| MinIO | [LICENSE](https://raw.githubusercontent.com/minio/minio/master/LICENSE), 200: AGPLv3 | Conditional S3 option; review release availability/support, obligations and deployment security. Not required for initial filesystem staging. |
| sentence-transformers | [LICENSE](https://raw.githubusercontent.com/UKPLab/sentence-transformers/master/LICENSE), 200: Apache-2.0 | Candidate only if retrieval is later added; weight rights separate. |
| pgvector | [LICENSE](https://raw.githubusercontent.com/pgvector/pgvector/master/LICENSE), 200: PostgreSQL-style | No current vector integration found. |

Direct Hugging Face requests for `Qwen/Qwen2.5-7B-Instruct`, `hexgrad/Kokoro-82M` and `BAAI/bge-m3` model cards failed through the current network route (curl exit 56, no HTTP status). No access policy was changed or bypassed. The Kokoro GitHub VOICES.md and attempted Indic-Parler-TTS GitHub README returned 404; those are not license evidence. Exact Hindi voice, diffusion checkpoint and aligner commercial rights remain UNVERIFIED. A reviewed model-card/license snapshot and hash are required before any production approval.

## Repository evidence

- [`Backend/src/Astronomy.MediaFactory.ContentGen/NarrationPerformer.cs`](../../Backend/src/Astronomy.MediaFactory.ContentGen/NarrationPerformer.cs): Existing text-provider contract, request budgets and invocation evidence.
- [`Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAiContentGenerationService.cs`](../../Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAiContentGenerationService.cs): RequestCompletionAsync and embedding-deployment rejection; real HTTP request format.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs): Provider invocation, semantic/structural checks and no-placeholder policy.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/SceneAssetsV3Service.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/SceneAssetsV3Service.cs): Conditional provider use versus scientific render modes and source provenance.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/MatureThumbnailCandidatePublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/MatureThumbnailCandidatePublisher.cs): Three independent backgrounds, deterministic overlays and no fallback.
- [`Backend/src/Astronomy.MediaFactory.Rendering/Phase13GalleryAuthority.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/Phase13GalleryAuthority.cs): Six independent gallery backgrounds.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ResponsiveHeroAuthorityService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ResponsiveHeroAuthorityService.cs): Certified raster reuse with zero provider calls.
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/ImageProviderProfiles.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/ImageProviderProfiles.cs): Existing visual provider profiles; names alone are not executable cloud integrations.
- [`Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAICinematicImageGenerator.cs`](../../Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAICinematicImageGenerator.cs): Prompt disallows fake exact star maps and misleading alignments.
