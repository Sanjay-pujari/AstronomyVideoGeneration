using System.Collections.ObjectModel;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
public sealed class AstronomyKnowledgeCatalogEntry
{
    public AstronomyKnowledgeCatalogEntry(AstronomyKnowledgeCatalogEntryId id,string code,string displayName,string description,int order,AstronomyKnowledgeDomain? domain=null,AstronomyKnowledgePayloadFamily? family=null,AstronomyKnowledgeTypeId? knowledgeTypeId=null,Type? runtimeType=null,IReadOnlyDictionary<string,string>? metadata=null){
        if(string.IsNullOrWhiteSpace(id.Value)) throw new ArgumentException("Catalog entry ID is required.",nameof(id));
        Id=id; Code=Req(code,nameof(code)); DisplayName=Req(displayName,nameof(displayName)); Description=Req(description,nameof(description)); if(order<0) throw new ArgumentOutOfRangeException(nameof(order)); Order=order;
        if(domain is { } d && !Enum.IsDefined(d)) throw new ArgumentOutOfRangeException(nameof(domain)); if(family is { } f && !Enum.IsDefined(f)) throw new ArgumentOutOfRangeException(nameof(family));
        Domain=domain; Family=family; KnowledgeTypeId=knowledgeTypeId; RuntimeType=runtimeType; Metadata=new ReadOnlyDictionary<string,string>(metadata is null? new Dictionary<string,string>(StringComparer.Ordinal) : new Dictionary<string,string>(metadata,StringComparer.Ordinal));
    }
    static string Req(string v,string n)=> string.IsNullOrWhiteSpace(v)? throw new ArgumentException("Value is required.",n):v.Trim();
    public AstronomyKnowledgeCatalogEntryId Id{get;} public AstronomyKnowledgeCatalogEntryKind Kind=>Id.Kind; public string Code{get;} public string DisplayName{get;} public string Description{get;} public int Order{get;} public AstronomyKnowledgeDomain? Domain{get;} public AstronomyKnowledgePayloadFamily? Family{get;} public AstronomyKnowledgeTypeId? KnowledgeTypeId{get;} public Type? RuntimeType{get;} public IReadOnlyDictionary<string,string> Metadata{get;}
}
