# Output Artifact Management

Hero outputs are organized so production assets remain in the `hero/` root while operational evidence is separated into dedicated folders.

## Production artifacts

Production files are written directly under `hero/`:

- `hero-final.png`
- `hero-landscape.png`
- `hero-portrait.png`
- `hero-square.png`
- `hero-asset-story.json`
- `hero-asset-blueprint.json`
- `hero-composition-model.json`

These are the only artifacts expected in a clean production hero root.

## Diagnostics artifacts

Diagnostics are written under `hero/diagnostics/` when diagnostics are enabled:

- `hero-generation-diagnostics.json`
- `hero-layout-validation.json`
- `hero-review.json`
- `hero-scene-manifest.json`
- `visual-prompt-diagnostics.json`

Diagnostic writes are non-blocking: if a diagnostic file cannot be written, the pipeline logs a warning and continues so production image output is not failed by diagnostic storage problems.

## Comparison artifacts

V4 comparison and migration files are written under `hero/comparison/` when comparison output is enabled:

- `hero-v3-prompt.txt`
- `hero-v4-prompt.txt`
- `hero-prompt-comparison.json`
- `hero-migration-report.json`
- `hero-v3.png`
- `hero-v4.png`
- `hero-side-by-side.png`
- `hero-comparison.json`

Comparison artifacts are observational only and do not replace active prompts or production hero generation.

## Configuration

Use the `OutputArtifacts` section:

```json
"OutputArtifacts": {
  "Mode": "Development",
  "WriteDiagnostics": true,
  "WriteComparison": true,
  "WriteIntermediateFiles": false,
  "CleanupTemporaryFiles": true
}
```

Supported modes:

- `Production`: writes production artifacts only by default. Diagnostics and comparison files are skipped unless explicitly enabled.
- `Development`: writes production, diagnostics, and comparison artifacts for local inspection.
- `CI`: keeps diagnostics where possible while avoiding unnecessary image comparison artifacts.
- `Debug`: writes all artifact classes.

Recommended settings:

- Development: `Mode=Development`, `WriteDiagnostics=true`, `WriteComparison=true`.
- Production: `Mode=Production`, `WriteDiagnostics=false`, `WriteComparison=false` unless an incident requires temporary diagnostics.
