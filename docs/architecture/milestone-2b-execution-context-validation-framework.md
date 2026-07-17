# Milestone 2B — Execution Context and Validation Framework

Milestone 2B adds a dormant, domain-neutral validation framework. It does not change production execution.

```text
DomainExecutionContract
    ↓
FamilyExecutionContract
    ↓
FamilyExecutionContext
    ↓
ExecutionValidationRequest
    ↓
ExecutionValidationPipeline
    ↓
Requirement Validators
    ↓
ExecutionValidationResult
```

## Purpose

The framework evaluates existing `FamilyExecutionContract` requirements against an immutable `FamilyExecutionContext` snapshot and returns structured validation data. It is intended for Milestone 2C shadow-mode validation.

## Execution context model

`FamilyExecutionContext` contains execution identity, family identity, optional format/language/region/time zone, explicit value observations, artifact observations, validation-rule observations, metadata, and `CreatedUtc`. Constructors preserve supplied timestamps and never call the clock. Dictionaries are normalized to immutable, case-insensitive snapshots. Keys are trimmed, empty keys are rejected, and duplicate keys differing only by case are rejected.

## Value observation model

`ExecutionValue` is explicit: validators read stable keys and `IsPresent`; no reflection, object traversal, JSON parsing, or serialization is performed. Numeric zero and boolean false are present when `IsPresent` is true. Null is missing only when supplied with `IsPresent=false`.

`ExecutionArtifactValue` is observation-only. It stores `ArtifactId`, path/content metadata, `Exists`, `IsNonEmpty`, and `ObservedCount`. It never calls file-system APIs.

`ExecutionRuleValue` stores supplied validation-rule outcomes. The rule validator consumes those outcomes and does not execute rule logic.

## Validation request

`ExecutionValidationRequest` carries `DomainExecutionContract`, `FamilyExecutionContract`, `FamilyExecutionContext`, boundary, optional/conditional flags, optional started timestamp, and metadata. Request construction permits identity mismatches so the pipeline can return structured diagnostics.

## Validator interface

`IExecutionRequirementValidator` exposes `ValidatorId`, `Boundary`, `CanValidate`, and synchronous `Validate`. Validators are stateless, deterministic, no-I/O components.

## Boundary model

Validators map to existing boundaries: input/pre-execution, semantic resolution, projection, artifact generation, and validation rules/post-execution. The validation-rule validator only evaluates rules for the requested boundary.

## Required / Optional / Conditional behavior

Required missing values produce blocking issues. Optional missing values are non-blocking and are only evaluated when `IncludeOptionalRequirements=true`. Conditional requirements use context metadata with keys `condition:<ConditionKey>` and values `true` or `false`, case-insensitive. When `EvaluateConditionalRequirements=false` or condition state is absent/invalid, the outcome is `NotEvaluated` with an informational issue. False conditions produce `ConditionalNotApplicable`.

## Blocking severity rules

Input, semantic, and projection required requirements use `Blocking`; optional and conditional diagnostics default to `Information` unless active and missing with required behavior. Validation-rule severity is preserved from the contract. Required artifacts are blocking; optional and diagnostic artifacts are informational.

## Artifact observation policy

Artifacts are looked up by `ArtifactId`. Cardinality rules are: `ExactlyOne` means count equals 1; `OneOrMore` means count is at least 1; `ZeroOrOne` means count is 0 or 1; `ZeroOrMore` always satisfies cardinality. `MustBeNonEmpty` emits `ArtifactEmpty` when the artifact exists but `IsNonEmpty=false`.

## Contract mismatch policy

The pipeline checks context domain, family, contract version, and domain-family membership before validators run. Domain and family comparisons are case-insensitive. Contract version comparison is ordinal/exact. Mismatches return `ContractMismatch` issues and do not throw.

## Deterministic ordering

The pipeline snapshots validators immutably, rejects duplicate validator IDs, and runs selected validators ordered by `ValidatorId`. Evaluations are ordered by boundary, requirement id, and source key. Issues are ordered by boundary, blocking severity first, requirement id, issue code, and message. Same controlled input and clock produce semantically identical results.

## No-I/O rule

Core validation does not reference Infrastructure, API, ASP.NET Core, dependency injection, production request types, semantic engines/adapters, mappers, or `System.IO` file operations.

## Dormant status

No DI registration, endpoint, orchestration, artifact generation, semantic resolution, or narration code invokes the pipeline. `ExecutionValidationPipelineFactory.CreateDefault()` is a dormant in-memory factory only.

## Planned Milestone 2C usage

Milestone 2C can build a shadow `FamilyExecutionContext`, resolve the selected dormant family contract, and call the pipeline without affecting production execution.

## Explicit non-goals

This milestone does not add family-specific contracts, fix family behavior, register validators, execute rules, perform file validation, create artifact manifests, resolve output paths, or parse arbitrary conditional expressions.
