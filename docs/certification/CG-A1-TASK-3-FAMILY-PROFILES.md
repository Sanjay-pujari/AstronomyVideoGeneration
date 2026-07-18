# CG-A1 Task 3 — Family Certification Profiles

Task 3 adds the first family-specific semantic certification layer for Phase 7 without changing runtime production behavior.

## Scope

Implemented families:

- `MeteorShower`
- `PlanetConjunction`

No coordinator, report writer, dashboard, pipeline integration, or certification JSON emission is added in Task 3.

## EventType profile selection

Certification profile selection is driven only by `FamilyCertificationContext.EventType`, which is populated from the production request EventType. `ContentStrategy` is not a profile-selection input. A request with `EventType = MeteorShower` and `ContentStrategy = LocalViewingGuide` resolves to the Meteor Shower profile.

## Meteor Shower profile

- Family ID: `MeteorShower`
- Aliases: `Meteor Shower`
- Canonical semantic value: `MeteorActivity`
- Required facts: `EventIdentity`, `EventWindow`, `ObservationDirection`, `MeteorActivity`, `DomainScientificKnowledge`
- Optional facts remain non-blocking and are left to existing runtime diagnostics.
- Forbidden leakage checks detect planet-conjunction terms in approved user-facing fields.
- Story roles: `Hook`, `Orientation`, `Timing`, `Observation`, `Science`, `Closing`
- Beat coverage maps required facts to the existing story roles that can legitimately carry them.

## Planet Conjunction profile

- Family ID: `PlanetConjunction`
- Aliases: `PlanetPairing`, `PlanetaryConjunction`, `PLANET_CONJUNCTION`, `PLANET_PAIRING`
- Canonical semantic value: `PlanetPairing`
- Required facts: `EventIdentity`, `AstronomicalObjects`, `EventWindow`, `DomainScientificKnowledge`
- Angular separation remains non-blocking because it is optional in the active profile contract.
- Forbidden leakage checks detect meteor-shower terms in approved user-facing fields.
- Story roles: `Hook`, `Orientation`, `Timing`, `Observation`, `Science`, `Closing`

## Semantic lifecycle evidence

`SemanticCertificationEvidenceReader` reads existing Phase 7 artifacts only. It does not execute semantic resolution or rebuild canonical values. It normalizes evidence from available diagnostics and narration artifacts, including:

- event identity diagnostics;
- family compatibility diagnostics;
- semantic registry/capability diagnostics;
- required semantic fact diagnostics;
- meteor shadow validation when present;
- narration context;
- narration plan and briefs;
- scene fact cards;
- documentary scripts;
- long and short narration;
- runtime and validation diagnostics;
- `validation/phase-07-validation.json`.

For each required fact the reader records resolution, projection, retention, beat assignment, narration evidence, source adapter/path, confidence, beats, scenes, and diagnostics when available.

## Forbidden leakage

`ForbiddenConceptValidator` scans only approved user-facing fields such as narration text, script text, briefs, scene fact-card values, titles, summaries, and editorial text. It does not scan paths, filenames, adapter IDs, registry IDs, type names, or diagnostic property names. English and selected Hindi/mixed-language terms are included; broader localization of forbidden terms remains future work.

## Story-role and beat coverage

`StoryBeatCoverageValidator` validates declared story roles and required fact beat assignments with structured beat IDs/roles rather than literal English sentence matching. Missing required roles and missing/disallowed beat assignments become Phase 7 semantic issues.

## Quality separation

Phase 7 now returns independent Structural, Semantic, and Quality statuses. Quality status is read from existing quality/validation diagnostics and can fail independently of semantic certification. For example, a Geminids artifact set can produce Structural = Passed, Semantic = Passed, Quality = Failed.

## English/Hindi parity

Profiles expose the same family IDs, canonical semantic values, required fact IDs, story roles, and beat coverage for English and Hindi. Only the narration evidence text differs by language.

## Adding the next family

To add another family:

1. Add one `IFamilyCertificationProfile` implementation.
2. Use the existing EventType aliases only.
3. Point `CanonicalSemanticValueId` to an existing semantic capability/model.
4. Declare only facts required by the active runtime family profile/contract.
5. Add forbidden leakage terms scoped to approved user-facing fields.
6. Add story/beat coverage using structured IDs.
7. Register the profile in DI.
8. Add language parity and lifecycle tests.

## Task 4 remains

Task 4 can add coordinator/report writing, certification summary artifacts, dashboard/report generation, and pipeline integration. Those features are intentionally not implemented here.
