using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Schema-aware deterministic event/evergreen merger; unknown JSON sections never become claims.</summary>
public sealed class Phase7KnowledgeResolver : IPhase7KnowledgeResolver
{
    private static readonly IReadOnlyDictionary<string, NarrationKnowledgeDomainKey> Sections = new Dictionary<string, NarrationKnowledgeDomainKey>(StringComparer.OrdinalIgnoreCase)
    {
        ["identity"]=NarrationKnowledgeDomainKey.Identity, ["scientific"]=NarrationKnowledgeDomainKey.ScientificStructure,
        ["observation"]=NarrationKnowledgeDomainKey.Observation, ["astrophotography"]=NarrationKnowledgeDomainKey.Astrophotography,
        ["history"]=NarrationKnowledgeDomainKey.History, ["cultureAndMythology"]=NarrationKnowledgeDomainKey.CultureAndMythology,
        ["astrologyRelationships"]=NarrationKnowledgeDomainKey.AstrologyClarification, ["editorialSafety"]=NarrationKnowledgeDomainKey.EditorialSafety,
        ["interestingFacts"]=NarrationKnowledgeDomainKey.InterestingFacts, ["localizedContent"]=NarrationKnowledgeDomainKey.LocalizedContent,
        ["timing"]=NarrationKnowledgeDomainKey.Timing, ["visibility"]=NarrationKnowledgeDomainKey.Visibility,
        ["geometry"]=NarrationKnowledgeDomainKey.Geometry, ["objects"]=NarrationKnowledgeDomainKey.KeyObjects,
        ["meteor"]=NarrationKnowledgeDomainKey.ActivityRate, ["eclipse"]=NarrationKnowledgeDomainKey.ContactTimeline,
        ["conjunction"]=NarrationKnowledgeDomainKey.Geometry, ["comet"]=NarrationKnowledgeDomainKey.Orbit,
        ["satellite"]=NarrationKnowledgeDomainKey.OrbitalMotion
    };
    private static readonly HashSet<string> NonFacts = new(StringComparer.OrdinalIgnoreCase) { "sourceIds","useCases","factId","stableKnowledgeId","externalId","familyCode","objectType","objectRole","catalogId","canonicalName","displayName","pronunciation","notes","label","id","name" };

    public ResolvedNarrationKnowledge Resolve(CertifiedKnowledgePayload payload, FamilyNarrationProfile profile)
    {
        if (!Certified(payload.VerificationStatus) || payload.ReviewedSources.Count == 0) return Empty(payload, profile, "P7KNOWLEDGE_NOT_CERTIFIED");
        var candidates = new List<Candidate>(); var issues = new List<string>();
        Read(payload.EvergreenJson, "evergreen", payload.EvergreenPayloadId ?? payload.PayloadId, candidates, issues, payload);
        Read(payload.RawDataJson, "event", payload.EventId, candidates, issues, payload);
        var merged = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var c in candidates.OrderBy(x => x.Origin == "evergreen" ? 0 : 1).ThenBy(x => x.Path, StringComparer.Ordinal))
        {
            if (merged.TryGetValue(c.SemanticKey, out var old) && old.Text != c.Text && old.Origin == c.Origin)
                issues.Add($"P7KNOWLEDGE_CERTIFIED_CONFLICT:{c.SemanticKey}");
            else merged[c.SemanticKey] = c; // certified event-specific fact has deterministic precedence
        }
        var claims = merged.Values.Select(c => Claim(c, payload)).OrderBy(x => x.ClaimId, StringComparer.Ordinal).ToArray();
        var required = profile.MandatoryKnowledgeDomains.Select(ParseDomain).ToHashSet();
        var domains = Enum.GetValues<NarrationKnowledgeDomainKey>().Select(key =>
        {
            var domainClaims = claims.Where(c => c.Domain == key.ToString()).ToArray();
            var mandatory = required.Contains(key);
            return new NarrationKnowledgeDomain(key.ToString(), domainClaims.Length > 0 ? KnowledgeDomainStatus.Available : mandatory ? KnowledgeDomainStatus.Missing : KnowledgeDomainStatus.NotApplicable,
                domainClaims, mandatory && domainClaims.Length == 0 ? [$"P7KNOWLEDGE_MANDATORY_DOMAIN_MISSING:{key}"] : []);
        }).ToArray();
        issues.AddRange(domains.Where(x => x.Status == KnowledgeDomainStatus.Missing).Select(x => x.Warnings[0]));
        var vocab = Localized(payload.EvergreenJson, payload.Language, "narrationVocabulary");
        var pronunciation = LocalizedScalar(payload.EvergreenJson, payload.Language, "pronunciation");
        var result = new ResolvedNarrationKnowledge(payload.PayloadId, payload.PayloadChecksum, payload.SourceRegistryId,
            Phase7Determinism.Hash(payload.ReviewedSources.OrderBy(x => x.SourceId)), payload.Language, domains, vocab,
            Localized(payload.EvergreenJson, payload.Language, "doNotBlindlyTranslate").Keys.ToArray(), pronunciation,
            payload.ReviewedSources.Select(x => x.SourceId).ToArray(), payload.Warnings, issues.Distinct().ToArray(), "");
        return result with { DeterministicChecksum = Phase7Determinism.Hash(result with { DeterministicChecksum = "" }) };
    }

    private static void Read(string? json, string origin, string knowledgeId, List<Candidate> output, List<string> issues, CertifiedKnowledgePayload payload)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var section in doc.RootElement.EnumerateObject())
                if (Sections.TryGetValue(section.Name, out var domain)) Extract(section.Value, section.Name, domain, origin, knowledgeId, [], output);
        }
        catch (JsonException) { issues.Add($"P7KNOWLEDGE_{origin.ToUpperInvariant()}_JSON_INVALID"); }
    }
    private static void Extract(JsonElement value, string path, NarrationKnowledgeDomainKey domain, string origin, string knowledgeId, string[] inheritedSources, List<Candidate> output)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var sources = value.TryGetProperty("sourceIds", out var s) && s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).ToArray() : inheritedSources;
            var entity = value.TryGetProperty("objectId", out var oid) && oid.ValueKind == JsonValueKind.String ? oid.GetString()! : knowledgeId;
            if (value.TryGetProperty("objectId", out oid) && oid.ValueKind == JsonValueKind.String
                && value.TryGetProperty("objectName", out var objectName) && objectName.ValueKind == JsonValueKind.String)
            {
                var type = value.TryGetProperty("objectType", out var objectType) && objectType.ValueKind == JsonValueKind.String ? objectType.GetString() : "astronomical object";
                output.Add(new(origin, entity, "objects.identity", NarrationKnowledgeDomainKey.KeyObjects, $"{objectName.GetString()} is a certified {type} in this subject.", sources));
            }
            foreach (var p in value.EnumerateObject()) if (!NonFacts.Contains(p.Name)) Extract(p.Value, path + "." + p.Name, Refine(domain, p.Name), origin, entity, sources, output);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var i=0; foreach (var item in value.EnumerateArray()) { Extract(item, path + (item.ValueKind == JsonValueKind.Object ? "" : $".{i}"), domain, origin, knowledgeId, inheritedSources, output); i++; }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            var text=value.GetString(); if (!string.IsNullOrWhiteSpace(text)) output.Add(new(origin, knowledgeId, CanonicalPath(path, text!), domain, text!.Trim(), inheritedSources));
        }
        else if (value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number)
            output.Add(new(origin, knowledgeId, CanonicalPath(path, value.GetRawText()), domain, $"{Humanize(path.Split('.').Last())}: {value.GetRawText()}", inheritedSources));
    }
    private static CertifiedNarrationClaim Claim(Candidate c, CertifiedKnowledgePayload p)
    {
        var supporting = p.ReviewedSources.Where(s => s.Reviewed && s.Certified && (c.SourceIds.Contains(s.SourceId, StringComparer.OrdinalIgnoreCase)
            || s.SupportedClaimIds.Contains(c.SemanticKey, StringComparer.OrdinalIgnoreCase)
            || s.SupportedKnowledgeIds.Contains(c.KnowledgeId, StringComparer.OrdinalIgnoreCase)
            || s.SupportedDomains.Contains(c.Domain.ToString(), StringComparer.OrdinalIgnoreCase))).ToArray();
        var exact = supporting.Where(s => c.SourceIds.Contains(s.SourceId, StringComparer.OrdinalIgnoreCase) || s.SupportedClaimIds.Contains(c.SemanticKey, StringComparer.OrdinalIgnoreCase)).ToArray();
        var chosen = exact.Length > 0 ? exact : supporting;
        var cultural = c.Domain is NarrationKnowledgeDomainKey.CultureAndMythology or NarrationKnowledgeDomainKey.RegionalTraditions;
        var astrology = c.Domain == NarrationKnowledgeDomainKey.AstrologyClarification;
        var approx = Has(c.Text,"approximately","approximate","roughly","generally","typically","may ");
        var location = c.Domain is NarrationKnowledgeDomainKey.Observation or NarrationKnowledgeDomainKey.Visibility or NarrationKnowledgeDomainKey.LocationDependence || Has(c.Text,"location","latitude","hemisphere","local horizon");
        var time = c.Domain == NarrationKnowledgeDomainKey.Timing || Has(c.Text,"date","time","season","month","evening","winter");
        var weather = Has(c.Text,"weather","cloud"); var moon = Has(c.Text,"moonlight","moon interference");
        var confidence = chosen.Length == 0 ? .5m : chosen.Min(x=>x.Confidence);
        if (approx) confidence -= .05m; if (cultural || astrology) confidence -= .05m; if (location || time) confidence -= .03m;
        confidence = Math.Clamp(confidence, 0m, 1m);
        var id = Phase7Determinism.SemanticClaimId(c.KnowledgeId, c.Path, p.Language, p.EvergreenPayloadId ?? p.PayloadId);
        var draft = new CertifiedNarrationClaim(id,c.Domain.ToString(),c.Text,chosen.Select(x=>x.SourceId).Order(StringComparer.Ordinal).ToArray(),[c.KnowledgeId,c.SemanticKey],confidence,approx,location,time,cultural,cultural,astrology,cultural||astrology||approx||location||time,chosen.Length==0||exact.Length==0||cultural,p.Language,"")
        { SemanticIdentity=c.SemanticKey, ProvenancePrecision=exact.Length>0?"Exact":"Coarse", WeatherDependent=weather, MoonDependent=moon, Uncertain=confidence<.8m };
        return draft with { Checksum=Phase7Determinism.Hash(draft with { Checksum="" }) };
    }
    private static NarrationKnowledgeDomainKey Refine(NarrationKnowledgeDomainKey d,string key) => key.ToLowerInvariant() switch { var x when x.Contains("starformation") => NarrationKnowledgeDomainKey.StarFormation, var x when x.Contains("distance") => NarrationKnowledgeDomainKey.Distance, var x when x.Contains("majorstar") => NarrationKnowledgeDomainKey.KeyObjects, var x when x.Contains("deepsky") => NarrationKnowledgeDomainKey.DeepSkyObjects, var x when x.Contains("importance") => NarrationKnowledgeDomainKey.ScientificSignificance, var x when x.Contains("belt")||x.Contains("recognition") => NarrationKnowledgeDomainKey.Recognition, var x when x.Contains("safety")||x.Contains("caution")||x.Contains("warning") => NarrationKnowledgeDomainKey.Safety, var x when x.Contains("equipment")||x.Contains("binocular")||x.Contains("telescope") => NarrationKnowledgeDomainKey.Equipment, _=>d };
    private static NarrationKnowledgeDomainKey ParseDomain(string value) => NarrationKnowledgeDomains.TryParse(value,out var key)?key:throw new InvalidOperationException($"P7DOMAIN_UNKNOWN:{value}");
    private static string CanonicalPath(string path,string text) => path.EndsWith(".0",StringComparison.Ordinal)||char.IsDigit(path[^1]) ? path[..path.LastIndexOf('.')]+"."+Phase7Determinism.Hash(text)[..10] : path;
    private static string Humanize(string v) => string.Concat(v.Select((c,i)=>char.IsUpper(c)&&i>0?" "+char.ToLowerInvariant(c):c.ToString()));
    private static bool Has(string v,params string[] terms)=>terms.Any(t=>v.Contains(t,StringComparison.OrdinalIgnoreCase));
    private static bool Certified(string v)=>v.Equals("Certified",StringComparison.OrdinalIgnoreCase)||v.Equals("Verified",StringComparison.OrdinalIgnoreCase)||v.Equals("Reviewed",StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<string,string> Localized(string? json,string language,string key) { var m=new SortedDictionary<string,string>(); if(string.IsNullOrWhiteSpace(json))return m; using var d=JsonDocument.Parse(json); if(!d.RootElement.TryGetProperty("localizedContent",out var l)||!l.TryGetProperty(language.Split('-','_')[0],out var c)||!c.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.Array)return m; foreach(var x in v.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String)){var s=x.GetString()!;m[s]=s;} return m; }
    private static IReadOnlyDictionary<string,string> LocalizedScalar(string? json,string language,string key) { var m=new SortedDictionary<string,string>(); if(string.IsNullOrWhiteSpace(json))return m; using var d=JsonDocument.Parse(json); if(d.RootElement.TryGetProperty("localizedContent",out var l)&&l.TryGetProperty(language.Split('-','_')[0],out var c)&&c.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String)m["subject"]=v.GetString()!; return m; }
    private static ResolvedNarrationKnowledge Empty(CertifiedKnowledgePayload p,FamilyNarrationProfile profile,string issue) { var ds=Enum.GetValues<NarrationKnowledgeDomainKey>().Select(k=>new NarrationKnowledgeDomain(k.ToString(),profile.MandatoryKnowledgeDomains.Contains(k.ToString())?KnowledgeDomainStatus.Missing:KnowledgeDomainStatus.NotApplicable,[],[])).ToArray(); var r=new ResolvedNarrationKnowledge(p.PayloadId,p.PayloadChecksum,p.SourceRegistryId,"",p.Language,ds,new Dictionary<string,string>(),[],new Dictionary<string,string>(),[],[],[issue],""); return r with{DeterministicChecksum=Phase7Determinism.Hash(r with{DeterministicChecksum=""})}; }
    private sealed record Candidate(string Origin,string KnowledgeId,string Path,NarrationKnowledgeDomainKey Domain,string Text,string[] SourceIds) { public string SemanticKey=>$"{KnowledgeId}.{Path}".ToLowerInvariant(); }
}
