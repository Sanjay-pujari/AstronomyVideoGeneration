# Knowledge Catalog

The catalog uses `AstronomyKnowledgeCatalogEntryId`, `AstronomyKnowledgeCatalogEntryKind`, `AstronomyKnowledgeCatalogSnapshot`, `IAstronomyKnowledgeCatalogBuilder`, and `IAstronomyKnowledgeCatalog`. Entries are immutable metadata, not runtime statements. Lookup is exact by ID or section collections. Ordering is deterministic by kind/order/code/id. Conflicts are constructor/builder defects. Entry kinds are exactly Domain, PayloadFamily, KnowledgeType, ValidationRule, CrossDomainValidationRule, GraphValidationRule, and StatementKind. Representative exact entry: `KnowledgeType:typed.physical.properties.v1`.


## Verified inventories

### Built-in typed payload descriptors
| Knowledge type ID | Runtime payload type | Domain | Family | Registration source |
|---|---|---|---|---|
| typed.classification.entity.v1 | AstronomyEntityClassificationPayload | Classification | EntityClassification | AstronomyBuiltInTypedPayloadDescriptors.BuiltIn |
| typed.event.astronomical.v1 | AstronomyEventPayload | Event | AstronomicalEvent | AstronomyBuiltInTypedPayloadDescriptors.BuiltIn |
| typed.observational.conditions.v1 | AstronomyObservationConditionsPayload | Observational | ObservationCondition | AstronomyBuiltInTypedPayloadDescriptors.BuiltIn |
| typed.observational.visibility-windows.v1 | AstronomyVisibilityWindowsPayload | Observational | VisibilityWindow | AstronomyBuiltInTypedPayloadDescriptors.BuiltIn |
| typed.orbital.keplerian-elements.v1 | AstronomyKeplerianElementsPayload | Orbital | OrbitalParameter | AstronomyBuiltInTypedPayloadDescriptors.BuiltIn |
| typed.orbital.parameters.v1 | AstronomyOrbitalParametersPayload | Orbital | OrbitalParameter | AstronomyBuiltInTypedPayloadDescriptors.BuiltIn |
| typed.physical.properties.v1 | AstronomyPhysicalPropertiesPayload | Physical | PhysicalProperty | AstronomyBuiltInTypedPayloadDescriptors.BuiltIn |
| typed.positional.spatial-position.v1 | AstronomySpatialPositionPayload | Positional | SpatialPosition | AstronomyBuiltInTypedPayloadDescriptors.BuiltIn |
| typed.temporal.pattern.v1 | AstronomyTemporalPatternPayload | Temporal | TemporalCycle | AstronomyBuiltInTypedPayloadDescriptors.BuiltIn |

### Foundation/domain validation rules
| RuleId | Order | Responsibility | Issue-code source | Non-responsibilities |
|---|---:|---|---|---|
| classification.assignment.integrity | 100 | classification assignment structure | AstronomyClassificationValidationCodes | Cross-statement taxonomy inference |
| classification.assignment.uniqueness | 200 | duplicate assignments | AstronomyClassificationValidationCodes | Graph duplicate statements |
| classification.primary.cardinality | 300 | primary assignment count | AstronomyClassificationValidationCodes | Entity-reference existence |
| event.aggregate.integrity | 100 | event aggregate shape | AstronomyEventValidationCodes | Event prediction |
| event.temporal.extent | 200 | event temporal extent | AstronomyEventValidationCodes | Cross-domain temporal agreement |
| event.reference-context.compatibility | 300 | event reference context | AstronomyEventValidationCodes | External ephemeris checks |
| event.participants.identity | 400 | event participant identity | AstronomyEventValidationCodes | Graph reference reachability |
| event.phase-markers.identity | 500 | event phase markers | AstronomyEventValidationCodes | Media timing |
| event.geometry.catalog | 600 | event geometry catalog values | AstronomyEventValidationCodes | Astronomy calculation |
| event.circumstances.identity | 700 | event circumstance identity | AstronomyEventValidationCodes | Observing-site inference |
| observational.context.integrity | 100 | observation context | AstronomyObservationalValidationCodes | Visibility-window comparison |
| observational.conditions.integrity | 200 | observation condition combinations | AstronomyObservationalValidationCodes | Weather lookup |
| observational.quantity.integrity | 300 | observational quantities | AstronomyObservationalValidationCodes | Unit conversion service |
| observational.horizontal-coordinates | 400 | horizontal coordinate consistency | AstronomyObservationalValidationCodes | Position propagation |
| orbital.keplerian.reference-context | 100 | keplerian reference context | AstronomyOrbitalValidationCodes | Positional comparison |
| orbital.parameters.reference-context | 100 | orbital parameters reference context | AstronomyOrbitalValidationCodes | Positional comparison |
| orbital.keplerian.elements | 200 | keplerian element values | AstronomyOrbitalValidationCodes | Orbit solving |
| orbital.parameters.integrity | 200 | orbital parameter values | AstronomyOrbitalValidationCodes | Orbit solving |
| physical.property.identity | 100 | physical property identity | AstronomyPhysicalValidationCodes | Classification consistency |
| physical.property.value | 200 | physical property value | AstronomyPhysicalValidationCodes | Scientific truth scoring |
| physical.range.integrity | 300 | physical ranges | AstronomyPhysicalValidationCodes | Catalog lookup |
| positional.reference-context | 100 | spatial reference context | AstronomyPositionalValidationCodes | Orbital consistency |
| positional.position.integrity | 200 | position value presence | AstronomyPositionalValidationCodes | Ephemeris calculation |
| positional.angular.integrity | 300 | angular positions | AstronomyPositionalValidationCodes | Coordinate transforms |
| positional.spherical.integrity | 400 | spherical positions | AstronomyPositionalValidationCodes | Coordinate transforms |
| positional.cartesian.integrity | 500 | cartesian positions | AstronomyPositionalValidationCodes | Coordinate transforms |
| temporal.pattern.integrity | 100 | temporal pattern shape | AstronomyTemporalValidationCodes | Event agreement |
| temporal.reference-context.compatibility | 200 | temporal context | AstronomyTemporalValidationCodes | Current-time checks |
| temporal.recurrence.matrix | 300 | recurrence matrix | AstronomyTemporalValidationCodes | Calendar expansion |
| temporal.cycle-period.dimension | 400 | cycle period dimension | AstronomyTemporalValidationCodes | Runtime scheduling |
| temporal.anchor.runtime | 500 | temporal anchor | AstronomyTemporalValidationCodes | Clock access |
| temporal.phase.identity | 600 | phase identity | AstronomyTemporalValidationCodes | Animation timing |
| temporal.occurrence.identity | 700 | occurrence identity | AstronomyTemporalValidationCodes | Occurrence prediction |
| temporal.seasonal.policy | 800 | seasonal policy | AstronomyTemporalValidationCodes | Locale seasons |
| temporal.applicability.extent | 900 | applicability extent | AstronomyTemporalValidationCodes | Query filtering |
| visibility.context.integrity | 100 | visibility context | AstronomyVisibilityValidationCodes | Observation comparison |
| visibility.window.integrity | 200 | visibility windows | AstronomyVisibilityValidationCodes | Weather lookup |
| visibility.assessment.integrity | 300 | visibility assessments | AstronomyVisibilityValidationCodes | AI suitability scoring |
| visibility.peak.integrity | 400 | visibility peak | AstronomyVisibilityValidationCodes | Peak prediction |

### Cross-domain validation rules
| RuleId | Order | Responsibility | Issue codes | Non-responsibilities |
|---|---:|---|---|---|
| cross-domain.entity.consistency | 100 | Related item entity agreement | EntityMismatch | Graph reachability |
| cross-domain.classification.consistency | 200 | Classification relationships | ClassificationMismatch | Taxonomy inference |
| cross-domain.epoch.consistency | 300 | Epoch alignment | EpochMismatch | Time conversion |
| cross-domain.reference-context.consistency | 400 | Reference-context agreement | ReferenceContextMismatch | External frame resolution |
| cross-domain.measurement.consistency | 500 | Measurement dimensions | MeasurementDimensionMismatch | Unit conversion |
| cross-domain.orbital-positional.consistency | 600 | Orbital/positional pair scope | OrbitalPositionalMismatch | Orbit propagation |
| cross-domain.observation-visibility.consistency | 700 | Observation/visibility pair scope | ObservationVisibilityMismatch | Weather lookup |
| cross-domain.event-participant.consistency | 800 | Event participant relationship scope | EventParticipantMismatch | Graph reference integrity |
| cross-domain.event-temporal.consistency | 900 | Event/temporal relationship scope | EventTemporalMismatch | Event prediction |

### Graph validation rules
| RuleId | Order | Responsibility | Issue codes | Non-responsibilities |
|---|---:|---|---|---|
| graph.node.identity | 100 | node identity and duplicates | GraphNodeIdMissing, GraphNodeDuplicate | Entity creation |
| graph.statement.identity | 200 | statement identity and duplicates | GraphStatementIdMissing, GraphStatementDuplicate | Domain validation |
| graph.reference.integrity | 300 | statement/node relationship references | GraphReferenceMissing | External resolution |
| graph.payload.completeness | 400 | statement payload registration/completeness | GraphPayloadMissing, GraphPayloadTypeUnknown | Payload repair |
| graph.duplicate-knowledge.integrity | 500 | duplicate knowledge identity | GraphDuplicateKnowledge | Merge decisions |
| graph.provenance.integrity | 600 | provenance/audit graph policy | GraphProvenanceInvalid | Actor lookup |
| graph.version.consistency | 700 | version policy | GraphVersionInvalid | Migration |
| graph.cycle.integrity | 800 | forbidden cycles for hierarchy/dependency relationships | GraphCycleDetected | Cycles on non-forbidden kinds |
| graph.orphan.integrity | 900 | orphan nodes/statements | GraphOrphanNode, GraphOrphanStatement | Automatic deletion |
| graph.connectivity.integrity | 1000 | root reachability/connectivity | GraphDisconnectedComponent | Root selection |
| graph.repository.consistency | 1100 | repository metadata/root policy | GraphRepositoryRootMismatch | Persistence |

### Capability descriptors
| Capability ID | Kind | Code | Contract type | Implementation type | Lifetime | Order | Purpose |
|---|---|---|---|---|---|---:|---|
| typed-knowledge.registry | TypedKnowledge | typed-knowledge.registry | IAstronomyTypedPayloadRegistry | AstronomyTypedPayloadRegistry | Singleton | 100 | Built-in typed payload registry |
| validation.foundation | Validation | validation.registry | IAstronomyKnowledgeValidationRuleRegistry | AstronomyKnowledgeValidationRuleRegistry | Singleton | 100 | Domain rule registry |
| validation.typed-validator | Validation | validation.typed-validator | IAstronomyTypedKnowledgeValidator | AstronomyTypedKnowledgeValidator | Singleton | 200 | Domain validation orchestration |
| validation.cross-domain | CrossDomainValidation | validation.cross-domain.registry | IAstronomyCrossDomainValidationRuleRegistry | AstronomyCrossDomainValidationRuleRegistry | Singleton | 100 | Cross-domain rule registry |
| validation.cross-domain.validator | CrossDomainValidation | validation.cross-domain.validator | IAstronomyCrossDomainValidator | AstronomyCrossDomainValidator | Singleton | 200 | Cross-domain validation |
| validation.graph | GraphValidation | validation.graph.registry | IAstronomyKnowledgeGraphValidationRuleRegistry | AstronomyKnowledgeGraphValidationRuleRegistry | Singleton | 100 | Graph rule registry |
| validation.graph.validator | GraphValidation | validation.graph.validator | IAstronomyKnowledgeGraphValidator | AstronomyKnowledgeGraphValidator | Singleton | 200 | Graph validation |
| catalog.metadata | Catalog | catalog.metadata | IAstronomyKnowledgeCatalog | AstronomyKnowledgeCatalog | Singleton | 100 | Immutable catalog |
| catalog.builder | Catalog | catalog.builder | IAstronomyKnowledgeCatalogBuilder | AstronomyKnowledgeCatalogBuilder | Singleton | 200 | Catalog building |
| query.model | QueryModel | query.model.validator | IAstronomyKnowledgeQueryValidator | AstronomyKnowledgeQueryValidator | Singleton | 100 | Query validation |
| query.execution.catalog | QueryExecution | query.execution.catalog | IAstronomyKnowledgeCatalogQueryEngine | AstronomyKnowledgeCatalogQueryEngine | Singleton | 100 | Catalog query execution |
| query.execution.statement | QueryExecution | query.execution.statement | IAstronomyKnowledgeStatementQueryEngine | AstronomyKnowledgeStatementQueryEngine | Singleton | 200 | Statement query execution |
