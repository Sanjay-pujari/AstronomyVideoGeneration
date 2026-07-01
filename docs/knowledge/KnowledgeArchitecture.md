# Knowledge Architecture

## Purpose

Knowledge Architecture describes how the platform turns structured domain understanding into generated media. It defines the reusable layer beneath story planning, visual planning, prompt assembly, validation, and publishing.

Astronomy V3 RC2 proves the pattern with astronomy events. The same architecture supports astrology, numerology, education, history, travel, health, finance, and other domains when each domain provides compatible knowledge contracts.

## Definition of knowledge

In this platform, knowledge is structured, validated, reusable context that describes what the content is about, what must be true, how it should be represented, and how output should be checked.

Knowledge is not a prompt. A prompt is only a provider-specific projection of knowledge into an AI generation request.

Knowledge includes:

- Facts: verifiable claims and source-derived values.
- Entities: objects, people, places, events, concepts, instruments, or market symbols.
- Relationships: how entities interact or depend on one another.
- Timing: dates, windows, recurrence, phases, sequence, duration, and deadlines.
- Region: geography, audience location, visibility, jurisdiction, or market.
- Language: locale, terminology, tone, units, calendars, and cultural rules.
- Visual intent: what must appear, what must not appear, and how visual assets communicate meaning.
- Validation rules: checks that keep generated assets faithful to the knowledge model.

## Knowledge-first pipeline

```mermaid
flowchart TD
    DK[Domain Knowledge] --> KM[Knowledge Model]
    KM --> SE[Story Engine]
    SE --> BE[Blueprint Engine]
    BE --> CE[Composition Engine]
    CE --> PE[Prompt Engine]
    PE --> AI[AI Generation]
    AI --> V[Validation]
    V --> AS[Assets]

    KM -.contracts.-> V
    DK -.terminology.-> PE
    DK -.constraints.-> BE
    V -.feedback.-> KM
```

The important architectural rule is that every downstream engine consumes a structured representation. The Story Engine receives facts and narrative affordances, not raw prose. The Blueprint Engine receives asset requirements and constraints, not vague creative direction. The Prompt Engine receives validated contracts and converts them into provider-specific prompt text.

## Knowledge categories

| Category | Description | Examples |
| --- | --- | --- |
| Platform knowledge | Shared rules owned by the platform and reused across domains. | Asset schemas, validation patterns, publishing states, prompt safety rules. |
| Domain knowledge | Subject-matter model for a product domain. | Astronomy objects, zodiac signs, lesson topics, historical periods, travel destinations. |
| Event knowledge | Time-bound or occurrence-specific knowledge. | Meteor shower peak, eclipse path, market earnings date, festival date, itinerary day. |
| Asset knowledge | Knowledge required to generate consistent media assets. | Hero image visual intent, thumbnail hierarchy, narration tone, safe-area rules. |
| Localization knowledge | Language, locale, cultural, and regional adaptation rules. | Hindi terminology, metric/imperial units, local sky visibility, market jurisdiction. |

## Astronomy today

Astronomy already uses a knowledge-first pattern through event families, event intelligence, visual requirements, localization, and validation gates. Event families such as eclipses, conjunctions, comets, meteor showers, supermoons, occultations, and generic astronomy topics provide domain-specific rules that influence story structure, visual depiction, observability, timing, and user guidance.

Current astronomy knowledge appears as:

- Event family definitions and constraints.
- Structured event metadata such as date, peak time, region, visibility, objects involved, and observing guidance.
- Visual source requirements for realistic celestial representation.
- Prompt enrichment rules that prevent generic or incorrect imagery.
- Localization requirements for language-specific narration and overlays.
- Validation checks for prompt safety, asset completeness, and event-family correctness.

## Reuse by future domains

Future domains reuse the same architecture by replacing astronomy-specific ontology and rules with domain-specific knowledge plugins.

```mermaid
flowchart LR
    Platform[Reusable Platform Engines]
    Contracts[Knowledge Contracts]
    Astronomy[Astronomy Plugin]
    Astrology[Astrology Plugin]
    Education[Education Plugin]
    Finance[Finance Plugin]
    Assets[Generated Assets]

    Contracts --> Platform
    Astronomy --> Contracts
    Astrology --> Contracts
    Education --> Contracts
    Finance --> Contracts
    Platform --> Assets
```

The shared platform does not need to know every domain in advance. It needs stable contracts for facts, entities, events, localization, visual intent, and validation. Each domain plugin supplies the domain-specific vocabulary, rules, sources, and interpretation logic.

## Example domains

| Domain | Knowledge focus | Generated asset implications |
| --- | --- | --- |
| Astronomy | Celestial entities, events, observability, timing, location. | Accurate sky visuals, observing guides, event explainers. |
| Astrology | Zodiac systems, houses, transits, interpretations, audience personalization. | Symbolic visuals, daily readings, compatibility narratives. |
| Numerology | Numbers, birth dates, name mappings, interpretive systems. | Personalized reports, symbolic artwork, explanation videos. |
| Education | Curriculum, learning objectives, prerequisites, assessments. | Lessons, diagrams, quizzes, explainer videos. |
| History | People, places, periods, chronology, causality, sources. | Timelines, documentary scripts, maps, reenactment prompts. |
| Travel | Destinations, seasons, itineraries, attractions, budgets, restrictions. | Guides, itineraries, destination visuals, localized recommendations. |
| Health | Conditions, wellness goals, evidence levels, safety disclaimers. | Educational content, habit plans, visuals with strict validation. |
| Finance | Instruments, markets, dates, metrics, risk, regulations. | Market explainers, dashboards, scenario narratives, compliance checks. |

## Architectural responsibilities

The knowledge layer is responsible for:

- Keeping facts separate from generated expression.
- Normalizing domain inputs into platform-readable contracts.
- Providing stable identifiers for entities, events, regions, languages, and assets.
- Supplying validation rules before and after generation.
- Enabling localization without corrupting meaning.
- Supporting future knowledge graph, RAG, and AI content director capabilities.

## Non-goals

The knowledge layer does not directly render videos, call AI providers, publish assets, or replace domain expertise. It provides the structured source of truth those systems consume.
