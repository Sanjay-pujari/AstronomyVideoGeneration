# O2.ORCH.4 Task 2 — Phase 6 hardening audit

| Concern | Current implementation | Risk found | Change required | Test required |
|---|---|---|---|---|
| Validation diagnostics | `StoryFrameArtifactValidator` returned prose strings | Callers could not reliably classify corruption | One structured validation engine with stable error codes and a legacy string projection | Error-code and corruption mutation tests |
| Contract compatibility | Artifacts had no explicit contract version | Valid checksums could resume an incompatible shape | Additive authority, index, and diagnostics versions; accept supported 1.0–1.1 only | Current, older minor, old/future major tests |
| Identity and checksums | Identity matching and checksum equality existed | Format, future timestamps, and execution ownership were unchecked | Validate required identity, composed authority ID, timestamp, and lowercase SHA-256 format | Independent identity/checksum mutations |
| Variants and timing | Order/duplicates and positive timings checked | Unknown variants and non-finite doubles could pass | Canonical `Long`/`Short` set and finite/overflow-safe timing | Case, unknown, NaN/infinity tests |
| Narration boundary | Builder supplied ownership metadata | Narration/SSML/audio payload could leak into Phase 6 | Reject narration markers and require Phase 7 ownership | Leakage matrix |
| Downstream readiness | Validation errors were interpreted by orchestration | No explicit consumer eligibility result | Project detailed validation into `StoryFrameDownstreamReadiness` | Valid and corrupt complete-set tests |
| Concurrency | Random staging names prevented collision | Same plan could race active-directory replacement | Plan/output scoped keyed semaphore with cancellation-safe release | Same/different plan, failure, cancellation tests |
| Manifest containment | Full path plus directory/name validation existed | Plain prefix containment was brittle | Retain exact directory/name validation and reject staging/backup; canonicalize root | Traversal/root/role/duplicate cases |
| Atomic replacement | Staging, reread, validation, backup restore existed | Failure coverage remains an operational concern | Preserve single architecture and recovery ordering | Failure-injection matrix |
| Phase 3–5 lineage | Phase 5 complete-set validator is called before Phase 6 | Duplication would allow rule drift | Continue reusing Phase 5 validator | Phase 3–5 regressions |
