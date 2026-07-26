namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryMediaPipelineStatus { Planned, Complete, PartiallyComplete, Rejected }
public enum DocumentaryMediaPipelineRejectionReason { MediaProjectNotComplete, MediaProjectIdentityMismatch, MaterializationIdentityMismatch, TopicIdentityMismatch, CorrelationMismatch, PipelinePolicyRejected, RequiredVariantMissing, VariantInventoryMismatch, VariantOrderMismatch, VariantIdentityMismatch, SceneInventoryMismatch, SceneOrderMismatch, SceneIdentityMismatch, NarrationPlanRejected, SubtitlePlanRejected, VisualPlanRejected, TimingPlanRejected, TransitionPlanRejected, AssetDependencyMismatch, UnsupportedAssetType, ProviderUnavailable, VisualGenerationFailed, NarrationSynthesisFailed, SubtitleGenerationFailed, SceneCompositionFailed, VariantCompositionFailed, RenderVerificationFailed, OutputManifestMismatch }
public enum DocumentaryMediaPipelineMode { PlanOnly, Execute }
public enum DocumentaryMediaAssetType { VisualImage, SkySimulationImage, StarChartImage, TelescopeViewImage, ScientificDiagramImage, HistoricalIllustrationImage, NarrationAudio, SubtitleDocument, SceneVideo, VariantVideo }
public enum DocumentaryMediaAssetFormat { Png, Jpeg, WebP, Wav, Mp3, Aac, Srt, Vtt, Mp4 }
public enum DocumentaryMediaAssetStatus { Planned, Generated, Verified, Failed }
public enum DocumentaryMediaExecutionStage { ValidateProject, PlanAssets, GenerateVisuals, SynthesizeNarration, GenerateSubtitles, ComposeScenes, ComposeVariant, VerifyVariant, BuildManifest }
public enum DocumentaryMediaProviderCapability { GeneratedIllustration, SkySimulation, StarChart, TelescopeView, ScientificDiagram, HistoricalIllustration, TextToSpeech, SubtitleGeneration, SceneComposition, VideoComposition, RenderVerification }

public sealed class DocumentaryMediaChecksumProfile
{
    public DocumentaryMediaChecksumProfile(string algorithm = "SHA-256", string schemaVersion = "1.0")
    { Algorithm = algorithm == "SHA-256" ? algorithm : throw new ArgumentException("Schema 1.0 requires SHA-256."); SchemaVersion = schemaVersion == "1.0" ? schemaVersion : throw new ArgumentException("Schema must be 1.0."); }
    public string Algorithm { get; }
    public string SchemaVersion { get; }
}
