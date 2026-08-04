# P7.1B-BB narration-planning publication

## Architecture and artifacts

P7.1B-BB is a provider-independent physical-publication boundary. It consumes the frozen
`IPhase7NarrationPlanningInputAuthorityEvaluator`, builder, and validator rather than reconstructing
their join. It publishes only planning metadata—never narration prose or a provider prompt—to:

* `07-narration/planning/narration-planning-authority.json`
* `07-narration/planning/narration-planning-diagnostics.json`
* `07-narration/planning/narration-planning-report.json`
* `validation/phase-07-narration-planning-validation.json`
* `phase-manifest.json` (the `phase7NarrationPlanningAuthorities` extension)
* `.phase-07-narration-planning-publication.json`

The report, validation, manifest entry, and publication evidence have deterministic checksums. The
manifest extension leaves the P7.1A entry and every unrelated manifest property untouched.

## Transaction, lock, rollback, and recovery

The coordinator obtains `<PlanId>.phase-07-narration-planning.lock`, recovers stale state, evaluates
committed inputs, builds and validates in memory, and writes a complete candidate beneath
`07-narration/.planning-staging-<token>`. It deserializes the candidate before changing stable state.
The prior planning directory and external evidence are retained in
`07-narration/.planning-backup-<token>` until committed deserialization succeeds. Planning is swapped,
then validation, then the manifest, and publication evidence is moved last. Any exception restores
all prior paths. Successful publication removes staging, backup, and lock state before final physical
committed-state evaluation. Recovery deletes an uncommitted stale staging directory and restores a
backup only where its stable planning directory is absent; it never deletes the sole known-good copy.

Physical readback deserializes the six governed files, calculates SHA-256 hashes, reports a typed
inventory, rejects transaction residue, and feeds the committed-state evaluator. The evaluator also
recomputes authority, diagnostics, report, validation, manifest-entry, and publication-evidence
checksums; binds identities across the files; requires committed evidence and passing gates; and
compares current packet, language, and profile lineage.

## Reuse precedence

1. `overwriteExisting=true` always bypasses reuse and performs the full build and transaction.
2. Otherwise, a complete valid committed state is reused, regardless of `retryFailedOnly`.
3. A missing, invalid, or stale state is rebuilt. Thus `retryFailedOnly=true` retries only such a
   state; it never needlessly overwrites a valid authority.

Changing the Phase 6/story-frame checksum, P7.1A knowledge checksum, packet-collection checksum,
language, profile, or runtime identity makes committed evaluation fail and prevents stale reuse.

## Pipeline and DI boundary

The publication service is a scoped, explicit P7.1B-BB boundary intended after P7.1A, P7.1B-A, and
P7.1B-BA. It does not alter the earlier boundaries and does not invoke Narration Draft Authority.
Filesystem, lock, recovery, and readback helpers are singleton; the committed evaluator, transaction
coordinator, and publication service are scoped, avoiding captive scoped dependencies. No
provider-facing dependency is injected.

## Verification record

The container used for this implementation does not include the .NET SDK, so test totals could not
be truthfully certified here. Focused, P7.1A, P7.1B-A, P7.1B-BA, and broader Phase 7 totals remain an
external CI milestone. This document does **not** claim real 12 Long / 4 Short fixture certification.

| Prohibited side effect | Calls / outputs |
|---|---:|
| Azure OpenAI | 0 |
| Azure Speech | 0 |
| Prompt composer | 0 |
| Narration generator | 0 |
| Narration prose | none |
| TTS, SRT, audio, image, or video | none |
