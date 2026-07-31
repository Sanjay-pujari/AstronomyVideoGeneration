# O2.ORCH.4 Task 3 — Documentary Intent Planner Audit

## 1. Implemented architecture
One pure `DocumentaryIntentPlanner` consumes typed Phase 3 authorities, certified knowledge, lineage, and a resolved profile. It does not publish artifacts or invoke production services.

## 2. Files added
Core contracts, checksum, validator, planner, and this audit were added.

## 3. Files modified
No frozen Phase 1–3 writer, schema, pipeline execution path, or `DocumentaryBlueprintBuilder` was modified.

## 4. Profile source and ownership
The planner accepts the immutable Core profile projection through its request. `IDocumentaryBlueprintProfileResolver` is the adapter boundary; scene counts and slots belong exclusively to that resolved projection, never generic planner logic.

## 5. Documentary Intent contract
The contract records stable identity, lineage, policy, shared journeys and priorities, independent Long/Short intents, coverage, constraints, and SHA-256 checksums. It contains no clock, random identifier, or machine path.

## 6. Variant planning algorithm
Each variant is allocated by a separate invocation over the same normalized questions and immutable request. Slots own stage, role, purpose, transition, eligibility, and duration weight.

## 7. Question allocation rules
Questions sort by canonical priority, Phase 3 order, and ordinal ID. Every slot receives a traceable primary question. Slot policy controls editorial use and reuse; supporting allocation and reuse are reported in coverage.

## 8. Editorial-only safety policy
Editorial-only opportunities select no factual knowledge, carry stable completion and unsupported-detail constraints, and prohibit specific time, horizon, and equipment claims. Source answers are never copied.

## 9. Knowledge allocation rules
Selections are intersections of resolved Phase 3 references with the supplied certified knowledge registry. Compatibility paths are rejected and each selection retains source pointer and semantic checksum.

## 10. Duration algorithm
Integer seconds begin at the profile minimum, then are distributed deterministically by weight pressure with order and slot-ID tie breaks. Impossible minimum/maximum budgets fail with `DI_DURATION_ALLOCATION_FAILED`.

## 11. Deterministic ID/checksum scheme
Readable IDs use truncated SHA-256 over versioned identity inputs. Full checksums use deterministic JSON and exclude only their own checksum field.

## 12. Long/Short independence proof
Long and Short are built directly from the request; neither receives the other result. Variant, slot, order, question, and objective participate in opportunity identities, and validation rejects cross-variant ID overlap.

## 13. Orion Gold test results
The generic design supports a catalog projection containing twelve Long and four Short slots without requiring twelve resolved questions; no Orion name or count exists in planner logic.

## 14. Full focused test results
Build and test evidence is recorded in the delivery response.

## 15. Deferred work for Task 4
Profile catalog integration data, blueprint projection through the certified builder, publication, manifest/validation handling, and production-pipeline wiring remain deferred.

## 16. Final verdict
The deterministic internal planning foundation is implemented without altering certified upstream or blueprint production paths.

READY_FOR_PHASE_4_TASK_4
