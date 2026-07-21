using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;

public sealed class AstronomyKnowledgeCatalogQueryEngine(IAstronomyKnowledgeCatalog catalog, IAstronomyKnowledgeQueryValidator validator) : IAstronomyKnowledgeCatalogQueryEngine
{
    readonly IAstronomyKnowledgeCatalog catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    readonly IAstronomyKnowledgeQueryValidator validator = validator ?? throw new ArgumentNullException(nameof(validator));
    public AstronomyKnowledgeCatalogQueryResult Execute(AstronomyKnowledgeCatalogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = validator.Validate(query);
        if (!validation.IsValid) return new(AstronomyKnowledgeQueryExecutionStatus.Rejected, [], Meta(query, 0, 0, 0, false), validation.Issues);
        var source = catalog.Snapshot.Entries.ToArray();
        var matched = source.Where(e => Match(query, e)).ToArray();
        var ordered = matched.Order(new CatalogComparer(query.Ordering)).ToArray();
        var page = ordered.Skip(query.Page.Offset).Take(query.Page.Limit).ToArray();
        return new(AstronomyKnowledgeQueryExecutionStatus.Succeeded, page, Meta(query, source.Length, matched.Length, page.Length, query.Page.Offset + page.Length < matched.Length));
    }
    static AstronomyKnowledgeQueryExecutionMetadata Meta(AstronomyKnowledgeCatalogQuery q,int source,int matched,int returned,bool more)=>new(AstronomyKnowledgeQueryTarget.CatalogEntry,q.Fingerprint,source,matched,returned,q.Page.Offset,q.Page.Limit,more,q.Ordering.Count);
    static bool Match(AstronomyKnowledgeCatalogQuery q,AstronomyKnowledgeCatalogEntry e)=> List(q.EntryIds,e.Id) && List(q.EntryKinds,e.Kind) && Strings(q.Codes,e.Code) && Filter(q.Domains,e.Domain) && Filter(q.Families,e.Family) && TypeFilter(q.KnowledgeTypes,e.KnowledgeTypeId);
    static bool List<T>(IReadOnlyList<T>? values,T actual)=>values is null || values.Contains(actual);
    static bool Strings(IReadOnlyList<string>? values,string actual)=>values is null || values.Contains(actual,StringComparer.Ordinal);
    static bool Filter<T>(AstronomyKnowledgeFilter<T>? filter,T? actual) where T:struct=>filter is null || (actual.HasValue && Values(filter, actual.Value));
    static bool TypeFilter(AstronomyKnowledgeTypeFilter? filter, Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgeTypeId? actual)=>filter is null || (actual.HasValue && Values(filter, actual.Value));
    static bool Values<T>(AstronomyKnowledgeFilter<T> f,T actual)=>f.MatchMode==AstronomyKnowledgeQueryMatchMode.Any ? f.Values.Contains(actual) : f.Values.Distinct().Count()==1 && f.Values.Contains(actual);
    sealed class CatalogComparer(IReadOnlyList<AstronomyKnowledgeCatalogOrder> orders):IComparer<AstronomyKnowledgeCatalogEntry>{public int Compare(AstronomyKnowledgeCatalogEntry? x,AstronomyKnowledgeCatalogEntry? y){ if(ReferenceEquals(x,y))return 0; if(x is null)return 1; if(y is null)return -1; foreach(var o in orders){var c=o.Field switch{AstronomyKnowledgeCatalogSortField.Kind=>x.Kind.CompareTo(y.Kind),AstronomyKnowledgeCatalogSortField.Order=>x.Order.CompareTo(y.Order),AstronomyKnowledgeCatalogSortField.Code=>StringComparer.Ordinal.Compare(x.Code,y.Code),AstronomyKnowledgeCatalogSortField.Id=>CompareId(x.Id,y.Id),AstronomyKnowledgeCatalogSortField.DisplayName=>StringComparer.Ordinal.Compare(x.DisplayName,y.DisplayName),_=>0}; if(c!=0)return o.Direction==AstronomyKnowledgeQuerySortDirection.Descending?-c:c;} return CompareId(x.Id,y.Id);} static int CompareId(AstronomyKnowledgeCatalogEntryId a,AstronomyKnowledgeCatalogEntryId b){var c=a.Kind.CompareTo(b.Kind); return c!=0?c:StringComparer.Ordinal.Compare(a.Value,b.Value);}} 
}
