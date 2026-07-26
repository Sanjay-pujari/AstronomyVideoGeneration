# O2.18 Documentary Media Pipeline Orchestration

O2.18 executes, but does not reinterpret, the certified O2.17 media project. It preserves the canonical Long English, Long Hindi, Short English, and Short Hindi variants and their narration, subtitle, visual, scene, topic, correlation, and knowledge-reference intent.

## Schema 1.0 behavior

`PlanOnly` deterministically creates logical visual, narration, subtitle, scene-video, and variant-video plans and their dependency graph. It invokes no provider and performs no file, network, or rendering work. `Execute` uses only constructor-injected logical provider ports. The core has no vendor SDK, FFmpeg process, publishing, YouTube, or social-upload integration.

Asset identities append `.asset.visual`, `.asset.audio`, `.asset.subtitle`, or `.asset.video` to the O2.17 instruction identity. Dependencies use `{source}.depends-on.{target}`. Visual types map to the equivalently named capability; orbital/scientific diagrams, maps, and timelines map to `ScientificDiagram`; object portraits and text cards map to `GeneratedIllustration`.

Actual TTS durations remain distinct from planned durations. Adapters return measured duration and orchestration never rewrites or truncates narration. Subtitle adapters may deterministically scale offsets monotonically, without overlap and within measured duration, while preserving cue text. Scene duration is at least the planned duration and narration duration plus the O2.17 visual hold and transition.

Failures are isolated to their dependency branch. Partial completion is allowed only by policy. Render verification is a distinct capability, and schema 1.0 accepts a verified SRT sidecar. O2.18 does not invent or rewrite astronomy facts, publish or upload content, or depend directly on vendor SDKs.
