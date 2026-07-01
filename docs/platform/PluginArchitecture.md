# Plugin Architecture

The plugin model allows domains to extend the platform without forcing the platform core to depend on domain-specific concepts.

```mermaid
flowchart TD
    Platform[Platform] --> Plugin[Plugin]
    Plugin --> Services[Domain Services]
    Services --> PromptRules[Prompt Rules]
    PromptRules --> ValidationRules[Validation Rules]
    ValidationRules --> Localization[Localization]
    Localization --> Publishing[Publishing]
```

## Dependency inversion

The platform defines contracts. Plugins implement domain behavior behind those contracts. This keeps the platform stable while allowing domains to evolve independently.

```mermaid
graph TD
    Core[Platform Core] --> Interfaces[Domain Interfaces]
    Astronomy[Astronomy Plugin] --> Interfaces
    Travel[Travel Plugin] --> Interfaces
    Finance[Finance Plugin] --> Interfaces
    Interfaces --> Runtime[Runtime Composition]
```

## Plugin responsibilities

| Plugin capability | Description |
| --- | --- |
| Domain services | Provide knowledge lookup, terminology, content constraints, and enrichment. |
| Prompt rules | Add domain tone, examples, constraints, and prohibited output patterns. |
| Validation rules | Check factual fit, domain safety, completeness, and audience appropriateness. |
| Localization rules | Adapt domain language, measurements, cultural references, and disclaimers. |
| Publishing rules | Shape channel packaging, metadata, cadence, and audience targeting. |

## Runtime composition

```mermaid
sequenceDiagram
    participant Core as Platform Core
    participant Registry as Plugin Registry
    participant Plugin as Domain Plugin
    participant Engines as Shared Engines
    Core->>Registry: Resolve domain plugin
    Registry-->>Core: Return plugin capabilities
    Core->>Plugin: Request domain context
    Plugin-->>Core: Return knowledge and rules
    Core->>Engines: Run generation using shared contracts
    Engines-->>Core: Return asset bundle
    Core->>Plugin: Request domain validation and publishing guidance
    Plugin-->>Core: Return approvals and channel guidance
```
