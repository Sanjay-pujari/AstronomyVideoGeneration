# Domain Architecture

A domain plugs into the platform by contributing knowledge, constraints, enrichment logic, validation rules, localization guidance, and asset preferences.

```mermaid
flowchart TD
    Domain[Domain] --> Knowledge[Knowledge]
    Knowledge --> Prompt[Prompt Enrichment]
    Prompt --> Validation[Validation]
    Validation --> Localization[Localization]
    Localization --> Assets[Assets]
```

## Extension model

| Extension point | Domain contribution | Platform responsibility |
| --- | --- | --- |
| Knowledge | Concepts, entities, facts, taxonomies, terminology | Store, retrieve, and inject context |
| Prompt enrichment | Tone, constraints, examples, forbidden patterns | Compose prompts consistently |
| Validation | Accuracy checks, domain warnings, compliance rules | Execute validators and report outcomes |
| Localization | Regional terms, cultural sensitivity, measurement conventions | Apply locale rules across assets |
| Assets | Preferred visuals, guide sections, narrative patterns | Generate assets using shared contracts |

## Plugin sequence

```mermaid
sequenceDiagram
    participant Platform
    participant DomainPlugin
    participant Knowledge
    participant Validator
    participant AssetEngine
    Platform->>DomainPlugin: Load domain capabilities
    DomainPlugin->>Knowledge: Provide topic context
    Knowledge-->>Platform: Return enriched domain model
    Platform->>DomainPlugin: Request prompt additions
    DomainPlugin-->>Platform: Return prompt rules
    Platform->>AssetEngine: Generate asset bundle
    AssetEngine->>Validator: Validate domain and asset fit
    Validator-->>Platform: Return quality report
```
