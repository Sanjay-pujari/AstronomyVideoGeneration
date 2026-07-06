# Visual Intelligence Configuration

Visual Intelligence is configured as an observation-only subsystem. It must not replace active image generation prompts, wire `PromptPackage` into Azure/Image2 generation, or block production pipeline phases unless a future feature explicitly changes that contract.

## Production/default mode

Production defaults keep Visual Intelligence disabled and preserve V3.1 production behavior:

```json
"VisualIntelligence": {
  "Enabled": false,
  "WriteDiagnostics": false,
  "DiagnosticsOutputPath": "",
  "DefaultProvider": "unknown",
  "ObservationMode": true,
  "UseVisualCreativeDirector": false,
  "UseCDL": false,
  "UseCreativeDirectionContract": false,
  "UsePromptComposerV2": false,
  "UseProviderProfiles": false,
  "UseQualityScoring": false,
  "UseQualityScoringBlocking": false,
  "UseExperimentalRenderingRules": false
}
```

With `Enabled: false`, orchestration is a no-op. Diagnostics writing is also disabled by default, and all Visual Intelligence feature flags default to `false`.

## Development observation mode

Development configuration enables non-blocking observation diagnostics using the `AzureImage` provider profile:

```json
"VisualIntelligence": {
  "Enabled": true,
  "WriteDiagnostics": true,
  "DiagnosticsOutputPath": "",
  "DefaultProvider": "AzureImage",
  "ObservationMode": true,
  "UseVisualCreativeDirector": true,
  "UseCDL": true,
  "UseCreativeDirectionContract": true,
  "UsePromptComposerV2": true,
  "UseProviderProfiles": true,
  "UseQualityScoring": false,
  "UseQualityScoringBlocking": false,
  "UseExperimentalRenderingRules": false
}
```

This mode generates `PromptPackage` diagnostics tailored for the `AzureImage` provider profile while leaving active Phase 1-18 output unchanged. It does not call Azure, generate images, change narration, SRT, TTS, validation, rendering, or phase execution.

## Optional full observation testing

For local/full observation testing only, quality scoring can be enabled without making it blocking:

```json
"UseQualityScoring": true,
"UseQualityScoringBlocking": false
```

Do not enable `UseQualityScoringBlocking` for development or production defaults.

## Provider profile guidance

- Use `AzureImage` for development observation so PromptComposerV2 resolves the same provider profile expected by Azure image diagnostics.
- Use `generic`/`unknown` only for fallback behavior and focused unit tests that intentionally verify missing-provider handling.
- `UseProviderProfiles: true` with `DefaultProvider: "AzureImage"` avoids the generic fallback warning in development observation diagnostics.

## Expected diagnostics files

When diagnostics writing is enabled, Visual Intelligence writes these files under the configured diagnostics folder or the run output folder:

- `CDL.json`
- `CreativeDirectionContract.json`
- `PromptPackage.json`
- `OrchestrationSummary.json`
- `QualityReport.json` only when `UseQualityScoring` is enabled
