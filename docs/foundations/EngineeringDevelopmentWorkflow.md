# Engineering Development Workflow (EDW)

## Purpose

The Engineering Development Workflow (EDW) is the official process for all future feature implementation in Astronomy V3 RC3 and its successor releases. It is mandatory reading before any engineering work begins.

Astronomy V3 RC3 has reached a stable platform foundation across Hero, Gallery, the Engineering Constitution, Platform Architecture, Universal Knowledge Model, and Documentary Blueprint. The project is therefore moving from architecture expansion into disciplined engineering execution.

The purpose of this workflow is to ensure that every new capability strengthens the platform rather than increasing renderer complexity. Future development must preserve extensibility across astronomical event families, future domains, localization needs, scientific knowledge, and presentation surfaces.

## Core Principle

**Think deeply once. Implement once. Extend forever.**

The objective is not to build features quickly. The objective is to build features that future families and future domains can extend without renderer modification.

Every feature must be designed so that future expansion happens through knowledge, providers, contracts, and configuration rather than patches to stable rendering surfaces.

## Engineering Workflow

### Phase 0: Business Goal

Before design begins, the team must define why the feature exists.

Required questions:

- Why are we building this?
- What user problem are we solving?
- How will success be measured?

Deliverable:

- **Business Objective**

A feature without a clear business objective should not enter engineering design.

### Phase 1: Engineering Design Review (EDR)

The Engineering Design Review is the mandatory thinking phase. Its purpose is to prevent implementation from beginning before the problem, requirements, user experience, and future extension model are understood.

Required questions:

1. Do we completely understand the feature?
2. What are all functional requirements?
3. What are all non-functional requirements?
4. What user experience are we trying to achieve?
5. What future scenarios must be supported?
6. Will the same architecture support all scenarios?

Future scenarios must include known families and plausible unknown families, such as:

- Moon
- Meteor
- Planet Pairing
- Planet Grouping
- Solar Eclipse
- Lunar Eclipse
- Comet
- Constellation
- Nebula
- Deep Sky
- Future unknown families

If the same architecture cannot support these scenarios, the feature must stop and return to design. Renderer-specific patches are not an acceptable substitute for platform architecture.

Deliverable:

- **Approved Engineering Design**

### Phase 2: Architecture Ownership

Every responsibility must have exactly one owner. Shared ownership of a responsibility creates ambiguity, duplication, inconsistent behavior, and long-term maintenance risk.

Responsibilities that require explicit ownership include:

- Title
- Observation
- Knowledge
- Localization
- Prompt
- Rendering
- Validation
- Diagnostics

No responsibility should belong to multiple components. If ownership is unclear, the feature is not ready for implementation.

### Phase 3: Extension Test

Before contracts or implementation begin, the team must run the extension test:

> If tomorrow we add twenty new families, what changes?

The correct answer is limited to:

- Knowledge
- Provider
- Configuration

The incorrect answer includes:

- Hero
- Gallery
- Thumbnail
- Renderer

If renderer modification is required to support a new family, the architecture fails. The work must stop and return to Engineering Design Review.

### Phase 4: Contract Design

Contracts must be designed before implementation. Contracts define what renderers consume, what providers produce, and what validators verify.

Examples include:

- `EventDisplayContract`
- `ObservationInfo`
- `PromptHints`
- `VisualHints`
- `ValidationRules`

At this phase, the team defines shape, ownership, lifecycle, defaults, and validation expectations. Implementation should not begin until contracts are stable enough for consumers and providers to rely on them.

### Phase 5: Implementation

Implementation must consume contracts.

Renderers should not contain business logic. Providers should contain domain intelligence. Knowledge modules should describe facts, rules, and family-specific meaning. Configuration should control supported variation without requiring renderer changes.

Implementation should prefer platform extension over presentation-layer branching. Any feature code that teaches a renderer about a specific family is a design warning and must be reviewed before proceeding.

### Phase 6: Validation

Validation must verify that the feature achieves its goal without weakening the platform.

Validation must cover:

- Business correctness
- Scientific correctness
- Localization correctness
- Visual correctness
- Architecture correctness
- Regression safety

Whenever practical, validation should verify outcomes rather than implementation details. The question is not only whether the code changed correctly, but whether the platform now produces correct, reusable, and extensible behavior.

### Phase 7: Review

After implementation and validation, the team must review the feature as a platform change.

Required questions:

- Did we improve the platform?
- Can future families reuse this?
- Did the renderer become more generic?
- Did business logic move into contracts or providers?

A feature that works for one scenario but makes future scenarios harder is incomplete.

### Phase 8: Knowledge Update

Update only the documents affected by the feature. Do not update unrelated documentation.

Documentation changes should preserve the distinction between stable platform principles, feature-specific notes, and implementation details. The documentation owner must be identified before the feature is considered complete.

### Phase 9: Freeze

Stable modules become frozen after completion.

After freeze:

- Bug fixes are allowed.
- Required correctness updates are allowed.
- Cosmetic redesign is not allowed.
- Renderer expansion for one-off family behavior is not allowed.

Freeze protects the platform from repeated churn and preserves confidence in stable surfaces.

## Engineering Checklist

Every feature must satisfy the following checklist:

- [ ] Business objective defined
- [ ] Requirements complete
- [ ] Future scenarios considered
- [ ] Architecture owner identified
- [ ] Extension test passed
- [ ] Contracts designed
- [ ] Validation strategy defined
- [ ] Diagnostics defined
- [ ] Regression plan created
- [ ] Documentation owner identified

Work that cannot satisfy this checklist must return to the appropriate earlier phase.

## Open/Closed Principle

Every new family should extend:

- Knowledge
- Provider
- Configuration

It should not require changes in:

- Hero
- Gallery
- Thumbnail
- Renderer

The platform must remain open for extension and closed for renderer modification. New family support should add intelligence at the domain layer, not special cases at the rendering layer.

## Golden Question

Before implementation, ask:

> Can the next twenty event families use this without modifying the renderer?

If the answer is no, stop and return to Engineering Design Review.

## Renderer Rule

Renderers render.

Knowledge knows.

Providers decide.

Validators verify.

This rule defines the separation of concerns for the platform. Renderers are presentation surfaces, not domain reasoning engines. Domain knowledge belongs in knowledge models and providers. Correctness belongs in validation.

## Engineering Definition of Done

A feature is complete only if:

- Business goal achieved
- Architecture remains scalable
- Contracts respected
- Validation passed
- Regression passed
- Documentation updated
- Future family extension remains possible

Completion requires both functional success and architectural preservation.

## Examples

### Solar Eclipse Observation Timing

Incorrect:

- Patch Gallery.

Correct:

- Extend the `ObservationInfo` provider.

### Moon Subtype Title

Incorrect:

- Patch Gallery title logic.

Correct:

- Extend the `EventDisplayContract` provider.

### Future Comet Support

Correct:

- Implement `CometProvider`.
- Add required knowledge and configuration.
- Make no Hero, Gallery, Thumbnail, or Renderer modifications.

## Closing Statement

This workflow exists to ensure that every new capability makes the platform stronger instead of more complex.

Think deeply.

Design once.

Implement once.

Extend forever.
