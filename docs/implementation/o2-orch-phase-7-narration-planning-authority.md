# O2.ORCH P7.1B-BA — Narration Planning Authority

## Architecture

P7.1B-BA is a deterministic, read-only authority boundary between committed Scene Knowledge Packets and any future Narration Draft Authority. Contract version `rc2-phase7-narration-planning.v1` accepts only the typed committed Phase 6/P7.1A/P7.1B-A join, a checksum-bound packet collection, and its successful `P7PACKET_VALID` validation. The evaluator recomputes validation, collection, and individual packet checksums and reruns the real packet validator; an outer collection checksum alone is never sufficient.

## Contracts

`Phase7NarrationPlanningInputAuthority` carries non-null published Story Frame and Knowledge authorities, packet validation, packet collection, family profile, execution/profile identity, Phase 4–7 lineage, and runtime compatibility evidence. `NarrationPlanningAuthority` contains independent Long and Short scene plans plus reconciled diagnostics. Every scene binds its Story Frame/source-scene lineage, packet identity/checksum, all three governed claim partitions, claim-specific qualifications, timing, constraints, visual targets, and both transition edges.

The contracts contain claim identifiers and declarative policies only. **No narration text is produced in P7.1B-BA.**

## Planning model

`NarrationPlanningGoal` and `NarrationPlanningStrategy` are typed, checksum-bound declarative records; no pipe-delimited intent strings remain. Primary references come only from exactly one governed, resolved Primary resolution with resolved claims. Supporting references come from governed non-Primary resolutions in authored reference order. A missing Primary is a deterministic blocker and is never guessed from collection position.

The injected `INarrationPlanningConstraintPolicy` uses language, variant, duration bounds, family profile, scene role/section, and claim counts to govern coherent minimum/preferred/maximum sentence counts, bounded reading time, pauses, emphasis, claim order, and visual synchronization. English and Hindi are evaluated independently through typed policy rates rather than builder constants.

Incoming and outgoing transition identities bind execution, variant, kind, endpoint IDs/checksums, upstream `TransitionOut`/`TransitionIn`, and packet lineage. Variant opening and closing edges use explicit null endpoints; internal edges follow adjacent authored Story Frames. Long and Short packets, Story Frames, planning identities, transitions, and reference resolutions remain variant-local; Short is independently planned and is never truncated from Long.

## Validation

`NarrationPlanningValidator` executes contract, input identity, profile, language, coverage, scene, packet lineage, viewer-question, learning-objective, goal, transition, constraint, Required/Optional/Deferred reconciliation, safety, cultural, location/time, astrology, human-review, Long/Short independence, diagnostics, authority-checksum, and determinism gates. Required and Optional partitions exclude human-review material; Deferred remains unavailable for factual drafting. Safety rules, editorial constraints, prohibited claims, and cultural/astrology/location/date-time qualifications reconcile to exact claim IDs.

## Determinism

`NarrationPlanningCanonicalizer` is shared by builder and validator for planning IDs, complete scene semantics, transitions, diagnostics, authority identity/checksum, and validation checksum. Semantically unordered sets and dictionaries are ordinally normalized while scene, governed reference, and visual synchronization order is preserved.

Focused correction tests cover the RC2 contract, typed model, English/Hindi constraint determinism and coherence, injected-policy construction, and provider-type isolation. Existing P7.1A and P7.1B-A regression suites remain the governing regression totals; the committed real-shape packet fixture is not yet present, so this document makes no fabricated 12 Long / 4 Short certification claim.

Azure OpenAI calls = **0**. Azure Speech calls = **0**. The foundation produces no narration prose, provider prompt, physical publication, transaction, manifest, validation JSON, audio, subtitle, image, or video output.

## Remaining work

P7.1B-BA does not implement publication transactions or Narration Draft Authority. Repository fixture certification for P7.1B-A remains external to this foundation. Future narration implementations must accept this planning authority as their sole upstream planning contract and must not bypass it to read Story Frames or Knowledge Authority.
