# Hardware and cost analysis

Audit date: 2026-10-21. Branch: `RecoveryBrach-18-05-26`. Source commit: `0e5ba735191036fdfac8c35c789805b83f03c785`. Audit only; no migration, model installation, generation, publication, or certification run was performed.

## Observed cloud machine

Read-only commands: `/etc/os-release`, `lscpu`, `free -h`, `df -h /workspace`, cgroup cpu.max/memory.max, device/tool presence, Python/FFmpeg/Docker version commands. Hardware is this **cloud task**, not the user's Windows deployment or future inference server.

| Resource | Observed result | Implication |
|---|---|---|
| OS | Debian GNU/Linux 13 (trixie), x86_64 | Current deployment workflow's IIS/NSSM details do not apply directly |
| CPU model / visible CPUs | AMD EPYC 9V74; 5 logical CPUs visible | Host model is not an 80-core allocation |
| CPU quota | cpu.max = 400000 / 100000 | Effective budget ~4 CPU cores; begin ≤4 inference threads |
| RAM | free reported ~33 GiB host-visible; cgroup memory.max=34359738368 bytes | Hard limit **32 GiB**; reserve headroom and monitor cgroup/RSS |
| Swap | 0 | Avoid models/context/concurrency that approach memory cap |
| GPU / VRAM | nvidia-smi absent; no /dev/nvidia* or /dev/dri exposed by probe | No usable GPU demonstrated; physical host GPU/VRAM UNKNOWN |
| Workspace disk | Overlay size 32 GiB; ~30 GiB available at inspection | Insufficient for an unbounded model cache plus long-term media archive |
| Python | 3.12.14 | Local AI engine version compatibility still needs testing |
| Docker | Client and daemon 28.4.0 responded | Container capability available; no inference containers created |
| FFmpeg | 7.1.5 Debian build | Available for normalization/rendering; no new media generated |
| .NET | Existing installed SDK 10.0.402 from onboarding | Current recovery branch was not rebuilt during audit; earlier main results are not current validation |

## Deployment recommendations

| Profile | Starting configuration (proposal) | Supported expectation / unproven limits |
|---|---|---|
| This CPU-only cloud task / similar workstation | Qwen2.5-7B Q4_K_M with Ollama or llama.cpp; 8k initial context, concurrency 1; Kokoro/Piper voice pilot; existing astronomy renderers | Reasonable for offline evaluation and queued text/TTS jobs; tokens/sec, audio real-time factor and full pipeline time UNKNOWN. Do not promise timely fresh thumbnail/gallery diffusion. Serialize inference and FFmpeg. |
| Entry GPU workstation | 32–64 GiB RAM, independently measured 12–16 GiB usable VRAM, 1–2 TB SSD; GPU-capable text and SDXL-class image trials | Run image generation and LLM sequentially if memory cannot hold both; no guaranteed model/size throughput from VRAM count alone. Check driver/runtime/OS/model licenses first. |
| Dedicated inference server | 64–128 GiB RAM, reviewed 24 GiB+ GPU(s), durable model/cache SSD and separate media storage; private API connection and bounded queue | Size from measured p95 prompt/context/image/audio load and concurrency. Separate public-media HTTPS origin from inference endpoints. Larger/continuous jobs justify hardware only after pilot quality gates. |

LLM disk estimate: ~4–6 GiB quantized 7B weights, plus tokenizer/runtime/cache. Working RAM reserve ~8–12 GiB at initial context is a planning allowance, not a measured peak. Keep 6–10 GiB available for .NET/Postgres/TTS/rendering and filesystem cache. TTS weights may be small relative to LLM but Python/PyTorch/frontend dependencies can consume several additional GiB. Diffusion checkpoints/dependencies/cache can add 10–25+ GiB; allocate additional storage before attempting a full stack here. Never download everything merely to fill a matrix.

## Storage and bandwidth sizing examples

These are arithmetic planning assumptions, **not measured repository output sizes**:

- One 8-minute long video at 6 Mb/s: 480×6/8 = **360 MB**. One 60-second short at 6 Mb/s: **45 MB**. Add 30 MB images/manifests and ~10 MB narration: about **445 MB per language/plan**, excluding intermediate render segments.
- Two languages × one plan/day × 30 days: ~**26.7 GB** final media. A second backup doubles that to ~53.4 GB before intermediate files, model cache, logs and growth. Cloud workspace free space cannot safely serve as long-term archive for this workload.
- Public staging scope can be much smaller for RC2 Instagram images than a full video archive. At 20–50 MB staged assets/plan and 2–5 fetch/retry downloads, allow ~40–250 MB outbound per plan; legacy reel staging adds video bytes. 30 plans/month gives ~1.2–7.5 GB image-staging egress under those assumptions. Actual view-serving/CDN traffic is separate and can dominate.
- Example upload lower bound: 445 MB over sustained 20 Mb/s ≈178 seconds, ignoring protocol/retry/provider processing. An always-on HTTPS origin needs stable upstream connectivity, DNS/TLS, monitoring and enough retention for uncertain publication retries.

## Costs: no mandatory paid AI API does not mean zero operating cost

| Cost category | Planning estimate / formula | Caveat |
|---|---|---|
| Software/model installation | $0 acquisition for reviewed free licenses; engineering effort below | Some model/voice/library licenses or redistribution constraints may invalidate a candidate; exact weights not yet approved |
| Existing workstation | $0 incremental hardware purchase if adequate hardware already exists | Its electricity, storage wear, Internet and opportunity cost are not zero |
| New entry GPU machine | Budgetary ~$800–$1,800 for a complete used/new system; verify local quotes | Not a vendor quote; exact GPU/RAM/SSD requirement depends on measured workload |
| Dedicated GPU server | ~$2,000–$5,000+ capital planning range | Cloud GPU rental or hosted API is optional paid infrastructure, not required software; current task's cloud billing UNKNOWN |
| CPU operating electricity | 120W × 8h/day × 30 /1000 = **28.8 kWh/month**; at $0.15/kWh ≈**$4.32/month** | Example wall power/tariff; actual power and local electricity tariff UNKNOWN |
| GPU operating electricity | 350W × 8h/day ×30 /1000 = **84 kWh/month**, ~$12.60 at same tariff | Includes an illustrative whole-system load; idle/24h duty differs |
| Public HTTPS origin | Existing always-on host may have no extra hosting subscription; optional small VPS budget ~$5–$15/month plus storage/egress/domain | Pricing unverified; public IP/CGNAT, upload bandwidth and uptime can require paid service. No free permanent public hosting guaranteed |
| Storage/backups | Retained GB × actual local disk or hosting cost/GB | Immutable historical media, database and encryption-key backups all matter |
| Internet | Actual uplink, monthly cap, staging egress × provider tariff | Zero egress fee is not universal; retries and downstream consumption add traffic |
| Hosted free inference | No recurring fee only while within revocable quota/terms | Cannot be the required production path; no free quota verified here |

M0–M7 engineering effort is estimated at **7–12 engineer-weeks**, plus bilingual listening and license/security review time; this is a scope-based estimate, not a fixed bid. Parallel staffing can reduce calendar time but adds integration work. No implementation or sub-agent delegation occurred in this audit.

## Evidence and limitations

Hardware command observations above are the evidence for the resource numbers. Model disk/RAM, hardware prices, energy, media sizes and durations are explicitly planning estimates. Repository workload evidence: Phase15 scene synthesis and codec checks in `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`; six images in `Backend/src/Astronomy.MediaFactory.Rendering/Phase13GalleryAuthority.cs`; three aspect candidates in `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/MatureThumbnailCandidatePublisher.cs`; retained manifests/MP4/captions in Phase18/19/20 publisher files. No throughput, MOS, WER, GPU load, power measurement or paid-provider price research was performed.
