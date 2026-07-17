using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed class ExecutionContractRegistry : IExecutionContractRegistry
{
    private readonly ImmutableArray<DomainExecutionContract> domains;
    private readonly ImmutableDictionary<string, DomainExecutionContract> domainsById;
    private readonly ImmutableDictionary<string, ImmutableArray<Entry>> globalIndex;
    private readonly ImmutableDictionary<string, ImmutableDictionary<string, Entry>> domainIndexes;

    public ExecutionContractRegistry(IEnumerable<DomainExecutionContract> domains)
    {
        ArgumentNullException.ThrowIfNull(domains);
        this.domains = domains.ToImmutableArray();
        var domainBuilder = ImmutableDictionary.CreateBuilder<string, DomainExecutionContract>(StringComparer.OrdinalIgnoreCase);
        var globalBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<Entry>.Builder>(StringComparer.OrdinalIgnoreCase);
        var domainIndexBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, Entry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in this.domains)
        {
            if (!domainBuilder.TryAdd(domain.DomainId, domain)) throw new ArgumentException($"Duplicate domain id '{domain.DomainId}'.", nameof(domains));
            var local = ImmutableDictionary.CreateBuilder<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var family in domain.Families)
            {
                Add(local, domain.DomainId, family.FamilyId, new Entry(domain.DomainId, family, FamilyContractMatchKind.CanonicalFamilyId));
                foreach (var alias in family.Aliases) Add(local, domain.DomainId, alias, new Entry(domain.DomainId, family, FamilyContractMatchKind.Alias));
            }
            domainIndexBuilder.Add(domain.DomainId, local.ToImmutable());
            foreach (var pair in local)
            {
                if (!globalBuilder.TryGetValue(pair.Key, out var list)) { list = ImmutableArray.CreateBuilder<Entry>(); globalBuilder.Add(pair.Key, list); }
                list.Add(pair.Value);
            }
        }
        foreach (var pair in globalBuilder)
        {
            var entries = pair.Value.ToImmutable();
            if (entries.Length > 1 && entries.Any(e => e.MatchKind == FamilyContractMatchKind.Alias))
            {
                throw new ArgumentException($"Family identity '{pair.Key}' has a cross-domain alias or canonical conflict.", nameof(domains));
            }
        }
        this.domainsById = domainBuilder.ToImmutable();
        this.domainIndexes = domainIndexBuilder.ToImmutable();
        this.globalIndex = globalBuilder.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutable(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<DomainExecutionContract> Domains => domains;

    public FamilyContractResolution ResolveFamily(string familyIdOrAlias, string? domainId = null)
    {
        var identity = familyIdOrAlias?.Trim() ?? string.Empty;
        var requestedDomain = ExecutionContractGuard.NormalizeOptional(domainId);
        if (identity.Length == 0) return new(FamilyContractResolutionStatus.InvalidRequest, identity, requestedDomain, null, null, FamilyContractMatchKind.None, null, "Family identity must be non-empty.");
        if (requestedDomain is not null)
        {
            if (!domainIndexes.TryGetValue(requestedDomain, out var index)) return new(FamilyContractResolutionStatus.NotFound, identity, requestedDomain, null, null, FamilyContractMatchKind.None, null, $"Domain '{requestedDomain}' is not registered.");
            return index.TryGetValue(identity, out var entry) ? Resolved(identity, requestedDomain, entry) : new(FamilyContractResolutionStatus.NotFound, identity, requestedDomain, null, null, FamilyContractMatchKind.None, null, $"Family identity '{identity}' was not found in domain '{requestedDomain}'.");
        }
        if (!globalIndex.TryGetValue(identity, out var matches)) return new(FamilyContractResolutionStatus.NotFound, identity, null, null, null, FamilyContractMatchKind.None, null, $"Family identity '{identity}' was not found.");
        if (matches.Length > 1) return new(FamilyContractResolutionStatus.NotFound, identity, null, null, null, FamilyContractMatchKind.None, null, $"Family identity '{identity}' is ambiguous across domains; provide a domain id.");
        return Resolved(identity, null, matches[0]);
    }

    public bool TryResolveFamily(string familyIdOrAlias, out FamilyExecutionContract? contract, string? domainId = null)
    {
        var result = ResolveFamily(familyIdOrAlias, domainId);
        contract = result.Contract;
        return result.Status == FamilyContractResolutionStatus.Resolved;
    }

    private static FamilyContractResolution Resolved(string requestedIdentity, string? requestedDomainId, Entry entry) => new(FamilyContractResolutionStatus.Resolved, requestedIdentity, requestedDomainId, entry.DomainId, entry.Contract.FamilyId, entry.MatchKind, entry.Contract, $"Family identity '{requestedIdentity}' resolved to '{entry.Contract.FamilyId}' in domain '{entry.DomainId}'.");

    private static void Add(IDictionary<string, Entry> index, string domainId, string identity, Entry entry)
    {
        if (index.TryGetValue(identity, out var existing)) throw new ArgumentException($"Family identity '{identity}' in domain '{domainId}' conflicts with family '{existing.Contract.FamilyId}'.", nameof(index));
        var sameFamilyVersion = index.Values.FirstOrDefault(e => string.Equals(e.Contract.FamilyId, entry.Contract.FamilyId, StringComparison.OrdinalIgnoreCase));
        if (sameFamilyVersion is not null && !string.Equals(sameFamilyVersion.Contract.ContractVersion, entry.Contract.ContractVersion, StringComparison.Ordinal)) throw new ArgumentException($"Conflicting contract versions for family '{entry.Contract.FamilyId}' in domain '{domainId}'.", nameof(index));
        index.Add(identity, entry);
    }

    private sealed record Entry(string DomainId, FamilyExecutionContract Contract, FamilyContractMatchKind MatchKind);
}
