# Constellation Orion Sprint 1

## Baseline
- `dotnet restore Backend/Astronomy.MediaFactory.slnx`: blocked in this container because `dotnet` is not installed.
- Complete build/test are likewise blocked by the missing .NET SDK before code changes.

## Repository inspection summary
Existing production family integration is split across legacy event-family thumbnail/gallery dispatch, CG-A2 domain primitives, Phase 7 semantic V1 family profiles, execution contracts, certification, and persistence-backed `ContentGenerationPlan`. MeteorShower remains the strongest reference for source-attributed family facts and production validation. PlanetPairing/PlanetGrouping are closest for object lists and angular geometry, NamedFullMoon for evergreen-ish naming/cultural editorial material, and SolarEclipse for CG-A1 blocking certification rules.

## Actual phase map discovered
`Rc2PipelinePhaseRegistry` declares `PracticalPhaseCount = 21` but only names phases 1-8 in source. CG-A1 certifiers are implemented only for phases 1-7. Therefore repository source does not currently provide an executable, named Phase 9-20 certification surface.

| Phase | Actual name | Coordinator/service discovered | Required inputs | Produced outputs | Persisted state | Family extension point | Validation/certification | Transient assumption |
|---:|---|---|---|---|---|---|---|---|
| 1 | Run Setup / Plan Selection | `Rc2ContentPlanningBatchOrchestrator`; `Phase1Certifier` | `ContentGenerationPlan`, plan id, language, region | setup/selection artifacts | phase validation JSON, manifest phase status | family resolver via event type | plan/event/language/region checks | no, but region required |
| 2 | Domain Intelligence | production-event-intelligence services; `Phase2Certifier` | plan and astronomy event type | `plan-input/production-event-intelligence.json`; constellation knowledge JSON for Orion | phase validation JSON | knowledge provider/catalog | event-type/source/status checks | partly; event intelligence naming is transient-oriented |
| 3 | Question / Story Planning | question engine; `Phase3Certifier` | event intelligence and topic plan | question-answer set | phase validation JSON | semantic capabilities/profile | non-empty questions/answers | no |
| 4 | Story Intelligence | `SceneIntentBuilder` story graph; `Phase4Certifier` | Q/A set | story graph | phase validation JSON | family narrative beats | graph/node validation | no |
| 5 | Editorial Intelligence | `SceneIntentBuilder`; `Phase5Certifier` | story graph and facts | observation metadata, scene intents, editorial contract | phase validation JSON | required facts and beat allocation | duplicate/missing facts | partly; observation metadata may assume observing windows |
| 6 | Creative Intelligence / Story Frames | `CreativeStoryboardBuilder`; `Phase6Certifier` | editorial contract | long/short story frames and manifests | phase validation JSON | family creative profile | scene/manifest checks | no |
| 7 | Narration Studio V5 | `NarrationGeneratorV5`; `Phase7Certifier` | story frames, semantic profile, language | narration contracts, semantic diagnostics | phase validation JSON and semantic evidence | `AstronomyFamilyProfileCatalogV1` | required fact projection/retention/narration evidence | no for Constellation profile; MeteorShower has event-specific shadow checks |
| 8 | Format-Aware Scene Asset Generation | RC2 overlay registry entry | narration/story frame artifacts | format-aware scene asset generation outputs | phase validation JSON | event-family/visual profile | orchestrator output-file validation | no obvious hard requirement |
| 9 | Not named in source | not found | not found | not found | generic manifest may accept number | unknown | no CG-A1 certifier | unknown |
| 10 | Not named in source | not found | not found | not found | generic manifest may accept number | unknown | no CG-A1 certifier | unknown |
| 11 | Not named in source | not found | not found | not found | generic manifest may accept number | unknown | no CG-A1 certifier | unknown |
| 12 | Not named in source | not found | not found | not found | generic manifest may accept number | unknown | no CG-A1 certifier | unknown |
| 13 | Not named in source | gallery/thumbnail services reference phase 13 diagnostics | gallery context | gallery PNGs and guides | service diagnostics | EventFamilyProfiles | service validation JSON | observation display may assume dates/times |
| 14 | Not named in source | not found | not found | not found | generic manifest may accept number | unknown | no CG-A1 certifier | unknown |
| 15 | Not named in source | not found | not found | not found | generic manifest may accept number | unknown | no CG-A1 certifier | unknown |
| 16 | Not named in source | not found | not found | not found | generic manifest may accept number | unknown | no CG-A1 certifier | unknown |
| 17 | Not named in source | not found | not found | not found | generic manifest may accept number | unknown | no CG-A1 certifier | unknown |
| 18 | Not named in source | docs mention pre-publish validation | final media | validation/package diagnostics | unknown | certification intended | no CG-A1 certifier | unknown |
| 19 | Not named in source | `ContentPlanBatchGeneration` has Phase19 review flags | review state | review approval | plan/batch flags | none found | no CG-A1 certifier | unknown |
| 20 | Not named in source | not found | not found | not found | generic manifest may accept number | unknown | no CG-A1 certifier | unknown |

## Reference-family comparison
| Concern | Closest existing pattern | Reuse decision |
|---|---|---|
| family registration | CG-A2 family registry + V1 semantic family profile | Add only `CONSTELLATION` domain family and aliases. |
| plan creation | existing `ContentGenerationPlan` persistence | Add one controlled Orion seed fixture; no table redesign. |
| semantic knowledge | MeteorShower catalog | Add curated Orion provider, no live fetch. |
| location dependence | NamedFullMoon/general guides | General hemispheric guidance only. |
| time dependence | NamedFullMoon seasonal text | Seasonal visibility, no event window requirement. |
| multilingual content | existing narration pipeline | No new multilingual engine. |
| visuals | SpecialEvent constellation thumbnail profile | Promote constellation to first-class profile. |
| narration | V1 reference `Constellation` profile | Use ObjectKnowledge required fact. |
| certification | CG-A1 family profile registry | Add Constellation profile/facts. |
| artifacts | MeteorShower additional artifact pattern | Require constellation knowledge artifact in Phase 2. |

## Orion plan and knowledge
The Orion plan fixture uses `PrimaryAstronomyEventTypeCode = CONSTELLATION`, `ContentCategoryCode = AstronomyEducation`, no `ContentStrategy`, and no artificial scheduled peak time. Orion knowledge covers IAU identity/abbreviation, principal stars, M42/Horsehead observing guidance, scientific significance, classical cultural tradition, beginner recognition, and visual constraints with controlled IAU/NASA references.

## Phase execution status
Full Phase 1-20 execution is blocked by environment and repository capability gaps: the container has no `dotnet`, source names only phases 1-8, and CG-A1 certifies only 1-7. No phase was falsely marked passed.

## Sprint result
BLOCKED — exact blockers: .NET SDK unavailable in the execution container; repository source does not define executable/named Phase 9-20 sequence or CG-A1 certifiers for phases 8-20.
