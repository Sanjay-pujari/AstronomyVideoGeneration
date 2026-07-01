# AI Content Generation Platform

Astronomy V3 RC2 is the first production implementation of a broader AI-powered Content Generation Platform. The platform separates reusable generation capability from domain knowledge so future products can reuse the same engines while changing the subject matter, rules, assets, and publishing strategy.

## Platform hierarchy

```mermaid
flowchart TD
    Platform[AI Content Generation Platform]
    Domains[Domain Plugins]
    Assets[Generated Asset Families]
    Media[Published Media Experiences]

    Platform --> Domains
    Domains --> Assets
    Assets --> Media

    Domains --> Astronomy[Astronomy]
    Domains --> Education[Education]
    Domains --> Travel[Travel]
    Domains --> Finance[Finance]

    Assets --> Story[Story]
    Assets --> Blueprint[Blueprint]
    Assets --> Hero[Hero Image]
    Assets --> Thumbnail[Thumbnail]
    Assets --> Gallery[Gallery]
    Assets --> Narration[Narration]
    Assets --> Guide[Guide]

    Media --> Video[Video]
    Media --> Article[Article]
    Media --> Social[Social Posts]
    Media --> Shorts[Short-form Media]
```

## Conceptual stack

```mermaid
graph TD
    A[Platform: reusable engines, contracts, orchestration] --> B[Domains: knowledge, terminology, constraints]
    B --> C[Assets: story, visuals, audio, metadata]
    C --> D[Media: rendered, localized, published experiences]
```

## Audience view

```mermaid
journey
    title Platform value by audience
    section Architects
      Understand reusable boundaries: 5: Architect
      Evaluate extension points: 5: Architect
    section Developers
      Implement domain plugins: 5: Developer
      Reuse shared modules: 5: Developer
    section Product Managers
      Define domain offerings: 4: PM
      Prioritize asset packages: 4: PM
    section Investors
      See repeatable expansion model: 5: Investor
      Understand commercial leverage: 5: Investor
```

## Core idea

The platform owns repeatable content generation mechanics. Domains provide context, rules, and market focus. Assets express content in reusable formats. Media packages those assets for channels, languages, and audiences.
