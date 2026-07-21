namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
public readonly record struct AstronomyKnowledgeCatalogEntryId
{
    public AstronomyKnowledgeCatalogEntryId(AstronomyKnowledgeCatalogEntryKind kind,string value){ if(!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind)); if(string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Catalog entry ID value is required.",nameof(value)); Kind=kind; Value=value.Trim(); }
    public AstronomyKnowledgeCatalogEntryKind Kind{get;} public string Value{get;} public override string ToString()=> $"{Kind}:{Value}";
}
