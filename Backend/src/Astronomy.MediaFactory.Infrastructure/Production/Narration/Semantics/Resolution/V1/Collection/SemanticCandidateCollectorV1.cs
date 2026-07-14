using System.Collections.Immutable;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Collection;
public interface ISemanticCandidateCollectorV1 { SemanticCandidateCollectionV1 Collect(SemanticResolutionRequestV1 request); }
public sealed class SemanticCandidateCollectorV1(ISemanticSourcePolicyCatalogV1 policies, ISemanticSourceAdapterRegistryV1 registry) : ISemanticCandidateCollectorV1
{
 public SemanticCandidateCollectionV1 Collect(SemanticResolutionRequestV1 r){
  if(!policies.TryGet(r.CapabilityId,out var p)) return Empty(r,null,["No source policy registered."]);
  var approved=p.ApprovedSources.Where(s=>s.ActiveInV1).OrderBy(s=>s.Priority).ThenBy(s=>s.SourceId,StringComparer.Ordinal).ToArray();
  var all=registry.GetAdapters(r.CapabilityId).Where(a=>a.SourceId!=SemanticSourcePolicyVocabularyV1.LegacyRawJsonScanner).ToArray();
  var approvedAdapters=approved.Select(s=>all.FirstOrDefault(a=>a.SourceId==s.SourceId)).Where(a=>a is not null).Cast<ISemanticSourceAdapterV1>().ToArray();
  var missing=approved.Where(s=>!all.Any(a=>a.SourceId==s.SourceId)).Select(s=>s.SourceId).ToImmutableArray();
  var skipped=all.Where(a=>!approved.Any(s=>s.SourceId==a.SourceId)).Select(a=>a.AdapterId).Order(StringComparer.Ordinal).ToImmutableArray();
  var results=new List<SemanticSourceAdapterResultV1>(); var candidates=new List<SemanticSourceCandidateV1>(); var rejections=new List<SemanticSourceRejectionV1>(); var invoked=new List<string>(); var warnings=new List<string>(); var errors=new List<string>();
  foreach(var a in approvedAdapters.OrderBy(a=>approved.First(s=>s.SourceId==a.SourceId).Priority).ThenBy(a=>a.AdapterId,StringComparer.Ordinal)){
   invoked.Add(a.AdapterId); var res=a.TryExtract(r.AdapterContext); results.Add(res);
   if(res.Status==SemanticSourceAdapterStatusV1.Resolved&&res.Candidate is not null)candidates.Add(res.Candidate);
   else if(res.Rejection is not null){rejections.Add(res.Rejection); if(res.Status==SemanticSourceAdapterStatusV1.UnsupportedSourceShape)warnings.Add(res.Rejection.Reason);} }
  return new(r.CapabilityId,r.CapabilityId.Value,p.PolicyVersion,results.ToImmutableArray(),candidates.OrderBy(c=>c.AdapterId,StringComparer.Ordinal).ThenBy(c=>c.CanonicalValue,StringComparer.Ordinal).ToImmutableArray(),rejections.ToImmutableArray(),missing,invoked.ToImmutableArray(),skipped,warnings.ToImmutableArray(),errors.ToImmutableArray(),invoked.ToImmutableArray(),p);
 }
 static SemanticCandidateCollectionV1 Empty(SemanticResolutionRequestV1 r, SemanticSourcePolicyV1? p, ImmutableArray<string> errors)=>new(r.CapabilityId,p?.SemanticCapabilityId.Value,p?.PolicyVersion,[],[],[],[],[],[],[],errors,[],p);
}
