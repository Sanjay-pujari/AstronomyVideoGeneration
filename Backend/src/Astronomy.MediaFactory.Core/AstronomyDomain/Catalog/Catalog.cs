using Astronomy.MediaFactory.Core.AstronomyDomain.Entities;
using Astronomy.MediaFactory.Core.AstronomyDomain.Relationships;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;

namespace Astronomy.MediaFactory.Core.AstronomyDomain.Catalog;

public sealed record AstronomyDomainQuery(
    IReadOnlySet<AstronomyEntityKind>? EntityKinds = null,
    IReadOnlySet<string>? FamilyIds = null,
    IReadOnlySet<AstronomyDomainCategory>? DomainCategories = null,
    string? LanguageCode = null,
    string? SearchText = null,
    IReadOnlySet<string>? Tags = null,
    bool IncludeDeprecated = false,
    int Limit = InMemoryAstronomyDomainCatalog.DefaultQueryLimit);

public interface IAstronomyDomainCatalog
{
    Task<IAstronomyDomainEntity?> GetByIdAsync(string entityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IAstronomyDomainEntity>> SearchAsync(AstronomyDomainQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AstronomyRelationship>> GetRelationshipsAsync(string entityId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryAstronomyDomainCatalog : IAstronomyDomainCatalog
{
    public const int DefaultQueryLimit = 100;

    private readonly Dictionary<string, IAstronomyDomainEntity> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliasToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public InMemoryAstronomyDomainCatalog(IEnumerable<IAstronomyDomainEntity>? entities = null)
    {
        foreach (var entity in entities ?? [])
            Add(entity);
    }

    public void Add(IAstronomyDomainEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var entityId = entity.Identity.EntityId;
        var aliases = GetLookupAliases(entity).ToArray();

        lock (_gate)
        {
            if (_byId.ContainsKey(entityId))
                throw new InvalidOperationException($"Duplicate astronomy EntityId '{entityId}'.");

            var duplicateAlias = aliases
                .GroupBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicateAlias is not null)
                throw new InvalidOperationException($"Duplicate astronomy alias '{duplicateAlias}'.");

            foreach (var alias in aliases)
            {
                if (_aliasToId.TryGetValue(alias, out var existingId)
                    && !string.Equals(existingId, entityId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Conflicting astronomy alias '{alias}'.");

                if (_byId.ContainsKey(alias))
                    throw new InvalidOperationException($"Astronomy alias '{alias}' conflicts with an existing EntityId.");
            }

            if (_aliasToId.ContainsKey(entityId))
                throw new InvalidOperationException($"Astronomy EntityId '{entityId}' conflicts with an existing alias.");

            _byId.Add(entityId, entity);
            foreach (var alias in aliases)
                _aliasToId[alias] = entityId;
        }
    }

    public Task<IAstronomyDomainEntity?> GetByIdAsync(string entityId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entityId);

        var lookup = entityId.Trim();
        if (lookup.Length == 0)
            return Task.FromResult<IAstronomyDomainEntity?>(null);

        lock (_gate)
        {
            if (_byId.TryGetValue(lookup, out var entity))
                return Task.FromResult<IAstronomyDomainEntity?>(entity);

            if (_aliasToId.TryGetValue(lookup, out var id) && _byId.TryGetValue(id, out entity))
                return Task.FromResult<IAstronomyDomainEntity?>(entity);

            return Task.FromResult<IAstronomyDomainEntity?>(null);
        }
    }

    public Task<IReadOnlyList<IAstronomyDomainEntity>> SearchAsync(AstronomyDomainQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);

        lock (_gate)
        {
            IEnumerable<IAstronomyDomainEntity> results = _byId.Values;

            if (!query.IncludeDeprecated)
                results = results.Where(e => e.Metadata.Status != AstronomyContentStatus.Deprecated && e.Metadata.Status != AstronomyContentStatus.Archived);

            if (query.FamilyIds?.Count > 0)
                results = results.Where(e => query.FamilyIds.Contains(e.Identity.FamilyId, StringComparer.OrdinalIgnoreCase));

            if (query.EntityKinds?.Count > 0)
                results = results.Where(e => query.EntityKinds.Contains(e.Identity.EntityKind));

            if (query.DomainCategories?.Count > 0)
                results = results.Where(e => query.DomainCategories.Contains(e.Identity.DomainCategory));

            if (query.Tags?.Count > 0)
                results = results.Where(e => query.Tags.All(t => e.Classification.Tags.Contains(t, StringComparer.OrdinalIgnoreCase) || e.Metadata.Keywords.Contains(t, StringComparer.OrdinalIgnoreCase)));

            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                var searchText = query.SearchText.Trim();
                results = results.Where(e =>
                    e.Identity.CanonicalName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    || e.Identity.Aliases.Any(a => a.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    || e.Localizations.Any(l =>
                        (string.IsNullOrWhiteSpace(query.LanguageCode) || l.LanguageCode.Equals(query.LanguageCode.Trim(), StringComparison.OrdinalIgnoreCase))
                        && (l.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) || l.SearchAliases.Any(a => a.Contains(searchText, StringComparison.OrdinalIgnoreCase)))));
            }

            var limit = query.Limit <= 0 ? DefaultQueryLimit : query.Limit;
            return Task.FromResult<IReadOnlyList<IAstronomyDomainEntity>>(results
                .OrderBy(e => e.Identity.EntityId, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToArray());
        }
    }

    public Task<IReadOnlyList<AstronomyRelationship>> GetRelationshipsAsync(string entityId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entityId);

        var lookup = entityId.Trim();
        if (lookup.Length == 0)
            return Task.FromResult<IReadOnlyList<AstronomyRelationship>>([]);

        lock (_gate)
        {
            var relationships = _byId.Values
                .SelectMany(e => e.Relationships)
                .Where(x => x.SourceEntityId.Equals(lookup, StringComparison.OrdinalIgnoreCase) || x.TargetEntityId.Equals(lookup, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.RelationshipId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Task.FromResult<IReadOnlyList<AstronomyRelationship>>(relationships);
        }
    }

    private static IEnumerable<string> GetLookupAliases(IAstronomyDomainEntity entity)
    {
        return entity.Identity.Aliases
            .Concat(entity.Localizations.SelectMany(l => l.SearchAliases))
            .Select(alias => alias?.Trim() ?? string.Empty)
            .Where(alias => alias.Length > 0);
    }
}
