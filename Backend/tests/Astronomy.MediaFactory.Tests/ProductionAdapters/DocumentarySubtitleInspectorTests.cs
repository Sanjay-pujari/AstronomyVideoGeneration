using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentarySubtitleInspectorTests
{
 [Theory]
 [InlineData(DocumentaryMediaAssetFormat.Srt,"1\n00:00:00,000 --> 00:00:01,000\nMars rises tonight.\n\n2\n00:00:01,000 --> 00:00:02,000\nLook east.\n\n")]
 [InlineData(DocumentaryMediaAssetFormat.Vtt,"WEBVTT\n\n1\n00:00:00.000 --> 00:00:01.000\nMars rises tonight.\n\n2\n00:00:01.000 --> 00:00:02.000\nLook east.\n\n")]
 public async Task Valid_document_is_measured_from_actual_bytes(DocumentaryMediaAssetFormat format,string document)
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".sub");await File.WriteAllTextAsync(path,document);
  try{var result=await new DocumentarySubtitleInspector().InspectAsync(new(path,format,2000,"Mars rises tonight. Look east.",new(),0,true),default);result.Succeeded.Should().BeTrue();result.CueCount.Should().Be(2);result.FirstStartMilliseconds.Should().Be(0);result.LastEndMilliseconds.Should().Be(2000);result.ReconstructedText.Should().Be("Mars rises tonight. Look east.");}finally{File.Delete(path);}
 }
 [Theory]
 [InlineData("1\n00:00:00,000 --> 00:00:01,100\nMars.\n\n2\n00:00:01,000 --> 00:00:02,000\nEast.\n\n")]
 [InlineData("2\n00:00:00,000 --> 00:00:02,000\nMars.\n\n")]
 [InlineData("1\ninvalid\nMars.\n\n")]
 public async Task Invalid_srt_is_rejected(string document)
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".srt");await File.WriteAllTextAsync(path,document);
  try{var result=await new DocumentarySubtitleInspector().InspectAsync(new(path,DocumentaryMediaAssetFormat.Srt,2000,"Mars.",new(),0,true),default);result.Succeeded.Should().BeFalse();result.Failure.Should().NotBeNull();}finally{File.Delete(path);}
 }
 [Fact] public async Task Reconstruction_preserves_Hindi_Unicode_and_punctuation(){const string hindi="आज मंगल उगता है।";var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".srt");await File.WriteAllTextAsync(path,$"1\n00:00:00,000 --> 00:00:01,000\n{hindi}\n\n");try{var r=await new DocumentarySubtitleInspector().InspectAsync(new(path,DocumentaryMediaAssetFormat.Srt,1000,hindi,new(),0,true),default);r.Succeeded.Should().BeTrue();r.ReconstructedText.Should().Be(hindi);}finally{File.Delete(path);}}
}

public sealed class DocumentarySubtitleProviderBindingTests
{
 [Theory][InlineData(DocumentaryMediaLanguage.English)][InlineData(DocumentaryMediaLanguage.Hindi)]
 public async Task Existing_cues_produce_one_owned_deterministic_document_without_TTS(DocumentaryMediaLanguage language)
 {
  var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var text=language==DocumentaryMediaLanguage.Hindi?"आज मंगल उगता है।":"Mars rises tonight.";var reference=new DocumentaryMediaKnowledgeReference("r","p",default,"s","a","v","/",0,"c");var cue=new DocumentarySubtitleCue("cue",language,text,text,null,default,0,1000,0,"n",[reference],"c");var request=new DocumentarySubtitleProviderRequest("asset","source","scene","variant","c",language,"n",text,Path.Combine(root,"final.wav"),1000,DocumentaryMediaAssetFormat.Srt,1,root,new(),[cue]);
  try{var binding=new ExistingDocumentarySubtitleProviderBinding();var result=await binding.GenerateAsync(request,default);result.Failure.Should().BeNull();result.CueCount.Should().Be(1);result.OutputPath.Should().Be(Path.Combine(root,"provider-subtitles.srt"));File.Exists(result.OutputPath).Should().BeTrue();}finally{Directory.Delete(root,true);}
 }
 [Fact] public async Task Caller_cancellation_propagates(){using var cts=new CancellationTokenSource();cts.Cancel();var binding=new ExistingDocumentarySubtitleProviderBinding();var action=()=>binding.GenerateAsync(null!,cts.Token);await action.Should().ThrowAsync<OperationCanceledException>();}
}
