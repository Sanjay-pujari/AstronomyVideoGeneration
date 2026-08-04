# O2.ORCH P7.1B-BA — Narration Planning Authority

## Architecture and boundary

P7.1B-BA is a deterministic, read-only authority boundary between committed Scene Knowledge Packets and any future Narration Draft Authority. Contract version `rc2-phase7-narration-planning.v1` accepts only the typed committed Phase 6/P7.1A/P7.1B-A join. It creates no narration prose, provider prompt, physical artifact, transaction, manifest, validation JSON, publication evidence, audio, subtitle, image, or video.

## Governed build result

`INarrationPlanningAuthorityBuilder.Build` returns `NarrationPlanningAuthorityBuildResult`, not an authority or a semantic exception. The result carries validity, the optional authority, reason code, errors, warnings, and blocking issues. Missing/ambiguous Primary references, missing Story Frame lineage, invalid packet identity/checksum, incoherent policy constraints, and invalid claim partitions are governed failures. Exceptions are reserved for programmer defects such as a null top-level argument.

## Authoritative packet validation

Supplied `PacketValidation` remains committed evidence, but is not authority merely because its checksum recomputes. The evaluator validates that evidence, recomputes the packet collection and every packet checksum, reruns `IPhase7SceneKnowledgePacketValidator`, requires `P7PACKET_VALID` with every gate passing, and compares reason code, ordinal gate names/states, and deterministic validation checksum. Any difference returns `NARRATION_PLANNING_PACKET_VALIDATION_MISMATCH`. Upstream and packet warnings are preserved, and cancellation flows through the committed evaluator.

## Transition identity and lineage

`NarrationPlanningCanonicalizer.ComputeTransitionId` explicitly binds contract version, ExecutionId, Variant, Kind, both endpoint IDs and checksums, source `TransitionOut`, destination `TransitionIn`, and Previous/Current/Next PacketIds. It is not a prefix of the generic transition checksum, and identical scene semantics in another execution produce a different identity.

Incoming and outgoing transitions are built separately. Opening is `(null, current)` with `(null, current packet, next packet)`; internal incoming is `(previous, current)` with `(previous packet, current packet, next packet)`; internal outgoing is `(current, next)` with the same three positions relative to the current scene; closing is `(current, null)` with `(previous packet, current packet, null)`. Validation recomputes identity and checksum and reconciles execution, variant, kind, endpoints, endpoint checksums, authored transition text, exact packet lineage, adjacency, opening/closing null semantics, and the semantic edge shared by adjacent scenes.

## Diagnostics model

Diagnostics now reconcile packet and total scene counts; Long and Short scene counts; Primary, Supporting, Required, Resolved, and Deferred reference counts; transition and blocking-issue counts; all three claim-partition counts; and warning/error counts. Candidate `FailedGateCount` is zero. Validation results already expose failed gates directly, so this avoids a circular authority-checksum dependency while still making failed-gate state observable after validation.

## Order and deterministic checksums

Authored order is authoritative for `LongScenes`, `ShortScenes`, `PrimaryKnowledgeReferences`, `SupportingKnowledgeReferences`, `VisualSynchronizationTargets`, and profile-authored emphasis sequences. Canonicalization preserves those sequences. Required, Optional, and Deferred claims; forbidden statements; safety and editorial rules; cultural, location, time, astrology, and human-review requirements; and order-free diagnostic warnings/errors are semantic sets and are ordinally normalized. Authority dictionaries are also ordinally normalized.

Scene checksums cover the complete record: viewer question, objective, typed goal and strategy, both authored reference lists, every claim partition, claim policy, constraints, every safety/qualification collection, timing and sentence targets, visual synchronization targets, and both full transitions. Authority checksums are invariant to dictionary insertion order; semantic-set permutations are invariant; authored-order changes are material.

## Claim, safety, and qualification reconciliation

Required, Optional, and Deferred partitions are reconciled as exact ordinal semantic sets, so missing, extra, or moved claims fail. Required and Optional human-review claims fail; a Deferred human-review claim produces an exact claim-specific review requirement. Safety rules, editorial constraints, prohibited claims, and cultural/mythological, location, date-time, astrology, and human-review qualification entries are exact: omissions and fabricated claim IDs fail.

## Long/Short independence

Long and Short retain independent packet collections, Story Frames, planning IDs, and transitions. Validation rejects cross-variant packet identity and planning identity. Short is independently planned and is not a Long prefix projection. Shared claim IDs remain permitted only when independently present in each packet authority.

## Provider and dependency-injection isolation

The P7.1B-BA constructor graph contains only the committed input evaluator, packet validator, constraint policy, builder, and validator. The evaluator is scoped; constraint policy, builder, packet validator, and planning validator are singleton-safe. `INarrationPlanningAuthorityBuilder` is distinct from the older foundation-level `IPhase7NarrationPlanningBuilder`, which remains registered because it is still used elsewhere and was not removed. Planning resolves no provider-facing service and makes no provider calls:

- Azure OpenAI calls = **0**
- Azure Speech calls = **0**
- Prompt composer calls = **0**
- Narration generator calls = **0**

## Verification record

The hardening source includes focused contract, transition identity, constraint determinism/coherence, dependency-injection shape, and provider isolation tests. The current execution container does not include the .NET SDK (`dotnet: command not found`), so focused, P7.1A regression, P7.1B-A regression, and broader Phase 7 totals cannot be truthfully reported from this environment. No real 12 Long / 4 Short planning certification is claimed because the committed P7.1B-A fixture is not present.
