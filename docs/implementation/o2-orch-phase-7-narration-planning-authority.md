# O2.ORCH P7.1B-BA — Narration Planning Authority

## Architecture and boundary

P7.1B-BA is a deterministic, read-only authority boundary between committed Scene Knowledge Packets and any future Narration Draft Authority. Contract version `rc2-phase7-narration-planning.v1` accepts only the typed committed Phase 6/P7.1A/P7.1B-A join. It creates no narration prose, provider prompt, physical artifact, transaction, manifest, validation JSON, publication evidence, audio, subtitle, image, or video.

## Governed build result

`INarrationPlanningAuthorityBuilder.Build` returns `NarrationPlanningAuthorityBuildResult`, not an authority or a semantic exception. The result carries validity, the optional authority, reason code, errors, warnings, and blocking issues. Every failed result contains a deterministic, machine-readable blocker: upstream packet blockers are preserved and the planning failure `ReasonCode` is always added, even when the packets have no blockers. The final blocker set is distinct and ordinally sorted; human-readable error prose remains in `Errors` only. Missing/ambiguous Primary references, missing Story Frame lineage, invalid packet identity/checksum, incoherent policy constraints, and invalid claim partitions are governed failures. Exceptions are reserved for programmer defects such as a null top-level argument.

## Authoritative packet validation

Supplied `PacketValidation` remains committed evidence, but is not authority merely because its checksum recomputes. The evaluator validates that evidence, recomputes the packet collection and every packet checksum, reruns `IPhase7SceneKnowledgePacketValidator`, requires `P7PACKET_VALID` with every gate passing, and compares reason code, ordinal gate names/states, and deterministic validation checksum. Any difference returns `NARRATION_PLANNING_PACKET_VALIDATION_MISMATCH`. Upstream and packet warnings are preserved, and cancellation flows through the committed evaluator.

## Transition identity and lineage

`NarrationPlanningCanonicalizer.ComputeTransitionId` explicitly binds contract version, ExecutionId, Variant, Kind, both endpoint IDs and checksums, source `TransitionOut`, destination `TransitionIn`, and Previous/Current/Next PacketIds. It is not a prefix of the generic transition checksum, and identical scene semantics in another execution produce a different identity.

Incoming and outgoing transitions are built separately. Opening is `(null, current)` with `(null, current packet, next packet)`; internal incoming is `(previous, current)` with `(previous packet, current packet, next packet)`; internal outgoing is `(current, next)` with the same three positions relative to the current scene; closing is `(current, null)` with `(previous packet, current packet, null)`. Validation recomputes identity and checksum and reconciles execution, variant, kind, endpoints, endpoint checksums, authored transition text, exact packet lineage, adjacency, opening/closing null semantics, and the semantic edge shared by adjacent scenes.

## Diagnostics model

Diagnostics now reconcile packet and total scene counts; Long and Short scene counts; Primary, Supporting, Required, Resolved, and Deferred reference counts; transition and blocking-issue counts; all three claim-partition counts; and warning/error counts. `DeferredReferenceCount` means status `Deferred` only. Missing, Ambiguous, CrossVariantInvalid, and Unsupported each have a distinct counter; `UnresolvedReferenceCount` is exactly their sum and excludes Deferred. Candidate `FailedGateCount` is zero. Validation results already expose failed gates directly, so this avoids a circular authority-checksum dependency while still making failed-gate state observable after validation.

## Governed semantic reconciliation

`NarrationPlanningReferenceGovernance.IsGovernedResolvedPrimary` is the single Primary predicate used by build admission, scene projection, validation reconciliation, and diagnostics. Primary variant ownership is inherited from successful supplied `P7PACKET_VALID` evidence (all gates passed), the evaluator's successful authoritative `IPhase7SceneKnowledgePacketValidator` recomputation, the packet `Variant`, and packet-local `ReferenceResolutions` and `KnowledgeReferenceIds`. The packet validator already rejects cross-variant references before planning input authority is admitted. P7.1B-BA never infers ownership from reference strings, IDs, naming conventions, claims, collection position, or packet order.

`HasValidatedVariantOwnership` independently rechecks direct builder calls: a packet must occur in exactly one collection, agree with its Long/Short collection, keep Primary rows packet-local, contain no `CrossVariantInvalid` resolution, and carry valid all-gates-passed packet evidence. Build admission rejects violations with `NARRATION_PLANNING_PACKET_VARIANT_OWNERSHIP_INVALID`; validation uses the same rule for scene, Primary, diagnostics, and Long/Short reconciliation. No P7.1B-A contract was changed and no `Variant` field was added to `Phase7PacketReferenceResolution` in this micro-pass.

After ownership is established, the governed Primary predicate requires Primary and Resolved state, a nonempty resolved-claim list, an authored reference ID, claims drawn only from the packet partitions, and—when required—at least one Required claim. Exactly one such reference is accepted. Primary and valid resolved Supporting references are projected in `KnowledgeReferenceIds` authored order; malformed and unresolved rows are excluded.

`NarrationPlanningPolicyCatalog` is the immutable, provider-independent owner of goal, strategy, claim-usage, qualification-prefix, and transition-kind identities. The builder consumes those constants. Validation reconciles every Narrative Goal field to packet/input authority, every Strategy field to packet/catalog authority, and every claim-usage value to the catalog, in addition to recomputing their checksums. A recomputed checksum therefore cannot legitimize modified policy content.

`SceneAuthorityGate` reconciles scene identity, variant, Story Frame and packet lineage, viewer question, objective, exact authored Primary/Supporting sequences, all three semantic-set claim partitions, prohibited/safety/editorial sets, authored visual targets, and all duration bounds. `ConstraintPolicyGate` reruns the injected singleton-safe `INarrationPlanningConstraintPolicy` with the original packet/input request and requires exact deterministic constraint content, preferred sentence count, reading time, and packet duration values. Expected policy-domain argument failures become a failed semantic gate; cancellation and fatal runtime exceptions are not caught.

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

## Verification record (2026-08-04 UTC)

The hardening covers unified Primary governance, exact diagnostics, goal/strategy/claim-policy reconciliation, injected constraint-policy recomputation, complete packet-derived scene reconciliation, authored reference ordering, checksum canonicalization, claim/safety/qualification gates, Long/Short isolation, provider isolation, and DI lifetime/registration inspection.

Exact focused command: `PATH=/tmp/dotnet10:$PATH dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --filter 'FullyQualifiedName~NarrationPlanning' --logger 'console;verbosity=minimal'`.

- Focused P7.1B-BA: total **19**, passed **19**, failed **0**, skipped **0**, duration **2 s**.
- P7.1A regression command: `PATH=/tmp/dotnet10:$PATH dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-build --filter 'FullyQualifiedName~Phase7Knowledge' --logger 'console;verbosity=minimal'`.
- P7.1A regressions: total **145**, passed **145**, failed **0**, skipped **0**, duration **582 ms**.
- P7.1B-A regression command: `PATH=/tmp/dotnet10:$PATH dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-build --filter 'FullyQualifiedName~Phase7SceneKnowledgePacket' --logger 'console;verbosity=minimal'`.
- P7.1B-A regressions: total **7**, passed **7**, failed **0**, skipped **0**, duration **86 ms**.
- Broader Phase 7 command: `PATH=/tmp/dotnet10:$PATH dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-build --filter 'FullyQualifiedName~Phase7' --logger 'console;verbosity=minimal'`.
- Broader Phase 7: total **268**, passed **257**, failed **11**, skipped **0**, duration **3 s**. All 11 failures are pre-existing semantic-source/API-path or missing-fixture regressions outside the P7.1B-BA files changed here; focused P7.1B-BA failures remain zero.
- Azure OpenAI calls = **0**; Azure Speech calls = **0**; Prompt composer calls = **0**; Narration generator calls = **0**.
- The focused run compiled the full test dependency graph successfully. Existing compiler/analyzer warnings and the known `SQLitePCLRaw.lib.e_sqlite3` NU1903 advisory remain unrelated warnings, not test failures.
- No narration prose or physical publication occurred.
- No real 12 Long / 4 Short planning certification is claimed because the committed P7.1B-A fixture is not present.
