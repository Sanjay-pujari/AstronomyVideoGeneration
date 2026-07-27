using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.ProductionAdapters;

public static class DocumentaryNarrationProviderIds { public const string AzureSpeech = "AzureSpeech"; }

public sealed class DocumentaryNarrationAdapterOptions
{
    public const string SectionName = "DocumentaryProductionAdapters:Narration";
    public bool Enabled { get; set; }
    public bool NormalizeAudio { get; set; }
    public bool RetainProviderNativeAudio { get; set; }
}

public sealed record DocumentaryNarrationVoiceResolution(bool Succeeded, DocumentaryMediaLanguage Language, string? Locale, string? VoiceId, string? SpeakingRate, string? Pitch, string? Volume, string Reason, DocumentaryProductionFailure? Failure);
public interface IDocumentaryNarrationVoiceResolver { DocumentaryNarrationVoiceResolution Resolve(DocumentaryNarrationSynthesisRequest request); }
public sealed class DocumentaryNarrationVoiceResolver(IOptions<AzureSpeechOptions> options) : IDocumentaryNarrationVoiceResolver
{
    public DocumentaryNarrationVoiceResolution Resolve(DocumentaryNarrationSynthesisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var language = request.Language;
        var code = language switch { DocumentaryMediaLanguage.English => "en", DocumentaryMediaLanguage.Hindi => "hi", _ => null };
        if (code is null) return Fail(language, DocumentaryProductionFailureCode.ProviderRejectedRequest, "The narration language is not supported.");
        var locale = code == "hi" ? "hi-IN" : "en-IN";
        var configured = options.Value.Voices ?? new(StringComparer.OrdinalIgnoreCase);
        var explicitVoice = string.IsNullOrWhiteSpace(request.VoiceProfileId) ? null : request.VoiceProfileId.Trim();
        var voice = explicitVoice;
        if (voice is null || voice.Equals("default", StringComparison.OrdinalIgnoreCase)) configured.TryGetValue(code, out voice);
        if (string.IsNullOrWhiteSpace(voice) && string.Equals(options.Value.DefaultLanguage, code, StringComparison.OrdinalIgnoreCase)) voice = options.Value.DefaultVoiceName;
        if (string.IsNullOrWhiteSpace(voice)) return Fail(language, DocumentaryProductionFailureCode.ConfigurationMissing, "No voice is configured for the narration language.");
        if (!voice.StartsWith(code + "-", StringComparison.OrdinalIgnoreCase)) return Fail(language, DocumentaryProductionFailureCode.ProviderRejectedRequest, "The requested voice is incompatible with the narration language.");
        configured.TryGetValue(code, out _);
        var rate = (options.Value.ProsodyRate ?? new(StringComparer.OrdinalIgnoreCase)).TryGetValue(code, out var configuredRate) ? configuredRate : options.Value.DefaultProsodyRate;
        return new(true, language, locale, voice, rate ?? "medium", options.Value.SsmlPitch ?? "+2%", "default", explicitVoice is null ? "ConfiguredLanguageVoice" : "ExplicitVoice", null);
    }
    private static DocumentaryNarrationVoiceResolution Fail(DocumentaryMediaLanguage language, DocumentaryProductionFailureCode code, string message) => new(false, language, null, null, null, null, null, "Rejected", new(code, message, ProviderId: DocumentaryNarrationProviderIds.AzureSpeech));
}

public sealed record DocumentaryNarrationProviderRequest(string AssetId,string SourceInstructionId,string? SceneId,string VariantId,string CorrelationId,DocumentaryMediaLanguage Language,string Locale,string VoiceId,string NarrationText,string Ssml,DocumentaryMediaAssetFormat RequestedAudioFormat,int SampleRate,int ChannelCount,int Attempt,string OutputDirectory);
public sealed record DocumentaryNarrationProviderResponse(string? OutputPath, DocumentaryMediaAssetFormat? Format, string? ProviderRequestId = null, DocumentaryProductionFailure? Failure = null);
public interface IDocumentaryNarrationProviderBinding { string ProviderId { get; } Task<DocumentaryNarrationProviderResponse> SynthesizeAsync(DocumentaryNarrationProviderRequest request,CancellationToken cancellationToken); }

public sealed class AzureSpeechDocumentaryNarrationProviderBinding(IAzureSpeechClient speech, ISsmlBuilder ssmlBuilder, IOptions<AzureSpeechOptions> options) : IDocumentaryNarrationProviderBinding
{
    public string ProviderId => DocumentaryNarrationProviderIds.AzureSpeech;
    public async Task<DocumentaryNarrationProviderResponse> SynthesizeAsync(DocumentaryNarrationProviderRequest request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.NarrationText) || string.IsNullOrWhiteSpace(request.VoiceId) || !Path.IsPathFullyQualified(request.OutputDirectory)) return Failed(DocumentaryProductionFailureCode.ProviderRejectedRequest, "The translated narration request is invalid.");
        if (!Configured(options.Value)) return Failed(DocumentaryProductionFailureCode.ConfigurationMissing, "Azure Speech configuration is missing.");
        Directory.CreateDirectory(request.OutputDirectory);
        try
        {
            byte[] bytes;
            if (request.RequestedAudioFormat == DocumentaryMediaAssetFormat.Wav) bytes = await speech.SynthesizeWavSsmlAsync(request.Ssml, options.Value, token);
            else if (request.RequestedAudioFormat == DocumentaryMediaAssetFormat.Mp3)
            {
                // The existing MP3 operation owns SSML construction; passing plain text prevents double building.
                var scoped = CloneForVoice(options.Value, request);
                bytes = await speech.SynthesizeMp3Async(request.NarrationText, scoped, token);
            }
            else return Failed(DocumentaryProductionFailureCode.ProviderRejectedRequest, "Only WAV and MP3 narration are supported.");
            if (bytes.Length == 0) return Failed(DocumentaryProductionFailureCode.OutputArtifactEmpty, "Azure Speech returned empty audio.");
            var extension = request.RequestedAudioFormat == DocumentaryMediaAssetFormat.Wav ? "wav" : "mp3";
            var path = Path.Combine(request.OutputDirectory, $"provider-azure-speech.{extension}");
            await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await output.WriteAsync(bytes, token); await output.FlushAsync(token); output.Flush(true);
            return new(path, request.RequestedAudioFormat);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (TimeoutException) { return Failed(DocumentaryProductionFailureCode.ProviderTimeout, "Azure Speech timed out.", true); }
        catch (UnauthorizedAccessException) { return Failed(DocumentaryProductionFailureCode.ProviderAuthenticationFailed, "Azure Speech authentication failed."); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            var code = ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ? DocumentaryProductionFailureCode.ProviderRateLimited : ex.Message.Contains("BadRequest", StringComparison.OrdinalIgnoreCase) ? DocumentaryProductionFailureCode.ProviderRejectedRequest : DocumentaryProductionFailureCode.ProviderUnavailable;
            return Failed(code, "Azure Speech did not complete narration synthesis.", code is DocumentaryProductionFailureCode.ProviderRateLimited or DocumentaryProductionFailureCode.ProviderUnavailable);
        }
    }
    private static bool Configured(AzureSpeechOptions o) => o.UseManagedIdentity ? !string.IsNullOrWhiteSpace(o.Region) && !string.IsNullOrWhiteSpace(o.ResourceId) : !string.IsNullOrWhiteSpace(o.Key) && (!string.IsNullOrWhiteSpace(o.Region) || !string.IsNullOrWhiteSpace(o.Endpoint));
    private static AzureSpeechOptions CloneForVoice(AzureSpeechOptions source, DocumentaryNarrationProviderRequest request) => new() { Key=source.Key,Region=source.Region,Endpoint=source.Endpoint,UseManagedIdentity=source.UseManagedIdentity,ResourceId=source.ResourceId,ManagedIdentityClientId=source.ManagedIdentityClientId,UseSsml=source.UseSsml,DefaultLanguage=request.Language==DocumentaryMediaLanguage.Hindi?"hi":"en",Voices=new(StringComparer.OrdinalIgnoreCase){{request.Language==DocumentaryMediaLanguage.Hindi?"hi":"en",request.VoiceId}},PrimaryVoice=request.VoiceId,FallbackVoices=[],ProsodyRate=new(StringComparer.OrdinalIgnoreCase){{request.Language==DocumentaryMediaLanguage.Hindi?"hi":"en",source.DefaultProsodyRate??"medium"}},DefaultProsodyRate=source.DefaultProsodyRate,SsmlPitch=source.SsmlPitch,TimeoutRetryAttempts=source.TimeoutRetryAttempts,TimeoutRetryDelayMs=source.TimeoutRetryDelayMs };
    private static DocumentaryNarrationProviderResponse Failed(DocumentaryProductionFailureCode code,string message,bool retry=false)=>new(null,null,Failure:new(code,message,retry,DocumentaryNarrationProviderIds.AzureSpeech));
}

public sealed record DocumentaryNarrationAudioInspectionResult(bool Succeeded,DocumentaryMediaAssetFormat? Format,long DurationMilliseconds,int SampleRate,int ChannelCount,DocumentaryProductionFailure? Failure);
public interface IDocumentaryNarrationAudioInspector { Task<DocumentaryNarrationAudioInspectionResult> InspectAsync(string path,CancellationToken cancellationToken); }
public sealed class DocumentaryNarrationAudioInspector : IDocumentaryNarrationAudioInspector
{
    public async Task<DocumentaryNarrationAudioInspectionResult> InspectAsync(string path,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if(!File.Exists(path)) return Fail(DocumentaryProductionFailureCode.OutputArtifactMissing,"Narration audio is missing.");
        var bytes=await File.ReadAllBytesAsync(path,token); if(bytes.Length==0)return Fail(DocumentaryProductionFailureCode.OutputArtifactEmpty,"Narration audio is empty.");
        if(bytes.Length>=12 && Encoding.ASCII.GetString(bytes,0,4)=="RIFF" && Encoding.ASCII.GetString(bytes,8,4)=="WAVE")
        { if(bytes.Length<44||Encoding.ASCII.GetString(bytes,12,4)!="fmt "||BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20,2))!=1)return Fail(DocumentaryProductionFailureCode.OutputFormatInvalid,"The WAV header is invalid.");var channels=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(22,2));var rate=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24,4));var byteRate=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28,4));var block=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(32,2));var bits=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(34,2));var data=FindData(bytes);if(channels<1||rate<1||byteRate!=rate*channels*(bits/8)||block!=channels*(bits/8)||data<1)return Fail(DocumentaryProductionFailureCode.OutputFormatInvalid,"The WAV header is invalid.");return new(true,DocumentaryMediaAssetFormat.Wav,Math.Max(1,data*1000L/byteRate),rate,channels,null); }
        var hasId3=bytes.Length>4&&bytes[0]==0x49&&bytes[1]==0x44&&bytes[2]==0x33;
        var hasFramePrefix=bytes.Length>2&&bytes[0]==0xff&&(bytes[1]&0xe0)==0xe0;
        if(hasId3||hasFramePrefix)
        { var frame=FindMp3Frame(bytes);if(frame is null)return Fail(DocumentaryProductionFailureCode.DurationMeasurementFailed,"MP3 duration could not be measured.");var (rate,bitrate,channels)=frame.Value;return new(true,DocumentaryMediaAssetFormat.Mp3,Math.Max(1,bytes.LongLength*8*1000/bitrate),rate,channels,null); }
        return Fail(DocumentaryProductionFailureCode.OutputFormatInvalid,"Narration audio is not a supported WAV or MP3 file.");
    }
    private static int FindData(byte[] b){for(var i=12;i+8<=b.Length;){var size=BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i+4,4));if(size<0||i+8L+size>b.Length)return 0;if(Encoding.ASCII.GetString(b,i,4)=="data")return size;i+=8+size+(size&1);}return 0;}
    private static (int rate,int bitrate,int channels)? FindMp3Frame(byte[] b){int[] rates=[44100,48000,32000],bitrates=[0,32000,40000,48000,56000,64000,80000,96000,112000,128000,160000,192000,224000,256000,320000];for(var i=0;i+4<b.Length;i++)if(b[i]==0xff&&(b[i+1]&0xe0)==0xe0){var ri=(b[i+2]>>2)&3;var bi=(b[i+2]>>4)&15;if(ri<3&&bi>0&&bi<15)return(rates[ri],bitrates[bi],((b[i+3]>>6)&3)==3?1:2);}return null;}
    private static DocumentaryNarrationAudioInspectionResult Fail(DocumentaryProductionFailureCode c,string m)=>new(false,null,0,0,0,new(c,m));
}

public sealed record DocumentaryNarrationAudioProfile(DocumentaryMediaAssetFormat Format,int SampleRate,int ChannelCount);
public sealed record DocumentaryNarrationNormalizationResult(bool Succeeded,string? OutputPath,DocumentaryProductionFailure? Failure);
public interface IDocumentaryNarrationAudioNormalizer { Task<DocumentaryNarrationNormalizationResult> NormalizeAsync(string sourcePath,string destinationPath,DocumentaryNarrationAudioProfile profile,CancellationToken cancellationToken); }
public sealed class ExistingDocumentaryNarrationAudioNormalizer : IDocumentaryNarrationAudioNormalizer
{ public Task<DocumentaryNarrationNormalizationResult> NormalizeAsync(string source,string destination,DocumentaryNarrationAudioProfile profile,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(new DocumentaryNarrationNormalizationResult(false,null,new(DocumentaryProductionFailureCode.DependencyMissing,"The existing audio normalizer is not available in this composition.")));} }

public sealed record DocumentaryProductionNarrationAdapterResult
{
    private DocumentaryProductionNarrationAdapterResult(bool ok,DocumentaryPhysicalArtifactDescriptor? artifact,DocumentaryProductionFailure? failure,string requested,string actual,string? voice,DocumentaryMediaLanguage language,DocumentaryMediaAssetFormat format,bool normalized,string? diagnostics){Succeeded=ok;Artifact=artifact;Failure=failure;RequestedProviderId=requested;ActualProviderId=actual;VoiceId=voice;Language=language;AudioFormat=format;NormalizationApplied=normalized;ProviderDiagnosticsReference=diagnostics;}
    public bool Succeeded{get;} public DocumentaryPhysicalArtifactDescriptor? Artifact{get;} public DocumentaryProductionFailure? Failure{get;} public string RequestedProviderId{get;} public string ActualProviderId{get;} public string? VoiceId{get;} public DocumentaryMediaLanguage Language{get;} public DocumentaryMediaAssetFormat AudioFormat{get;} public bool NormalizationApplied{get;} public string? ProviderDiagnosticsReference{get;}
    public static DocumentaryProductionNarrationAdapterResult Success(DocumentaryPhysicalArtifactDescriptor artifact,string voice,DocumentaryMediaLanguage language,DocumentaryMediaAssetFormat format,bool normalized,string? diagnostics=null)=>new(true,artifact,null,DocumentaryNarrationProviderIds.AzureSpeech,DocumentaryNarrationProviderIds.AzureSpeech,voice,language,format,normalized,diagnostics);
    public static DocumentaryProductionNarrationAdapterResult Failed(DocumentaryProductionFailure failure,DocumentaryMediaLanguage language,DocumentaryMediaAssetFormat format,string? voice=null)=>new(false,null,failure,DocumentaryNarrationProviderIds.AzureSpeech,DocumentaryNarrationProviderIds.AzureSpeech,voice,language,format,false,null);
}
public interface IDocumentaryProductionNarrationAdapter { Task<DocumentaryProductionNarrationAdapterResult> SynthesizeAsync(DocumentaryNarrationSynthesisRequest request,DocumentaryProductionExecutionContext executionContext,DocumentaryProductionAttemptContext attemptContext,DocumentaryProductionWorkspace workspace,CancellationToken cancellationToken); }

public sealed class ExistingAzureSpeechDocumentaryNarrationAdapter(IOptions<DocumentaryNarrationAdapterOptions> options,IDocumentaryNarrationVoiceResolver voices,IEnumerable<IDocumentaryNarrationProviderBinding> bindings,ISsmlBuilder ssmlBuilder,IDocumentaryNarrationAudioInspector audio,IDocumentaryNarrationAudioNormalizer normalizer,IDocumentaryProductionWorkspaceManager workspaces,IDocumentaryPhysicalArtifactInspector artifacts,IDocumentaryPhysicalArtifactDescriptorValidator validator,IDocumentaryPhysicalArtifactRegistry registry,IDocumentaryProductionDiagnosticsWriter diagnostics,IDocumentaryProductionFailureNormalizer failures) : IDocumentaryProductionNarrationAdapter
{
    public async Task<DocumentaryProductionNarrationAdapterResult> SynthesizeAsync(DocumentaryNarrationSynthesisRequest request,DocumentaryProductionExecutionContext execution,DocumentaryProductionAttemptContext attempt,DocumentaryProductionWorkspace workspace,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();Validate(request,execution,attempt);var resolution=voices.Resolve(request);if(!resolution.Succeeded)return DocumentaryProductionNarrationAdapterResult.Failed(resolution.Failure!,request.Language,request.AssetFormat);
        if(!options.Value.Enabled)return DocumentaryProductionNarrationAdapterResult.Failed(new(DocumentaryProductionFailureCode.AdapterUnavailable,"The narration adapter is disabled."),request.Language,request.AssetFormat,resolution.VoiceId);
        var providers=bindings.ToArray();if(providers.Count(x=>x.ProviderId==DocumentaryNarrationProviderIds.AzureSpeech)!=1)return DocumentaryProductionNarrationAdapterResult.Failed(new(DocumentaryProductionFailureCode.AdapterUnavailable,"The Azure Speech narration binding is unavailable."),request.Language,request.AssetFormat,resolution.VoiceId);
        var provider=providers.Single(x=>x.ProviderId==DocumentaryNarrationProviderIds.AzureSpeech);var dir=workspaces.GetAttemptDirectory(workspace,DocumentaryProductionOperationKind.NarrationSynthesis,request.AssetPlan.AssetId,attempt.AttemptNumber);Directory.CreateDirectory(dir);
        var text=request.NarrationBlock.Text;var ssml=ssmlBuilder.BuildSsml(text,resolution.VoiceId!,rateOverride:resolution.SpeakingRate,pitchOverride:resolution.Pitch);var translated=new DocumentaryNarrationProviderRequest(request.AssetPlan.AssetId,request.AssetPlan.SourceInstructionId,request.AssetPlan.SceneId,request.AssetPlan.VariantType.ToString(),request.CorrelationId,request.Language,resolution.Locale!,resolution.VoiceId!,text,ssml,request.AssetFormat,request.SampleRate,request.ChannelCount,request.Attempt,dir);
        var watch=Stopwatch.StartNew();DocumentaryNarrationProviderResponse response;try{response=await provider.SynthesizeAsync(translated,token);}catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}catch(Exception ex){return DocumentaryProductionNarrationAdapterResult.Failed(failures.Normalize(ex,DocumentaryProductionOperationKind.NarrationSynthesis,false),request.Language,request.AssetFormat,resolution.VoiceId);}if(response.Failure is not null)return DocumentaryProductionNarrationAdapterResult.Failed(response.Failure,request.Language,request.AssetFormat,resolution.VoiceId);if(string.IsNullOrWhiteSpace(response.OutputPath)||!DocumentaryPathComparison.IsBelow(dir,response.OutputPath))return DocumentaryProductionNarrationAdapterResult.Failed(new(DocumentaryProductionFailureCode.ProviderInvalidResponse,"Provider output is not owned by the attempt workspace."),request.Language,request.AssetFormat,resolution.VoiceId);
        var measured=await audio.InspectAsync(response.OutputPath,token);if(!measured.Succeeded)return DocumentaryProductionNarrationAdapterResult.Failed(measured.Failure!,request.Language,request.AssetFormat,resolution.VoiceId);var current=response.OutputPath;var normalized=false;
        if(measured.Format!=request.AssetFormat||measured.SampleRate!=request.SampleRate||measured.ChannelCount!=request.ChannelCount){if(!options.Value.NormalizeAudio)return DocumentaryProductionNarrationAdapterResult.Failed(new(DocumentaryProductionFailureCode.OutputFormatInvalid,"Measured narration audio does not match the requested profile."),request.Language,request.AssetFormat,resolution.VoiceId);var ext=request.AssetFormat==DocumentaryMediaAssetFormat.Wav?"wav":"mp3";var norm=await normalizer.NormalizeAsync(current,Path.Combine(dir,$"normalized-narration.{ext}"),new(request.AssetFormat,request.SampleRate,request.ChannelCount),token);if(!norm.Succeeded)return DocumentaryProductionNarrationAdapterResult.Failed(norm.Failure!,request.Language,request.AssetFormat,resolution.VoiceId);if(string.IsNullOrWhiteSpace(norm.OutputPath)||!DocumentaryPathComparison.IsBelow(dir,norm.OutputPath))return DocumentaryProductionNarrationAdapterResult.Failed(new(DocumentaryProductionFailureCode.ProviderInvalidResponse,"Normalized output is not owned by the attempt workspace."),request.Language,request.AssetFormat,resolution.VoiceId);current=norm.OutputPath;normalized=true;measured=await audio.InspectAsync(current,token);if(!measured.Succeeded||measured.Format!=request.AssetFormat||measured.SampleRate!=request.SampleRate||measured.ChannelCount!=request.ChannelCount)return DocumentaryProductionNarrationAdapterResult.Failed(new(DocumentaryProductionFailureCode.OutputFormatInvalid,"Normalized narration does not match the requested profile."),request.Language,request.AssetFormat,resolution.VoiceId);}
        var extension=request.AssetFormat==DocumentaryMediaAssetFormat.Wav?"wav":"mp3";var final=workspaces.GetFinalArtifactPath(workspace,request.AssetPlan.VariantType.ToString(),null,DocumentaryPhysicalArtifactKind.NarrationAudio,request.AssetPlan.AssetId,extension);await workspaces.FinalizeArtifactAsync(workspace,current,final,true,token);var descriptor=await artifacts.InspectAsync(new(request.AssetPlan.AssetId,final,request.AssetFormat==DocumentaryMediaAssetFormat.Wav?"audio/wav":"audio/mpeg",provider.ProviderId,attempt.AttemptNumber,request.CorrelationId),token);descriptor=descriptor with{DurationMilliseconds=measured.DurationMilliseconds,AudioSampleRate=measured.SampleRate,AudioChannelCount=measured.ChannelCount};if(validator.Validate(descriptor).Count>0)return DocumentaryProductionNarrationAdapterResult.Failed(new(DocumentaryProductionFailureCode.OutputFormatInvalid,"The finalized narration descriptor is invalid."),request.Language,request.AssetFormat,resolution.VoiceId);await registry.RegisterAsync(descriptor,DocumentaryPhysicalArtifactKind.NarrationAudio,token);watch.Stop();await diagnostics.WriteAsync(workspace.DiagnosticsDirectory,$"narration-{Safe(request.AssetPlan.AssetId)}-{attempt.AttemptNumber}.json",new{execution.ExecutionId,request.CorrelationId,request.AssetPlan.AssetId,request.AssetPlan.SourceInstructionId,request.AssetPlan.SceneId,VariantId=request.AssetPlan.VariantType.ToString(),Language=request.Language.ToString(),resolution.Locale,resolution.VoiceId,ProviderId=provider.ProviderId,Attempt=attempt.AttemptNumber,RequestedFormat=request.AssetFormat.ToString(),ActualFormat=measured.Format.ToString(),NormalizationApplied=normalized,measured.DurationMilliseconds,measured.SampleRate,measured.ChannelCount,descriptor.Length,descriptor.Checksum,descriptor.ContentIdentity,ProviderRequestId=response.ProviderRequestId,ElapsedMilliseconds=watch.ElapsedMilliseconds,Outcome="Succeeded",NarrationTextHash=Hash(text),NarrationTextLength=text.Length,SsmlHash=Hash(ssml),SsmlLength=ssml.Length},token);return DocumentaryProductionNarrationAdapterResult.Success(descriptor,resolution.VoiceId!,request.Language,request.AssetFormat,normalized,response.ProviderRequestId);
    }
    private static void Validate(DocumentaryNarrationSynthesisRequest r,DocumentaryProductionExecutionContext e,DocumentaryProductionAttemptContext a){ArgumentNullException.ThrowIfNull(r);if(r.AssetFormat is not(DocumentaryMediaAssetFormat.Wav or DocumentaryMediaAssetFormat.Mp3)||r.SampleRate<1||r.ChannelCount<1||r.Attempt<1)throw new ArgumentException("The narration profile is invalid.");if(r.CorrelationId!=r.AssetPlan.CorrelationId||r.CorrelationId!=e.CorrelationId||r.CorrelationId!=a.CorrelationId||r.AssetPlan.AssetId!=a.AssetId)throw new ArgumentException("Narration execution identity mismatch.");}
    private static string Safe(string v)=>new(v.Select(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_'?c:'_').ToArray());private static string Hash(string v)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v))).ToLowerInvariant();
}

public interface IDocumentaryNarrationSynthesisResultMapper { DocumentaryNarrationSynthesisResult Map(DocumentaryNarrationSynthesisRequest request,DocumentaryProductionNarrationAdapterResult result); }
public sealed class DocumentaryNarrationSynthesisResultMapper : IDocumentaryNarrationSynthesisResultMapper
{ public DocumentaryNarrationSynthesisResult Map(DocumentaryNarrationSynthesisRequest r,DocumentaryProductionNarrationAdapterResult x){var d=x.Artifact;var status=x.Succeeded?DocumentaryMediaAssetStatus.Generated:DocumentaryMediaAssetStatus.Failed;var asset=new DocumentaryMediaAssetResult(r.AssetPlan.AssetId,r.AssetPlan.AssetType,r.AssetFormat,status,x.ActualProviderId,d?.ContentIdentity,d?.Length??0,d?.DurationMilliseconds??0,0,0,0,d?.AudioSampleRate??0,d?.AudioChannelCount??0,d?.Checksum,x.Failure?.Code.ToString(),x.Failure?.Message,d?.AttemptCount??r.Attempt,r.CorrelationId);return new(status,asset,d?.DurationMilliseconds??0,x.Failure?.Code.ToString(),x.Failure?.Message);} }
