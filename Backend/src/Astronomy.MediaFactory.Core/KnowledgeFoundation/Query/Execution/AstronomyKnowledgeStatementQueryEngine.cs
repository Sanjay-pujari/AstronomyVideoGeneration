using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;

public sealed class AstronomyKnowledgeStatementQueryEngine(IAstronomyKnowledgeQueryValidator validator, IAstronomyTypedPayloadRegistry registry) : IAstronomyKnowledgeStatementQueryEngine
{
    readonly IAstronomyKnowledgeQueryValidator validator = validator ?? throw new ArgumentNullException(nameof(validator));
    readonly IAstronomyTypedPayloadRegistry registry = registry ?? throw new ArgumentNullException(nameof(registry));
    public AstronomyKnowledgeStatementQueryResult Execute(AstronomyKnowledgeStatementQuery query, IEnumerable<IAstronomyKnowledgeStatement> statements)
    {
        ArgumentNullException.ThrowIfNull(query); ArgumentNullException.ThrowIfNull(statements);
        var validation = validator.Validate(query);
        if (!validation.IsValid) return new(AstronomyKnowledgeQueryExecutionStatus.Rejected, [], Meta(query,0,0,0,false), validation.Issues);
        var source = statements.Select((s,i)=>s??throw new ArgumentException($"Statement source contains null at index {i}.",nameof(statements))).ToArray();
        var matched = source.Where(s => Match(query, s)).ToArray();
        var ordered = matched.Order(new StatementComparer(query.Ordering)).ToArray();
        var page = ordered.Skip(query.Page.Offset).Take(query.Page.Limit).ToArray();
        return new(AstronomyKnowledgeQueryExecutionStatus.Succeeded, page, Meta(query, source.Length, matched.Length, page.Length, query.Page.Offset + page.Length < matched.Length));
    }
    static AstronomyKnowledgeQueryExecutionMetadata Meta(AstronomyKnowledgeStatementQuery q,int source,int matched,int returned,bool more)=>new(AstronomyKnowledgeQueryTarget.Statement,q.Fingerprint,source,matched,returned,q.Page.Offset,q.Page.Limit,more,q.Ordering.Count);
    bool Match(AstronomyKnowledgeStatementQuery q,IAstronomyKnowledgeStatement s)=> Ids(q.StatementIds,s.Id)&&Subject(q.Subjects,s.PrimarySubject)&&Filter(q.Domains, Typed(s)?.Domain)&&Filter(q.Families, Typed(s)?.Family)&&TypeFilter(q.KnowledgeTypes, Typed(s))&&Filter(q.StatementKinds,s.Kind)&&Filter(q.Statuses,s.Status)&&Version(q.Version,s.Version)&&Provenance(q.Provenance,s.Audit.CreatedBy);
    static ITypedAstronomyKnowledgePayload? Typed(IAstronomyKnowledgeStatement s)=>s.Payload as ITypedAstronomyKnowledgePayload;
    static bool Ids(IReadOnlyList<KnowledgeId>? ids,KnowledgeId id)=>ids is null||ids.Contains(id);
    static bool Subject(AstronomyKnowledgeSubjectFilter? f,AstronomyEntityReference actual)=>f is null || (f.MatchMode==AstronomyKnowledgeQueryMatchMode.Any ? f.Values.Any(v=>v==actual) : f.Values.Distinct().Count()==1 && f.Values.Any(v=>v==actual));
    static bool Filter<T>(AstronomyKnowledgeFilter<T>? f,T actual)=>f is null || (f.MatchMode==AstronomyKnowledgeQueryMatchMode.Any ? f.Values.Contains(actual) : f.Values.Distinct().Count()==1 && f.Values.Contains(actual));
    static bool Filter<T>(AstronomyKnowledgeFilter<T>? f,T? actual) where T:struct=>f is null || (actual.HasValue && Filter(f,actual.Value));
    bool TypeFilter(AstronomyKnowledgeTypeFilter? f,ITypedAstronomyKnowledgePayload? p)=>f is null || (p is not null && registry.TryGetByPayloadType(p.GetType(), out var d) && Filter(f, new AstronomyKnowledgeTypeId(d.Discriminator)));
    static bool Version(AstronomyKnowledgeVersionFilter? f,KnowledgeVersion v)=>f is null||f.IsEmpty||(f.ExactRevision.HasValue?v==f.ExactRevision.Value:(f.MinimumRevision is null||v>=f.MinimumRevision.Value)&&(f.MaximumRevision is null||v<=f.MaximumRevision.Value));
    static bool Provenance(AstronomyKnowledgeProvenanceFilter? f,string? createdBy)=>f?.CreatedBy is null || (createdBy is not null && f.CreatedBy.Contains(createdBy,StringComparer.Ordinal));
    sealed class StatementComparer(IReadOnlyList<AstronomyKnowledgeStatementOrder> orders):IComparer<IAstronomyKnowledgeStatement>{public int Compare(IAstronomyKnowledgeStatement? x,IAstronomyKnowledgeStatement? y){if(ReferenceEquals(x,y))return 0;if(x is null)return 1;if(y is null)return -1;foreach(var o in orders){var c=o.Field switch{AstronomyKnowledgeStatementSortField.Id=>StringComparer.Ordinal.Compare(x.Id.Value,y.Id.Value),AstronomyKnowledgeStatementSortField.Subject=>StringComparer.Ordinal.Compare(x.PrimarySubject.EntityId,y.PrimarySubject.EntityId),AstronomyKnowledgeStatementSortField.Kind=>x.Kind.CompareTo(y.Kind),AstronomyKnowledgeStatementSortField.Status=>x.Status.CompareTo(y.Status),AstronomyKnowledgeStatementSortField.Revision=>x.Version.CompareTo(y.Version),AstronomyKnowledgeStatementSortField.CreatedAt=>x.Audit.CreatedUtc.CompareTo(y.Audit.CreatedUtc),AstronomyKnowledgeStatementSortField.UpdatedAt=>NullableCompare(x.Audit.UpdatedUtc,y.Audit.UpdatedUtc,o.Direction),_=>0}; if(c!=0)return o.Direction==AstronomyKnowledgeQuerySortDirection.Descending?-c:c;} var t=StringComparer.Ordinal.Compare(x.Id.Value,y.Id.Value); return t!=0?t:x.Version.CompareTo(y.Version);} static int NullableCompare(DateTimeOffset? a,DateTimeOffset? b,AstronomyKnowledgeQuerySortDirection d){if(a.HasValue&&b.HasValue)return a.Value.CompareTo(b.Value); if(!a.HasValue&&!b.HasValue)return 0; return a.HasValue? (d==AstronomyKnowledgeQuerySortDirection.Ascending?-1:1) : (d==AstronomyKnowledgeQuerySortDirection.Ascending?1:-1);}}
}
