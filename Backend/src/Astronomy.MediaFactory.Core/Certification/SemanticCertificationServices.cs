using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Core.Certification;

public sealed record ForbiddenConceptValidationResult(string ConceptId, string MatchedTerm, string SourceArtifact, string SourceField, string? SceneId, string? BeatId, bool IsBlocking, string ContextSnippet);
public interface IForbiddenConceptValidator { Task<IReadOnlyList<ForbiddenConceptValidationResult>> ValidateAsync(FamilyCertificationContext context, IFamilyCertificationProfile profile, CancellationToken cancellationToken); }
public interface IStoryBeatCoverageValidator { IReadOnlyList<CertificationIssue> Validate(IFamilyCertificationProfile profile, FamilyCertificationContext context, SemanticCertificationEvidence evidence); }

public sealed class SemanticCertificationEvidenceReader(ICertificationJsonReader? reader = null, ISemanticFactCatalog? catalog = null) : ISemanticCertificationEvidenceReader
{
    private readonly ICertificationJsonReader reader = reader ?? new CertificationJsonReader();
    private readonly ISemanticFactCatalog catalog = catalog ?? new CertificationSemanticFactCatalog();
    private static readonly string[] EvidencePaths = ["narration-v5/event-identity-diagnostics.json", "narration-v5/family-profile-v1-compatibility-diagnostics.json", "narration-v5/semantic-registry-validation-report.json", "narration-v5/semantic-capability-diagnostics.json", "narration-v5/required-semantic-fact-diagnostics.json", "narration-v5/meteor-shower-shadow-validation.json", "narration-v5/narration-context.json", "narration-v5/narration-plan.json", "narration-v5/narration-briefs.json", "narration-v5/scene-fact-cards/long/scene-fact-cards.json", "narration-v5/scene-fact-cards/short/scene-fact-cards.json", "narration-v5/documentary-script/long/documentary-script.json", "narration-v5/documentary-script/short/documentary-script.json", "narration-v5/long/narration.json", "narration-v5/short/narration.json", "narration-v5/narration-diagnostics.json", "narration-v5/narration-validation-diagnostics.json", "narration-v5/runtime-composition-diagnostics.json", "validation/phase-07-validation.json"];
    public async Task<SemanticCertificationEvidence> ReadAsync(FamilyCertificationContext context, CancellationToken cancellationToken)
    {
        var docs = new List<(string Path, JsonDocument Doc)>();
        foreach (var p in EvidencePaths)
        {
            var doc = await reader.ReadOptionalDocumentAsync(CertificationPathHelpers.ResolveArtifactPath(context, p), cancellationToken);
            if (doc is not null) docs.Add((p, doc));
        }
        var all = docs.Select(d => d.Doc.RootElement).ToArray();
        var family = FirstString(all, "familyId", "FamilyId", "canonicalFamilyId", "CanonicalFamilyId", "eventType", "EventType") ?? context.EventType;
        var diagnostics = docs.Select(d => d.Path).ToList();
        var canonical = ResolveCanonicalSemanticValue(all, family, context, diagnostics);
        var factIds = catalog.Facts.Select(f => f.FactId).ToArray();
        var facts = factIds.Select(f => BuildFact(f, docs)).Where(f => f.Resolved || f.Projected || f.Retained || f.BeatAssigned || f.NarrationEvidenceFound).ToArray();
        diagnostics.AddRange(docs.SelectMany(d => StructuredRoleValues(d.Doc.RootElement).Select(v => $"structured-story-role:{v}")));
        diagnostics.AddRange(docs.SelectMany(d => FlattenValues(d.Doc.RootElement)));
        return new SemanticCertificationEvidence { CanonicalIdentityPresent = HasFact(catalog.ResolveFactId("EventIdentity").FactId, facts) || ContainsAny(all, "canonicalIdentityPresent", context.EventTitle), CanonicalFamilyValuePresent = canonical is not null, FamilyId = family, CanonicalSemanticValueId = canonical, Facts = facts, Diagnostics = diagnostics.ToArray() };
    }

    private string? ResolveCanonicalSemanticValue(IEnumerable<JsonElement> roots, string? family, FamilyCertificationContext context, List<string> diagnostics)
    {
        var direct = FirstString(roots, "canonicalSemanticValueId", "CanonicalSemanticValueId");
        if (!string.IsNullOrWhiteSpace(direct)) return direct;
        var fromObject = FirstString(roots, "canonicalSemanticValue", "canonicalEventType", "canonicalProfile", "canonicalFamily");
        if (!string.IsNullOrWhiteSpace(fromObject)) return catalog.ResolveCanonicalValueOrNull(fromObject) ?? fromObject;
        var diag = FirstString(roots, "resolvedCanonicalSemanticValueId", "resolvedFamilyId", "resolvedProfileId");
        if (!string.IsNullOrWhiteSpace(diag)) return catalog.ResolveCanonicalValueOrNull(diag) ?? diag;
        diagnostics.Add("fallback:canonical-semantic-value:text-matching");
        if (!string.IsNullOrWhiteSpace(family)) return catalog.ResolveCanonicalValueOrNull(family);
        return catalog.ResolveCanonicalValueOrNull(context.EventType);
    }
    private static SemanticFactCertificationResult BuildFact(string fact, IReadOnlyList<(string Path, JsonDocument Doc)> docs)
    {
        var factDocs = docs.Where(d => ContainsAny([d.Doc.RootElement], fact)).ToArray();
        var structured = factDocs.Any(d => d.Path.Contains("required-semantic-fact", StringComparison.OrdinalIgnoreCase) || d.Path.Contains("shadow-validation", StringComparison.OrdinalIgnoreCase) || d.Path.Contains("semantic-capability", StringComparison.OrdinalIgnoreCase));
        bool AnyPath(string s) => factDocs.Any(d => d.Path.Contains(s, StringComparison.OrdinalIgnoreCase));
        var projected = AnyPath("required-semantic-fact") || AnyPath("shadow-validation") || factDocs.Any(d => ContainsAny([d.Doc.RootElement], "projected", "Projection", "DerivationRuleId"));
        var retained = AnyPath("scene-fact-cards") || AnyPath("narration-context") || factDocs.Any(d => ContainsAny([d.Doc.RootElement], "retained", "Retention"));
        var beatIds = factDocs.SelectMany(d => StringsNamed(d.Doc.RootElement, "beatId", "documentaryBeatId", "BeatId")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var sceneIds = factDocs.SelectMany(d => StringsNamed(d.Doc.RootElement, "sceneId", "SceneId")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var narration = factDocs.Any(d => d.Path.Contains("narration", StringComparison.OrdinalIgnoreCase) || d.Path.Contains("documentary-script", StringComparison.OrdinalIgnoreCase));
        return new() { FactId = fact, Required = true, Resolved = structured || factDocs.Any(), Projected = projected, Retained = retained, BeatAssigned = beatIds.Length > 0 || factDocs.Any(d => ContainsAny([d.Doc.RootElement], "beatAssigned", "allocatedFacts")), NarrationEvidenceFound = narration, SourceAdapterId = FirstString(factDocs.Select(d=>d.Doc.RootElement), "adapterId", "sourceAdapterId"), SourcePath = factDocs.FirstOrDefault().Path, ResolutionMode = FirstString(factDocs.Select(d=>d.Doc.RootElement), "resolutionMode", "mode"), Confidence = FirstNumber(factDocs.Select(d=>d.Doc.RootElement), "confidence", "minimumConfidence"), BeatIds = beatIds, SceneIds = sceneIds, Diagnostics = structured ? ["structured evidence preferred"] : factDocs.Length > 0 ? ["fallback structured/text evidence"] : [] };
    }
    private static bool HasFact(string id, IEnumerable<SemanticFactCertificationResult> facts) => facts.Any(f => f.FactId.Equals(id, StringComparison.OrdinalIgnoreCase) && f.Resolved);
    internal static bool ContainsAny(IEnumerable<JsonElement> roots, params string[] terms) => roots.Any(r => FlattenValues(r).Any(v => terms.Any(t => v.Contains(t, StringComparison.OrdinalIgnoreCase))));
    internal static IEnumerable<string> FlattenValues(JsonElement e) { if (e.ValueKind == JsonValueKind.String) yield return e.GetString() ?? ""; else if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) foreach (var s in FlattenValues(p.Value)) yield return s; else if (e.ValueKind == JsonValueKind.Array) foreach (var a in e.EnumerateArray()) foreach (var s in FlattenValues(a)) yield return s; }
    internal static IEnumerable<string> StructuredRoleValues(JsonElement e) => StringsNamed(e, "storyRole", "roleId", "beatRole", "documentaryRole", "scenePurpose", "requiredRoleIds");
    internal static IEnumerable<string> StringsNamed(JsonElement e, params string[] names) { if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (names.Contains(p.Name, StringComparer.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String) yield return p.Value.GetString()!; foreach (var s in StringsNamed(p.Value, names)) yield return s; } else if (e.ValueKind == JsonValueKind.Array) foreach (var a in e.EnumerateArray()) foreach (var s in StringsNamed(a, names)) yield return s; }
    private static string? FirstString(IEnumerable<JsonElement> roots, params string[] names) => roots.SelectMany(r => StringsNamed(r, names)).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
    private static double? FirstNumber(IEnumerable<JsonElement> roots, params string[] names) { foreach (var r in roots) foreach (var n in NumbersNamed(r, names)) return n; return null; }
    private static IEnumerable<double> NumbersNamed(JsonElement e, params string[] names) { if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (names.Contains(p.Name, StringComparer.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var d)) yield return d; foreach (var n in NumbersNamed(p.Value, names)) yield return n; } else if (e.ValueKind == JsonValueKind.Array) foreach (var a in e.EnumerateArray()) foreach (var n in NumbersNamed(a, names)) yield return n; }
}

public sealed class ForbiddenConceptValidator(ICertificationJsonReader? reader = null) : IForbiddenConceptValidator
{
    private readonly ICertificationJsonReader reader = reader ?? new CertificationJsonReader();
    private static readonly string[] Paths = ["narration-v5/narration-context.json", "narration-v5/narration-briefs.json", "narration-v5/scene-fact-cards/long/scene-fact-cards.json", "narration-v5/scene-fact-cards/short/scene-fact-cards.json", "narration-v5/documentary-script/long/documentary-script.json", "narration-v5/documentary-script/short/documentary-script.json", "narration-v5/long/narration.json", "narration-v5/short/narration.json"];
    private static readonly string[] Fields = ["text", "narration", "script", "spokenText", "summary", "description", "title", "body", "fact", "value", "speakableValue", "localizedText", "editorialText"];
    public async Task<IReadOnlyList<ForbiddenConceptValidationResult>> ValidateAsync(FamilyCertificationContext context, IFamilyCertificationProfile profile, CancellationToken cancellationToken)
    { var hits = new List<ForbiddenConceptValidationResult>(); foreach (var p in Paths) { var doc = await reader.ReadOptionalDocumentAsync(CertificationPathHelpers.ResolveArtifactPath(context, p), cancellationToken); if (doc is null) continue; foreach (var field in Approved(doc.RootElement)) foreach (var c in profile.GetForbiddenConcepts(context)) foreach (var term in c.Terms) if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(field.Value, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0) hits.Add(new(c.ConceptId, term, p, field.Name, field.SceneId, field.BeatId, c.Blocking, Snip(field.Value, term))); } return hits; }
    private static IEnumerable<(string Name,string Value,string? SceneId,string? BeatId)> Approved(JsonElement e, string? scene=null, string? beat=null) { if (e.ValueKind == JsonValueKind.Object) { var s = scene; var b = beat; if (e.TryGetProperty("sceneId", out var sv) && sv.ValueKind == JsonValueKind.String) s = sv.GetString(); if (e.TryGetProperty("beatId", out var bv) && bv.ValueKind == JsonValueKind.String) b = bv.GetString(); foreach (var p in e.EnumerateObject()) { if (Fields.Contains(p.Name, StringComparer.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String) yield return (p.Name, p.Value.GetString() ?? "", s, b); foreach (var x in Approved(p.Value, s, b)) yield return x; } } else if (e.ValueKind == JsonValueKind.Array) foreach (var a in e.EnumerateArray()) foreach (var x in Approved(a, scene, beat)) yield return x; }
    private static string Snip(string text, string term) { var i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); if (i < 0) i = 0; var start = Math.Max(0, i - 32); return text.Substring(start, Math.Min(text.Length - start, term.Length + 64)); }
}

public sealed class StoryBeatCoverageValidator : IStoryBeatCoverageValidator
{
    public IReadOnlyList<CertificationIssue> Validate(IFamilyCertificationProfile profile, FamilyCertificationContext context, SemanticCertificationEvidence evidence)
    {
        var issues = new List<CertificationIssue>();
        var structuredRoles = evidence.Diagnostics.Where(d => d.StartsWith("structured-story-role:", StringComparison.OrdinalIgnoreCase)).Select(d => d[22..]).ToArray();
        foreach (var r in profile.GetStoryRequirements(context).Where(r=>r.Required))
        {
            var found = structuredRoles.Length > 0
                ? structuredRoles.Any(role => role.Equals(r.StoryRole, StringComparison.OrdinalIgnoreCase))
                : evidence.Diagnostics.Any(d => d.Contains(r.StoryRole, StringComparison.OrdinalIgnoreCase));
            if (!found) issues.Add(Issue(CertificationIssueCategory.StoryStructureFailure, "P7.StoryRoleMissing", $"Required story role '{r.StoryRole}' was not found.", null));
        }
        foreach (var req in profile.GetBeatCoverageRequirements(context).Where(r=>r.Required)) { var fact = evidence.Facts.FirstOrDefault(f=>f.FactId.Equals(req.FactId,StringComparison.OrdinalIgnoreCase)); if (fact is null || !fact.BeatAssigned) issues.Add(Issue(CertificationIssueCategory.BeatAssignmentFailure, "P7.RequiredFactNotBeatAssigned", $"Required fact '{req.FactId}' was not assigned to an allowed beat.", req.FactId)); else if (fact.BeatIds.Count > 0 && !fact.BeatIds.Any(b=>req.AllowedBeatRoles.Any(a=>b.Contains(a,StringComparison.OrdinalIgnoreCase)))) issues.Add(Issue(CertificationIssueCategory.BeatAssignmentFailure, "P7.RequiredFactNotBeatAssigned", $"Required fact '{req.FactId}' was assigned only to disallowed beats.", req.FactId)); } return issues;
    }
    private static CertificationIssue Issue(CertificationIssueCategory category, string code, string message, string? fact) => new() { Category = category, Code = code, Message = message, SemanticFactId = fact, IsBlocking = true, Source = "Phase7SemanticCertification" };
}

file static class SemanticFactCatalogExtensions { public static string? ResolveCanonicalValueOrNull(this ISemanticFactCatalog catalog, string value) { try { return catalog.ResolveCanonicalValue(value); } catch { return null; } } }
