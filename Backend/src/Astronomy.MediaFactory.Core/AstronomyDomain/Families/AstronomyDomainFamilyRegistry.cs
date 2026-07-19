using Astronomy.MediaFactory.Core.AstronomyDomain.Identity;
namespace Astronomy.MediaFactory.Core.AstronomyDomain.Families;
public sealed class AstronomyDomainFamilyRegistry:IAstronomyDomainFamilyRegistry
{
    private readonly IReadOnlyList<IAstronomyDomainFamily> _families; private readonly Dictionary<string,IAstronomyDomainFamily> _byId; private readonly Dictionary<string,IAstronomyDomainFamily> _byAlias;
    public AstronomyDomainFamilyRegistry(IEnumerable<IAstronomyDomainFamily> families){ ArgumentNullException.ThrowIfNull(families); _families=families.OrderBy(f=>f.FamilyId,StringComparer.OrdinalIgnoreCase).ToArray(); _byId=new(StringComparer.OrdinalIgnoreCase); _byAlias=new(StringComparer.OrdinalIgnoreCase); foreach(var f in _families){ var id=N(f.FamilyId); if(string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Astronomy family ID cannot be empty."); if(!_byId.TryAdd(id,f)) throw new InvalidOperationException($"Duplicate astronomy family ID '{id}'."); foreach(var a in f.SupportedEventTypeAliases.Append(f.FamilyId)){ var alias=N(a); if(string.IsNullOrWhiteSpace(alias)) throw new InvalidOperationException($"Astronomy family '{f.FamilyId}' registered an empty EventType alias."); if(_byAlias.TryGetValue(alias,out var existing)&&!string.Equals(existing.FamilyId,f.FamilyId,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Duplicate astronomy EventType alias '{alias}' for families '{existing.FamilyId}' and '{f.FamilyId}'."); _byAlias[alias]=f; } } }
    public IReadOnlyList<IAstronomyDomainFamily> Families=>_families;
    public IAstronomyDomainFamily ResolveByFamilyId(string familyId)=>TryResolveByFamilyId(familyId,out var f)?f!:throw new KeyNotFoundException($"Unknown astronomy family ID '{familyId}'.");
    public bool TryResolveByFamilyId(string familyId,out IAstronomyDomainFamily? family)=>_byId.TryGetValue(N(familyId),out family);
    public IAstronomyDomainFamily ResolveByEventType(string eventType)=>TryResolveByEventType(eventType,out var f)?f!:throw new KeyNotFoundException($"Unknown astronomy EventType '{eventType}'.");
    public bool TryResolveByEventType(string eventType,out IAstronomyDomainFamily? family)=>_byAlias.TryGetValue(N(eventType),out family);
    public IAstronomyDomainFamily ResolveForEntity(AstronomyEntityIdentity identity)=>TryResolveForEntity(identity,out var f)?f!:throw new KeyNotFoundException($"No astronomy family supports entity '{identity.EntityId}' with family '{identity.FamilyId}'.");
    public bool TryResolveForEntity(AstronomyEntityIdentity identity,out IAstronomyDomainFamily? family){ ArgumentNullException.ThrowIfNull(identity); if(TryResolveByFamilyId(identity.FamilyId,out family)&&family!.Supports(identity)) return true; family=null; return false;}
    private static string N(string? v)=>v?.Trim()??string.Empty;
}
