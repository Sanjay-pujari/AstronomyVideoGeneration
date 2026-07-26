# O2.18 documentary media pipeline orchestration (schema 1.0)

O2.18 executes the retained O2.17 media project without reinterpretation or invented astronomy facts. It produces the four canonical variants (long English, long Hindi, short English, short Hindi) in canonical order.

`PlanOnly` returns `Planned`, invokes no provider, and creates only the deterministic logical plan and planned manifest. `Execute` requires all six constructor-injected provider ports: visual generation, narration synthesis, subtitle generation, scene composition, variant composition, and render verification. A variant is complete only after its variant-video output passes render verification.

Measured TTS duration is retained. Effective timing reconciles planned duration with measured narration, visual hold, and transition duration. Subtitle text and cue inventory are preserved. Scene composition and variant composition execute only after their dependencies succeed; failure isolation follows those dependencies. Partial completion is returned only when the policy permits it.

Visual generation uses `MaximumVisualAttempts`; narration synthesis uses `MaximumNarrationAttempts`; scene and variant composition use `MaximumCompositionAttempts`. Subtitle generation intentionally has exactly one attempt in schema 1.0 (subtitle retry is unsupported).

Core contains no vendor SDK, direct FFmpeg or process execution, HTTP adapter, storage/database/queue, publishing, upload, or YouTube integration. Those production adapters are outside O2.18. This work does not begin O2.19.
