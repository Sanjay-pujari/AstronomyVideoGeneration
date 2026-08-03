using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Deterministically projects certified JSON; it has no network or model dependency.</summary>
public sealed class Phase7KnowledgeResolver : IPhase7KnowledgeResolver
{
    private static readonly string[] DomainNames = ["Identity","Recognition","ScientificStructure","KeyObjects","DeepSkyObjects",
        "Observation","Astrophotography","History","CultureAndMythology","IndianOrRegionalTraditions","AstrologyClarification",
        "InterestingFacts","EditorialSafety","LocalizedContent","Timing","Geometry","Visibility","LocationDependence",
        "WeatherDependence","MoonInterference","Safety","Equipment","Uncertainty","ScientificSignificance","Objects"];

    public ResolvedNarrationKnowledge Resolve(CertifiedKnowledgePayload payload, FamilyNarrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(payload); ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(payload.VerificationStatus, "Certified", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(payload.VerificationStatus, "Verified", StringComparison.OrdinalIgnoreCase))
            return Empty(payload, profile, "Knowledge payload is not certified or verified.");
        if (payload.ReviewedSourceIds.Count == 0 || payload.ReviewedSourceIds.Any(string.IsNullOrWhiteSpace))
            return Empty(payload, profile, "Certified source registry contains no reviewed source IDs.");
        JsonDocument document;
        try { document = JsonDocument.Parse(SelectJson(payload)); }
        catch (JsonException ex) { return Empty(payload, profile, $"Knowledge JSON is invalid: {ex.Message}"); }
        using (document)
        {
            var domains = new List<NarrationKnowledgeDomain>();
            foreach (var domain in DomainNames)
            {
                var elements = FindProperties(document.RootElement, domain).ToArray();
                var texts = elements.SelectMany(ExtractClaimTexts).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
                var claims = texts.Select((text, index) => Claim(payload, domain, text, index)).ToArray();
                var mandatory = profile.MandatoryKnowledgeDomains.Any(x => SameDomain(x, domain));
                domains.Add(new(domain, claims.Length > 0 ? KnowledgeDomainStatus.Available : mandatory ? KnowledgeDomainStatus.Missing : KnowledgeDomainStatus.NotApplicable,
                    claims, mandatory && claims.Length == 0 ? [$"Mandatory domain '{domain}' has no certified claim."] : []));
            }
            var blocking = domains.Where(x => x.Status == KnowledgeDomainStatus.Missing).Select(x => $"Mandatory knowledge domain missing: {x.Domain}.").ToArray();
            var vocabulary = LocalizedMap(document.RootElement, "localizedVocabulary");
            var protectedTerms = FindProperties(document.RootElement, "protectedTerms").SelectMany(ExtractClaimTexts).Distinct(StringComparer.Ordinal).ToArray();
            var pronunciation = LocalizedMap(document.RootElement, "pronunciationHints");
            var checksum = Phase7Determinism.Hash(new { payload.PayloadId, payload.RawDataJson, payload.MetadataJson, payload.EvergreenJson });
            var result = new ResolvedNarrationKnowledge(payload.PayloadId, checksum, payload.SourceRegistryId,
                Phase7Determinism.Hash(payload.ReviewedSourceIds.Order(StringComparer.Ordinal)), payload.Language, domains,
                vocabulary, protectedTerms, pronunciation, payload.ReviewedSourceIds, [], blocking, "");
            return result with { DeterministicChecksum = Phase7Determinism.Hash(result with { DeterministicChecksum = "" }) };
        }
    }

    private static string SelectJson(CertifiedKnowledgePayload payload) => !string.IsNullOrWhiteSpace(payload.EvergreenJson)
        ? payload.EvergreenJson! : payload.RawDataJson;
    private static CertifiedNarrationClaim Claim(CertifiedKnowledgePayload p, string domain, string text, int ordinal)
    {
        var cultural = domain.Contains("Culture", StringComparison.OrdinalIgnoreCase) || domain.Contains("Tradition", StringComparison.OrdinalIgnoreCase);
        var astrology = domain.Contains("Astrology", StringComparison.OrdinalIgnoreCase);
        var approximate = ContainsAny(text, "approximately", "approximate", "about ", "roughly", "typically");
        var location = domain is "Observation" or "Visibility" or "LocationDependence" || ContainsAny(text, "location", "latitude", "hemisphere");
        var time = domain is "Timing" || ContainsAny(text, "date", "time", "season", "month", "evening", "peak");
        var id = Phase7Determinism.ClaimId(p.PayloadId, domain, ordinal);
        var draft = new CertifiedNarrationClaim(id, domain, text.Trim(), p.ReviewedSourceIds, [], 1.0m, approximate,
            location, time, cultural, cultural, astrology, cultural || astrology || approximate || location || time,
            cultural, p.Language, "");
        return draft with { Checksum = Phase7Determinism.Hash(draft with { Checksum = "" }) };
    }
    private static IEnumerable<JsonElement> FindProperties(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (SameDomain(property.Name, name)) yield return property.Value;
                foreach (var nested in FindProperties(property.Value, name)) yield return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) foreach (var nested in FindProperties(item, name)) yield return nested;
    }
    private static IEnumerable<string> ExtractClaimTexts(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) yield return value.GetString()!;
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) foreach (var text in ExtractClaimTexts(item)) yield return text;
        else if (value.ValueKind == JsonValueKind.Object)
        {
            var emitted = false;
            foreach (var key in new[] { "text", "claim", "value", "description", "summary", "name" })
                if (value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String) { emitted = true; yield return property.GetString()!; }
            if (!emitted) foreach (var property in value.EnumerateObject()) foreach (var text in ExtractClaimTexts(property.Value)) yield return text;
        }
    }
    private static IReadOnlyDictionary<string,string> LocalizedMap(JsonElement root, string name)
    {
        var map = new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach (var value in FindProperties(root, name).Where(x => x.ValueKind == JsonValueKind.Object))
            foreach (var property in value.EnumerateObject()) if (property.Value.ValueKind == JsonValueKind.String) map[property.Name] = property.Value.GetString()!;
        return map;
    }
    private static bool SameDomain(string left, string right) => Normalize(left) == Normalize(right);
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static ResolvedNarrationKnowledge Empty(CertifiedKnowledgePayload p, FamilyNarrationProfile profile, string issue)
    {
        var domains = DomainNames.Select(x => new NarrationKnowledgeDomain(x,
            profile.MandatoryKnowledgeDomains.Any(m => SameDomain(m,x)) ? KnowledgeDomainStatus.Missing : KnowledgeDomainStatus.NotApplicable, [], [])).ToArray();
        var result = new ResolvedNarrationKnowledge(p.PayloadId, "", p.SourceRegistryId, "", p.Language, domains,
            new Dictionary<string,string>(), [], new Dictionary<string,string>(), p.ReviewedSourceIds, [], [issue], "");
        return result with { DeterministicChecksum = Phase7Determinism.Hash(result with { DeterministicChecksum = "" }) };
    }
}
