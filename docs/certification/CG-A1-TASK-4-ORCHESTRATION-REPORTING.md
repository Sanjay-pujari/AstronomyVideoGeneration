# CG-A1 Task 4: Certification Orchestration and Reporting

Task 4 adds the production orchestration layer above the existing read-only CG-A1 certification engine. It does not change semantic resolution, projection, beat assignment, prompts, narration generation, story frames, media generation, or family profiles.

## Coordinator flow

`CertificationCoordinator` validates `FamilyCertificationContext`, resolves the family profile from `FamilyCertificationContext.EventType`, selects the requested phase range, orders certifiers by `IPhaseCertifier.PhaseNumber`, executes each selected certifier exactly once, writes each phase result, aggregates a run summary, and writes summary, dashboard, and Markdown artifacts.

## Phase ordering and selection

Requested phases must be in the inclusive range 1-7. Duplicate phase certifier registrations and missing requested certifiers fail deterministically. Certification never silently skips a requested phase.

## Failure handling

Certification failures are represented as result data. An unexpected non-cancellation phase exception is captured as blocking issue `CERT.PhaseExecutionException`, converted to a failed phase result, persisted, and later phases continue. `OperationCanceledException` propagates.

## Status aggregation

Structural, Semantic, and Quality statuses are aggregated independently. Structural fails on any applicable structural failure and warns on phase warnings. Semantic ignores non-applicable Phase 1-6 `NotEvaluated` values and uses actual semantic evaluations. Quality fails on any quality failure, preserves `NotEvaluated`, and is never upgraded to pass from missing evidence.

## Certification decision

Technical certification is based only on Structural and Semantic dimensions:

- `NotCertified` when Structural or Semantic fails.
- `NotEvaluated` when required technical certification cannot be evaluated.
- `CertifiedWithWarnings` when technical dimensions pass with warnings.
- `Certified` when Structural and Semantic pass without warnings.

## Publication decision

Publication combines Structural, Semantic, and Quality:

- `DoNotPublish` when any dimension fails or explicit do-not-publish diagnostics are present.
- `Publish` when all dimensions pass.
- `PublishWithWarnings` when no dimension fails and at least one applicable dimension warns.
- `ManualReview` when Structural and Semantic pass but Quality is not evaluated.
- `NotEvaluated` when technical certification itself is not evaluated.

## Output files

All output paths are centralized in `CertificationPathService` and written under `<output-root>/certification/`:

- `phase-XX-certification.json` for selected phases only.
- `certification-summary.json` as the authoritative machine-readable run summary.
- `certification-dashboard.json` for UI/API/CI dashboard consumption.
- `certification-report.md` for human review.

The schema version is `cg-a1-certification.v1`. Writes use UTF-8 without BOM and atomic temporary-file replacement.

## Dashboard schema

`CertificationDashboard` contains a header, identity block, deterministic status cards, phase timeline, issue summary, semantic lifecycle summary from Phase 7 facts, publication card, artifact links, generated timestamp, and schema version. Fact display names are resolved through `ISemanticFactCatalog`.

## Markdown report

The Markdown report includes run identity, overall decisions, a phase table, required semantic facts, blocking issues, warnings, and generated artifact links. It does not embed JSON bodies or stack traces.

## Pipeline integration

`PipelineOrchestrator` runs certification after production post-processing artifacts have been flushed and before the completed stage is recorded. Context values come from the live pipeline run/request and use EventType directly. Certification can be disabled without changing existing pipeline behavior.

## Configuration

The typed `CertificationOptions` section supports:

- `Certification:Enabled`
- `Certification:WriteMarkdownReport`
- `Certification:WriteDashboardJson`
- `Certification:FailPipelineOnCertificationFailure`

The default keeps certification disabled for non-breaking rollout, writes reports when invoked, and does not fail the outer pipeline for `NotCertified` unless configured.

## Idempotency

Re-running certification overwrites certification files atomically, writes one summary phase entry per selected phase, and writes only under `certification/`. Certification remains read-only for production artifacts.

## Concurrency

`CertificationOutputLock` serializes certification writes per output root without globally serializing unrelated plans. Locks are released reliably and removed from the lock dictionary after use.

## API/UI consumption

Consumers should use `certification-summary.json` for authoritative decisions and `certification-dashboard.json` for dashboard rendering. Markdown is for human inspection only.

## Operational troubleshooting

- Missing phase certifier: inspect DI registrations for all seven `IPhaseCertifier` services.
- Duplicate phase certifier: remove duplicate `PhaseNumber` registration.
- `NotCertified`: inspect `blockingIssues` in the summary.
- `ManualReview`: quality evidence is incomplete or ambiguous.
- Technical failure: inspect pipeline logs; ordinary certification result failures should still persist reports.
