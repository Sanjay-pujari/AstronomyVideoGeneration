# Milestone 2C-B — Meteor Shower Context Builder

## Purpose
Milestone 2C-B adds an observation-only bridge from production state to an immutable `FamilyExecutionContext` for the Meteor Shower execution contract. The builder does not validate, execute production logic, call semantic services, invoke adapters, perform catalog lookup, perform file I/O, or repair missing data.

## Observation model
`MeteorShowerProductionObservation` is an immutable snapshot containing only contract-relevant observations: execution identity, observed time, content strategy, event identity/window, observer location, language, format, local viewing guide, canonical meteor activity/radiant/peak-window observations, projected facts, validation-rule observations, and flat metadata. Observed values carry value type, source id, evidence, and metadata without exposing production services or mutable runtime object graphs.

## Input mapping
`MeteorShowerExecutionContextBuilder` populates `InputValues` with stable keys from `MeteorShowerExecutionKeys.Inputs`. It maps only observed input values. Numeric zero and boolean false are present; null values are absent; whitespace-only text is absent for input values where absence is appropriate. `ContentStrategy` is preserved exactly when present.

## Semantic mapping
`SemanticValues` represent canonical semantic capability availability only. Request data, typed request fields, and earlier-stage values do not cause `MeteorActivity`, `Radiant`, or `PeakWindow` to be marked present. Earlier-stage evidence can be retained in metadata or value evidence, but only canonical observations populate semantic keys.

## Projection mapping
`ProjectionValues` are populated only from observed projected facts using `MeteorShowerExecutionKeys.Projection.RadiantFact` and `MeteorShowerExecutionKeys.Projection.PeakWindowFact`. The builder does not infer projections from semantic values.

## Rule mapping
`ValidationRuleValues` are copied from observed rule values for `familyStrategyConsistency`, `activityObserved`, `semanticLifecycleComplete`, and `requiredFactsRetained` when those observations exist. The builder does not execute rule logic.

## Canonical vs request values
The observation model differentiates request values (inputs), typed values (observed value payloads and value type metadata), and canonical semantic values (`ObservedMeteorActivity`, `ObservedRadiant`, and `ObservedPeakWindow`). This prevents request-only Meteor Shower data from manufacturing canonical semantic availability.

## Evidence policy
Evidence, source ids, diagnostic hints, and flat execution metadata are preserved on `ExecutionValue`, `ExecutionRuleValue`, and context metadata. `LocalViewingGuide` is never normalized; when supplied it is retained exactly in metadata rather than transformed into a semantic value.

## Known limitations
The builder is not integrated into production and is not a shadow runner. It assumes callers have already produced safe immutable observations. It does not inspect production object graphs, does not resolve missing facts, and does not determine whether rule observations are correct.

## Preparation for 2C-C
2C-C can use this builder as the context-construction boundary before invoking shadow validation. Since the output is an immutable `FamilyExecutionContext` using stable contract keys, validation can be introduced later without changing production behavior or the Meteor Shower semantic lifecycle.
