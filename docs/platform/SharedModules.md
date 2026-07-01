# Shared Modules

## Reuse categories

```mermaid
graph TD
    Modules[Platform Modules] --> Reusable[Reusable]
    Modules --> DomainSpecific[Domain-specific]
    Modules --> AssetSpecific[Asset-specific]

    Reusable --> Orchestration[Orchestration]
    Reusable --> Contracts[Asset Contracts]
    Reusable --> Diagnostics[Diagnostics]
    Reusable --> Configuration[Configuration]
    Reusable --> Localization[Localization Framework]
    Reusable --> Publishing[Publishing Framework]

    DomainSpecific --> Knowledge[Knowledge Models]
    DomainSpecific --> Terminology[Terminology]
    DomainSpecific --> Rules[Validation Rules]
    DomainSpecific --> Voice[Audience Voice]

    AssetSpecific --> Thumbnail[Thumbnail Layout Rules]
    AssetSpecific --> Hero[Hero Composition Rules]
    AssetSpecific --> Narration[Narration Style]
    AssetSpecific --> Guide[Guide Format]
```

## Classification table

| Capability | Reusable | Domain-specific | Asset-specific |
| --- | --- | --- | --- |
| Workflow orchestration | Yes | No | No |
| Story framework | Yes | Narrative patterns | Story format variants |
| Blueprint framework | Yes | Domain production sections | Asset package sections |
| Prompt framework | Yes | Knowledge and terminology | Asset prompt goals |
| Validation framework | Yes | Accuracy and safety rules | Asset completeness rules |
| Localization framework | Yes | Cultural assumptions | Asset text length and tone |
| Rendering framework | Yes | Visual vocabulary | Renderer and layout choices |
| Publishing framework | Yes | Channel strategy | Channel asset requirements |
| Diagnostics | Yes | Domain quality dimensions | Asset failure categories |
| Configuration | Yes | Domain configuration | Asset presets |

## Design principle

Shared modules should know how to run a generation workflow. Domain modules should know what is true and appropriate. Asset modules should know what must be produced.
