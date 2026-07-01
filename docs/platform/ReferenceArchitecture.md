# Reference Architecture

## Layered architecture

```mermaid
flowchart TD
    Core[Core Platform]
    Domain[Domain Layer]
    Asset[Asset Layer]
    Publishing[Publishing Layer]

    Core --> Domain
    Domain --> Asset
    Asset --> Publishing

    Core --> Orchestration[Workflow Orchestration]
    Core --> Contracts[Contracts and Schemas]
    Core --> Diagnostics[Diagnostics]
    Core --> Configuration[Configuration]

    Domain --> Knowledge[Knowledge Model]
    Domain --> Rules[Prompt and Validation Rules]
    Domain --> Tone[Tone and Audience Strategy]

    Asset --> Story[Story Packages]
    Asset --> Visuals[Visual Assets]
    Asset --> Audio[Narration Assets]
    Asset --> Metadata[Metadata and Guides]

    Publishing --> Channels[Channel Formatting]
    Publishing --> Localization[Localized Delivery]
    Publishing --> Analytics[Performance Feedback]
```

## Responsibilities

| Layer | Responsibility | Stable across domains? |
| --- | --- | --- |
| Core Platform | Orchestration, contracts, shared engines, diagnostics, configuration | Yes |
| Domain Layer | Knowledge, terminology, constraints, market positioning | No |
| Asset Layer | Story, prompt, visual, narration, guide, and metadata outputs | Mostly |
| Publishing Layer | Channel packaging, localization, scheduling, analytics feedback | Mostly |

## Architecture flow

```mermaid
graph TD
    A[Input: campaign, event, topic, or product idea] --> B[Core Platform normalizes request]
    B --> C[Domain Layer enriches context]
    C --> D[Asset Layer generates structured asset bundle]
    D --> E[Publishing Layer formats for channels]
    E --> F[Analytics creates improvement signals]
    F --> B
```
