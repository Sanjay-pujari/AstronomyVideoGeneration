# Domain Lifecycle

The lifecycle describes how a domain idea becomes repeatable, validated, publishable content.

```mermaid
flowchart TD
    Idea[Idea] --> Analysis[Domain Analysis]
    Analysis --> Knowledge[Knowledge Model]
    Knowledge --> Story[Story]
    Story --> Blueprint[Blueprint]
    Blueprint --> Prompt[Prompt]
    Prompt --> Assets[Assets]
    Assets --> Publishing[Publishing]
    Publishing --> Analytics[Analytics]
    Analytics --> Analysis
```

## Lifecycle states

```mermaid
stateDiagram-v2
    [*] --> Candidate
    Candidate --> Analyzed
    Analyzed --> Modeled
    Modeled --> Pilot
    Pilot --> Production
    Production --> Optimized
    Optimized --> Expanded
    Expanded --> Production
```

## Stage descriptions

| Stage | Purpose | Outcome |
| --- | --- | --- |
| Idea | Identify a commercially or strategically valuable domain. | Domain hypothesis. |
| Domain Analysis | Define audience, competitors, content patterns, risks, and monetization. | Domain brief. |
| Knowledge Model | Capture entities, terminology, facts, taxonomies, and constraints. | Structured domain context. |
| Story | Define narrative patterns and audience journeys. | Reusable story rules. |
| Blueprint | Convert story into production plans and asset packages. | Asset blueprint. |
| Prompt | Generate AI instructions using domain and asset rules. | Prompt set. |
| Assets | Produce visuals, narration, guides, metadata, and media parts. | Asset bundle. |
| Publishing | Package, localize, schedule, and distribute content. | Published media. |
| Analytics | Measure performance and quality to improve the loop. | Optimization signals. |
