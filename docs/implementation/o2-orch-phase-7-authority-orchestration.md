# O2.ORCH — RC2 Phase 7 authority orchestration integration

## Public Phase 7 boundary

Public RC2 phase `7` remains a single public phase. Its registry name is now `Narration Authority`, and the production runner resolves the provider-free authority orchestrator rather than the former P7.1A-only knowledge lifecycle.

## Internal sub-stage order

The orchestrator executes the frozen internal order:

1. P7.1A Knowledge Authority publication.
2. P7.1A committed-state evaluation.
3. P7.1B-A Scene Knowledge Packets build and validation.
4. P7.1B-BA Narration Planning Authority input evaluation, build, and validation.
5. P7.1B-BB Narration Planning Publication and committed-state evaluation.
6. P7.1C-A Narration Draft Authority in-memory build and validation.

## Typed authority flow

The request carries only coordinates and execution options. It never carries caller-authored claims, packets, planning scenes, or draft sentences. Packets are built from the typed scene-packet input authority, planning consumes the validated packet collection, publication consumes the same typed planning request, and draft authority consumes committed planning and committed knowledge coordinates.

## Failure short-circuiting

The orchestrator stops on the first failed boundary. A knowledge failure prevents packet, planning, publication, and draft calls. Packet failure prevents planning, publication, and draft. Planning failure prevents publication and draft. Publication or committed-state failure prevents draft. Draft failure marks public Phase 7 failed without deleting valid committed upstream artifacts.

## Overwrite, reuse, and retry behaviour

Overwrite, reuse, and retry-failed-only are delegated to the existing governed P7.1A and P7.1B-BB services. The orchestrator passes the resolved execution-mode flags through and does not create competing retry semantics. In-memory packet, planning-build, and draft stages are recomputed against the current typed authorities for each execution.

## Physical versus in-memory artifacts

Physical outputs are limited to the existing P7.1A knowledge package and P7.1B-BB planning package, including their validation, manifest, and publication-evidence files. P7.1C-A remains in memory; no draft directory, draft manifest entry, narration prose, audio, SRT, TTS, image, video, or Phase 8 artifact is published by this milestone.

## Provider isolation

The orchestrator constructor depends only on Phase 7 authority services and validators. It does not inject Azure OpenAI, prompt composers, `NarrationGeneratorV5`, translation providers, Azure Speech, TTS, SRT, rendering, or Phase 8 services. Runtime provider-isolation evidence is returned as zero counts for all prohibited provider/media categories.

## Test totals and Orion physical certification evidence

This container does not include the .NET SDK, so focused orchestration tests, P7.1A/P7.1B-A/P7.1B-BA/P7.1B-BB/P7.1C-A regressions, broader Phase 7 tests, RC2 partial-range tests, and the physical Orion endpoint certification could not be executed here. No pass totals or Orion 12 Long / 4 Short physical certification are claimed by this document.

## Remaining warnings

Executable certification remains required in a .NET-enabled environment. The public response now carries internal stage details on the Phase 7 result, but endpoint evidence must be captured from the real Orion request before this milestone can be called physically certified.

## Freeze recommendation

Do not freeze solely from this environment. Freeze is recommended only after the focused and regression test matrix passes and the real Orion endpoint proves 12 Long and 4 Short packet, planning, and draft counts; all 27 draft gates; zero blockers; committed planning publication; and zero provider/media calls.
