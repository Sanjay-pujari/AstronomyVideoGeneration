# Milestone 2C-A Meteor Shower Execution Contract

## Purpose

Milestone 2C-A introduces the first real `FamilyExecutionContract` in the Astronomy contract catalog: `MeteorShower`. The contract is declarative shadow-mode metadata only. It describes execution requirements that production already exposes without invoking production code, semantic resolution, projection logic, adapters, dependency injection, file I/O, JSON parsing, or runtime services.

## Contract hierarchy

- Domain: `Astronomy`
- Domain contract version: `AstronomyExecutionContracts-v1`
- Family: `MeteorShower`
- Family contract version: `MeteorShowerExecutionContract-v1`
- Catalog metadata:
  - `frameworkMilestone=2C`
  - `validationMode=shadow`
  - `runtimeAuthority=production`
- Family metadata:
  - `frameworkMilestone=2C`
  - `validationMode=shadow`
  - `contractAuthority=production`
  - `contractRevision=2C-A`

## Stable keys

`MeteorShowerExecutionKeys` is the single source of truth for Meteor Shower contract strings.

### Inputs

- `eventIdentity`
- `eventStart`
- `eventEnd`
- `observerLocation`
- `language`
- `format`
- `contentStrategy`

### Semantic

- `MeteorActivity`
- `Radiant`
- `PeakWindow`

### Projection

- `RadiantFact`
- `PeakWindowFact`
- `V1Projection.MeteorActivity.Radiant`
- `V1Projection.MeteorActivity.PeakWindow`

### Artifacts

No artifact keys are required in 2C-A because artifact availability at validation time is intentionally not assumed.

### Rules

- `meteor.rule.familyStrategyConsistency`
- `meteor.rule.activityObserved`
- `meteor.rule.semanticLifecycleComplete`
- `meteor.rule.requiredFactsRetained`

### Conditions

- `meteor.condition.localizedOutput`
- `meteor.condition.multiFormatOutput`

## Requirement inventory

### Input requirements

Required:

- `meteor.input.eventIdentity` → `eventIdentity`
- `meteor.input.eventStart` → `eventStart`
- `meteor.input.eventEnd` → `eventEnd`
- `meteor.input.observerLocation` → `observerLocation`
- `meteor.input.language` → `language`

Optional:

- `meteor.input.format` → `format`
- `meteor.input.contentStrategy` → `contentStrategy`

The contract observes `contentStrategy` but does not require a specific content-strategy literal.

### Semantic requirements

Required:

- `meteor.semantic.meteorActivity` → `MeteorActivity`
- `meteor.semantic.radiant` → `Radiant`
- `meteor.semantic.peakWindow` → `PeakWindow`

### Projection requirements

Required:

- `meteor.projection.radiantFact` maps contract-owned `MeteorActivity` to `RadiantFact` through the observed compatibility projection rule id `V1Projection.MeteorActivity.Radiant`.
- `meteor.projection.peakWindowFact` maps contract-owned `MeteorActivity` to `PeakWindowFact` through the observed compatibility projection rule id `V1Projection.MeteorActivity.PeakWindow`.

### Artifact requirements

None. Meteor lifecycle diagnostics and generated narration artifacts are omitted because 2C-A must not require files that may be unavailable at validation time.

## Validation rule inventory

- `meteor.rule.familyStrategyConsistency`: shadow warning that family identity and strategy observations should be consistent without encoding the current content-strategy bug.
- `meteor.rule.activityObserved`: blocking semantic-resolution observation that `MeteorActivity` exists before projection.
- `meteor.rule.semanticLifecycleComplete`: blocking post-execution observation that semantic source, resolution, projection, and retention observations are complete.
- `meteor.rule.requiredFactsRetained`: blocking post-execution observation that required radiant and peak-window facts were retained when observable.

## Known production divergence

Production discovery found a Meteor Shower divergence where realistic requests can use `ContentStrategy=LocalViewingGuide`, while current MeteorActivity derivation is gated on `ContentStrategy == "MeteorShower"`. This contract intentionally does not encode that brittle literal. It declares family-strategy consistency as a shadow validation rule for later observation.

## Non-goals

This milestone does not:

- Build `ExecutionContext`.
- Build observation objects.
- Execute validation.
- Modify `NarrationGeneratorV5`.
- Modify `BuildMeteorActivityFromRequest`.
- Modify `ReadMeteorActivity`.
- Integrate into production behavior.
- Create a shadow runner.
- Change semantic resolution, adapters, projections, DI, or configuration.

## Milestone 2B relationship

Milestone 2B provides the validation framework, validators, execution context, and validation pipeline. The Meteor Shower contract is compatible with those types, but this milestone does not instantiate contexts or run validation in production.

## Milestone 2C-B preparation

2C-A supplies the immutable catalog contract and stable keys needed by 2C-B. A later milestone can map production observations into `ExecutionContext` and evaluate these requirements in shadow mode without changing the contract vocabulary.
