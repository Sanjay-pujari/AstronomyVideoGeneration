# O2.18 Documentary Media Pipeline Orchestration

O2.18 executes, but does not reinterpret, the certified O2.17 media project. It preserves the canonical Long English, Long Hindi, Short English, and Short Hindi variants and their narration, subtitle, visual, scene, topic, correlation, and knowledge-reference intent.

## Schema 1.0 behavior

`PlanOnly` deterministically creates logical visual, narration, subtitle, scene-video, and variant-video plans and their complete dependency graph. It returns `Planned`, with no physical asset results or completed rendered variants. It invokes no provider and performs no render verification, file, network, or rendering work. `Execute` uses only constructor-injected visual-generation, narration-synthesis, subtitle-generation, scene-composition, variant-composition, and render-verification ports. The core has no vendor SDK, FFmpeg process, publishing, YouTube, or social-upload integration.

Asset identities append `.asset.visual`, `.asset.audio`, `.asset.subtitle`, or `.asset.video` to the O2.17 instruction identity. Dependencies use `{source}.depends-on.{target}`. Visual types map to the equivalently named capability; orbital/scientific diagrams, maps, and timelines map to `ScientificDiagram`; object portraits and text cards map to `GeneratedIllustration`.

Actual TTS durations remain distinct from planned durations. Adapters return measured duration and orchestration never rewrites or truncates narration. Subtitle adapters may deterministically scale offsets monotonically, without overlap and within measured duration, while preserving cue text. Scene duration is at least the planned duration and narration duration plus the O2.17 visual hold and transition.

Failures propagate through asset dependencies and are isolated to their dependency branch, so unrelated scenes and variants continue. Partial completion is allowed only by policy. A variant becomes `Complete` only after every required scene asset has succeeded, its variant video has been composed, and that output has passed render verification; `OutputAssetId` then identifies that verified video. Render verification is a distinct capability, and schema 1.0 accepts a verified SRT sidecar. Summary asset counts represent actual execution results rather than planned assets. O2.18 does not invent or rewrite astronomy facts, publish or upload content, or depend directly on vendor SDKs.
