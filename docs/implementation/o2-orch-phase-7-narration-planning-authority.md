# O2.ORCH P7.1B-BA — Narration Planning Authority

## Architecture

P7.1B-BA is a deterministic, read-only authority boundary between committed Scene Knowledge Packets and any future Narration Draft Authority. Its input evaluator accepts only the typed committed Phase 6/P7.1A/P7.1B-A join and a checksum-bound packet collection. Downstream narration generators are expected to consume `NarrationPlanningAuthority`, rather than Story Frames or Knowledge Authority directly.

## Contracts

`Phase7NarrationPlanningInputAuthority` carries the published Story Frame and Knowledge authorities, packet collection, family profile, execution/profile identity, Phase 4–7 lineage, and runtime compatibility evidence. `NarrationPlanningAuthority` contains independent Long and Short scene plans plus diagnostics. Every scene binds its Story Frame, packet identity/checksum, governed claims, qualifications, timing, constraints, visual targets, and both transition edges.

The contracts contain claim identifiers and declarative policies only. **No narration text is produced in P7.1B-BA.**

## Planning model

Narrative goals are deterministically composed from scene role, section, viewer-question identity, learning-objective identity, required-claim identifiers, and profile identity. Constraints govern sentence bounds, reading-time target, pauses, emphasis, claim order, and visual synchronization. They neither contain generated narration nor depend on provider state.

Incoming and outgoing transition identifiers are computed from adjacent Story Frame lineage. Variant opening and closing edges use explicit null endpoints. Long and Short packet sequences are planned independently; Short is never truncated from Long.

## Validation

`NarrationPlanningValidator` exposes the required fourteen named gates: input authority, coverage, scene planning, packet lineage, viewer question, learning objective, narrative goal, transitions, constraints, required claims, safety, culture, location/time, and determinism. A failure produces stable gate errors and an invalid verdict.

## Determinism

Each scene checksum binds execution, variant, Story Frame, packet identity/checksum, ordered required-claim IDs, planning constraints, and transition IDs. Authority and diagnostics checksums use the repository canonical SHA-256 serializer. No provider, prompt, narration, audio, or subtitle state participates.

## Remaining work

P7.1B-BA does not implement publication transactions or Narration Draft Authority. Repository fixture certification for P7.1B-A remains external to this foundation. Future narration implementations must accept this planning authority as their sole upstream planning contract and must not bypass it to read Story Frames or Knowledge Authority.
