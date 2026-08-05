using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7KnowledgeReferenceNormalizer : IPhase7KnowledgeReferenceNormalizer
{
    private static readonly HashSet<string> SupportedNamespaces = new(StringComparer.Ordinal) { "production-event-intelligence" };

    public Phase7KnowledgeReferenceNormalizationResult Normalize(string referenceId)
    {
        var original = referenceId ?? "";
        var trimmed = original.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return Invalid(original, "P7REF_BLANK");
        var hash = trimmed.IndexOf('#');
        if (hash <= 0 || hash != trimmed.LastIndexOf('#')) return Invalid(original, "P7REF_MALFORMED");
        var ns = trimmed[..hash];
        var pointer = trimmed[(hash + 1)..];
        if (!SupportedNamespaces.Contains(ns)) return new(false, original, ns, "", [], "P7REF_UNSUPPORTED_NAMESPACE");
        if (!IsValidJsonPointer(pointer)) return new(false, original, ns, "", [], "P7REF_INVALID_JSON_POINTER");
        var canonical = DecodePointer(pointer);
        var candidates = new[] { trimmed, canonical, "#" + canonical, canonical.TrimStart('/') }
            .Distinct(StringComparer.Ordinal).ToArray();
        return new(true, original, ns, canonical, candidates, "P7REF_NORMALIZED");
    }

    private static Phase7KnowledgeReferenceNormalizationResult Invalid(string original, string code) => new(false, original, "", "", [], code);
    private static bool IsValidJsonPointer(string pointer)
    {
        if (!pointer.StartsWith("/", StringComparison.Ordinal)) return false;
        for (var i = 0; i < pointer.Length; i++)
            if (pointer[i] == '~' && (i + 1 >= pointer.Length || pointer[i + 1] is not ('0' or '1'))) return false;
        return true;
    }
    private static string DecodePointer(string pointer) => pointer.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
}

public sealed class Phase7ApprovedFieldPathCanonicalizer : IPhase7ApprovedFieldPathCanonicalizer
{
    public Phase7ApprovedFieldPathCanonicalizationResult Canonicalize(string approvedFieldPath, Phase7KnowledgeOrigin? origin = null)
    {
        var original = approvedFieldPath ?? "";
        var s = original.Trim();
        if (string.IsNullOrWhiteSpace(s)) return new(false, original, "", "", origin, "P7REF_APPROVED_PATH_BLANK");
        var ns = "production-event-intelligence";
        var hash = s.IndexOf('#');
        if (hash >= 0)
        {
            ns = s[..hash];
            s = s[(hash + 1)..];
            if (ns.Length == 0) ns = "production-event-intelligence";
        }
        if (!s.StartsWith("/", StringComparison.Ordinal))
            return new(false, original, "", ns, origin, "P7REF_APPROVED_PATH_UNMAPPED");
        for (var i = 0; i < s.Length; i++)
            if (s[i] == '~' && (i + 1 >= s.Length || s[i + 1] is not ('0' or '1')))
                return new(false, original, "", ns, origin, "P7REF_APPROVED_PATH_INVALID_JSON_POINTER");
        return new(true, original, s.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal), ns, origin, "P7REF_APPROVED_PATH_CANONICALIZED");
    }
}

public sealed class Phase7CommittedClaimEvidenceIndexBuilder : IPhase7CommittedClaimEvidenceIndexBuilder
{
    private readonly IPhase7ApprovedFieldPathCanonicalizer canonicalizer;
    public Phase7CommittedClaimEvidenceIndexBuilder(IPhase7ApprovedFieldPathCanonicalizer canonicalizer) => this.canonicalizer = canonicalizer;
    public IReadOnlyList<Phase7CommittedClaimEvidenceIndexEntry> Build(Phase7ScenePacketInputAuthority authority)
    {
        var k = authority.Knowledge.KnowledgeAuthority;
        var r = authority.Knowledge.ResolvedNarrationKnowledge;
        return k.Claims.OrderBy(c => c.ClaimId, StringComparer.Ordinal).Select(c =>
        {
            var ae = k.ClaimSupportEvidence.Where(e => e.ClaimId == c.ClaimId || e.SemanticIdentity == c.SemanticIdentity).ToArray();
            var re = (r?.ClaimSupportEvidence ?? Array.Empty<Phase7ClaimSupportEvidence>()).Where(e => e.ClaimId == c.ClaimId || e.SemanticIdentity == c.SemanticIdentity).ToArray();
            var rd = (r?.ClaimResolutionDiagnostics ?? Array.Empty<Phase7ClaimResolutionDiagnostic>()).Where(d => d.ClaimId == c.ClaimId || d.SemanticIdentity == c.SemanticIdentity || d.SelectedClaimIds.Contains(c.ClaimId, StringComparer.Ordinal)).ToArray();
            string[] Canon(IEnumerable<Phase7ClaimSupportEvidence> xs) => xs.Select(x => canonicalizer.Canonicalize(x.ApprovedFieldPath, x.Origin)).Where(x => x.IsValid).Select(x => x.CanonicalJsonPointer).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var diagPaths = rd.Select(d => canonicalizer.Canonicalize(d.ApprovedFieldPath, d.Origin)).Where(x => x.IsValid).Select(x => x.CanonicalJsonPointer).ToArray();
            var all = Canon(ae.Concat(re)).Concat(diagPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return new Phase7CommittedClaimEvidenceIndexEntry(c.ClaimId, c.SemanticIdentity, all,
                ae.Select(e => e.KnowledgeId).Concat(re.Select(e => e.KnowledgeId)).Concat(rd.Select(d => d.KnowledgeEntityId)).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                c.KnowledgeReferenceIds, c.SourceIds, c.Disposition, c.Language, c.RequiresHumanReview,
                ae.Concat(re).Select(e => e.SourceEligibility).Distinct().OrderBy(x=>x.ToString()).ToArray(),
                ae.Concat(re).Select(e => e.ProvenancePrecision).Concat(rd.Select(d=>d.ProvenancePrecision)).Distinct().OrderBy(x=>x.ToString()).ToArray(),
                ae.Concat(re).Select(e => e.Origin).Concat(rd.Select(d=>d.Origin)).Distinct().OrderBy(x=>x.ToString()).ToArray())
            {
                AuthorityCanonicalApprovedFieldPaths = Canon(ae),
                ResolutionCanonicalApprovedFieldPaths = Canon(re).Concat(diagPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                RawApprovedFieldPaths = ae.Select(e=>e.ApprovedFieldPath).Concat(re.Select(e=>e.ApprovedFieldPath)).Concat(rd.Select(d=>d.ApprovedFieldPath)).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                ResolutionDiagnosticIds = rd.Select(d=>d.CandidateId).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            };
        }).ToArray();
    }
}

public sealed class Phase7KnowledgeReferenceIdentityBridge : IPhase7KnowledgeReferenceIdentityBridge
{
    private readonly IPhase7KnowledgeReferenceNormalizer normalizer;
    private readonly IPhase7CommittedClaimEvidenceIndexBuilder indexBuilder;
    public Phase7KnowledgeReferenceIdentityBridge(IPhase7KnowledgeReferenceNormalizer normalizer) : this(normalizer, new Phase7CommittedClaimEvidenceIndexBuilder(new Phase7ApprovedFieldPathCanonicalizer())) { }
    public Phase7KnowledgeReferenceIdentityBridge(IPhase7KnowledgeReferenceNormalizer normalizer, IPhase7CommittedClaimEvidenceIndexBuilder indexBuilder) { this.normalizer = normalizer; this.indexBuilder = indexBuilder; }

    public Phase7KnowledgeReferenceIdentityBridgeResult Resolve(string phase6ReferenceId, PublishedPhase7KnowledgeAuthority authority)
    {
        var normalized = normalizer.Normalize(phase6ReferenceId);
        var input = new Phase7ScenePacketInputAuthority(authority, null!, null!, "", "", "", "", "", authority.KnowledgeAuthority.Language, "", "", [], [], [], [], new Dictionary<string,string>(), new Dictionary<string,string>());
        return Resolve(normalized, input);
    }

    public Phase7KnowledgeReferenceIdentityBridgeResult Resolve(Phase7KnowledgeReferenceNormalizationResult normalized, Phase7ScenePacketInputAuthority authority)
    {
        if (!normalized.IsValid) return new(false, normalized.OriginalReferenceId, normalized.CanonicalJsonPointer, [], [], [], normalized.ReasonCode, normalized.ReasonCode, [$"originalReferenceId={normalized.OriginalReferenceId}"]);
        var pointer = normalized.CanonicalJsonPointer;
        var candidates = normalized.CanonicalIdentityCandidates;
        var index = indexBuilder.Build(authority);
        Phase7CommittedClaimEvidenceIndexEntry[] matches = [];
        var method = "P7REF_REFERENCE_UNRESOLVED";
        bool Try(IEnumerable<Phase7CommittedClaimEvidenceIndexEntry> q, string m) { matches = q.OrderBy(x=>x.ClaimId,StringComparer.Ordinal).ToArray(); if (matches.Length == 0) return false; method = m; return true; }
        _ = Try(index.Where(x => x.ClaimId == normalized.OriginalReferenceId || x.ClaimId == pointer), "P7PACKET_REFERENCE_RESOLVED_EXACT_CLAIM_ID")
            || Try(index.Where(x => x.SemanticIdentity == normalized.OriginalReferenceId || x.SemanticIdentity == pointer || candidates.Contains(x.SemanticIdentity, StringComparer.Ordinal)), "P7PACKET_REFERENCE_RESOLVED_EXACT_SEMANTIC_IDENTITY")
            || Try(index.Where(x => x.KnowledgeReferenceIds.Intersect(candidates, StringComparer.Ordinal).Any()), "P7PACKET_REFERENCE_RESOLVED_EXACT_KNOWLEDGE_REFERENCE")
            || Try(index.Where(x => x.AuthorityCanonicalApprovedFieldPaths.Contains(pointer, StringComparer.Ordinal)), "P7PACKET_REFERENCE_RESOLVED_AUTHORITY_APPROVED_FIELD")
            || Try(index.Where(x => x.ResolutionCanonicalApprovedFieldPaths.Contains(pointer, StringComparer.Ordinal)), "P7PACKET_REFERENCE_RESOLVED_RESOLUTION_APPROVED_FIELD")
            || Try(index.Where(x => x.AuthorityCanonicalApprovedFieldPaths.Any(p => IsDescendant(pointer, p))), "P7PACKET_REFERENCE_RESOLVED_AUTHORITY_DESCENDANT")
            || Try(index.Where(x => x.ResolutionCanonicalApprovedFieldPaths.Any(p => IsDescendant(pointer, p))), "P7PACKET_REFERENCE_RESOLVED_RESOLUTION_DESCENDANT")
            || Try(index.Where(x => x.KnowledgeEntityIds.Intersect(candidates, StringComparer.Ordinal).Any()), "P7PACKET_REFERENCE_RESOLVED_KNOWLEDGE_ENTITY");
        var claimIds = matches.Select(x=>x.ClaimId).ToArray();
        var paths = matches.SelectMany(x=>x.CanonicalApprovedFieldPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var entities = matches.SelectMany(x=>x.KnowledgeEntityIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var evidence = new[] { $"originalReferenceId={normalized.OriginalReferenceId}", $"canonicalJsonPointer={pointer}", $"authorityNamespace={normalized.AuthorityNamespace}", $"resolutionMethod={method}" }
            .Concat(index.SelectMany(x=>x.RawApprovedFieldPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(x=>$"rawApprovedFieldPathConsidered={x}"))
            .Concat(index.SelectMany(x=>x.CanonicalApprovedFieldPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(x=>$"canonicalApprovedFieldPathConsidered={x}"))
            .Concat(paths.Select(x=>$"matchingPath={x}"))
            .Concat(matches.SelectMany(x=>x.ResolutionDiagnosticIds).Select(x=>$"matchingResolutionDiagnosticId={x}"))
            .Concat(entities.Select(x=>$"matchingKnowledgeEntityId={x}"))
            .Concat(matches.Select(x=>$"candidateClaimId={x.ClaimId};disposition={x.Disposition};origins={string.Join('|',x.Origins)}"))
            .ToArray();
        return new(claimIds.Length>0, normalized.OriginalReferenceId, pointer, paths, entities, claimIds, method, claimIds.Length>0 ? method : "P7REF_REFERENCE_UNRESOLVED", evidence) { CanonicalReferenceIds = candidates };
    }

    internal static bool IsRequiredEligible(CertifiedNarrationClaim c, Phase7ScenePacketInputAuthority input) =>
        c.Disposition == Phase7ClaimDisposition.Required && !c.RequiresHumanReview && string.Equals(c.Language, input.Language, StringComparison.OrdinalIgnoreCase) &&
        input.Knowledge.KnowledgeAuthority.ClaimSupportEvidence.Any(e => e.ClaimId == c.ClaimId && e.SemanticIdentity == c.SemanticIdentity && c.SourceIds.Contains(e.SourceId, StringComparer.Ordinal) && e.SourceEligibility == Phase7SourceEligibility.EligibleForRequiredClaim && !e.RequiresHumanReview && e.ProvenancePrecision is Phase7ProvenancePrecision.ExactClaim or Phase7ProvenancePrecision.ExactKnowledgeEntity or Phase7ProvenancePrecision.ExactApprovedField);
    private static bool IsDescendant(string parent, string path) => path.Length > parent.Length && path.StartsWith(parent, StringComparison.Ordinal) && path[parent.Length] == '/';
}

public sealed class Phase7KnowledgeReferenceResolver : IPhase7KnowledgeReferenceResolver
{
    private readonly IPhase7KnowledgeReferenceNormalizer normalizer;
    private readonly IPhase7KnowledgeReferenceIdentityBridge bridge;
    public Phase7KnowledgeReferenceResolver() : this(new Phase7KnowledgeReferenceNormalizer(), null) { }
    public Phase7KnowledgeReferenceResolver(IPhase7KnowledgeReferenceNormalizer normalizer, IPhase7KnowledgeReferenceIdentityBridge? bridge = null) { this.normalizer = normalizer; this.bridge = bridge ?? new Phase7KnowledgeReferenceIdentityBridge(normalizer); }

    public IReadOnlyList<Phase7KnowledgeReferenceResolution> Resolve(IReadOnlyList<string> ids, ResolvedNarrationKnowledge knowledge, bool optional = false)
    {
        var claims = knowledge.Domains.SelectMany(x => x.Claims).ToArray();
        return ids.Select(id =>
        {
            if (string.IsNullOrWhiteSpace(id)) return new Phase7KnowledgeReferenceResolution(id, Phase7KnowledgeReferenceStatus.Unsupported, [], "P7REF_BLANK");
            var matches = claims.Where(c => c.ClaimId.Equals(id, StringComparison.OrdinalIgnoreCase) || c.SemanticIdentity.Equals(id, StringComparison.OrdinalIgnoreCase) || c.KnowledgeReferenceIds.Contains(id, StringComparer.OrdinalIgnoreCase)).OrderBy(c => c.ClaimId, StringComparer.Ordinal).ToArray();
            return matches.Length switch { 0 when optional => new(id, Phase7KnowledgeReferenceStatus.Deferred, [], "P7REF_OPTIONAL_DEFERRED"), 0 => new(id, Phase7KnowledgeReferenceStatus.Missing, [], "P7REF_PRIMARY_MISSING"), _ => new(id, Phase7KnowledgeReferenceStatus.Resolved, matches, "P7REF_RESOLVED") };
        }).ToArray();
    }

    public Phase7KnowledgeReferenceResolution Resolve(Phase7KnowledgeReferenceRequest request, Phase7ScenePacketInputAuthority authority)
    {
        var normalized = normalizer.Normalize(request.ReferenceId);
        if (!normalized.IsValid) return new(request.ReferenceId, Phase7KnowledgeReferenceStatus.Unsupported, [], normalized.ReasonCode) { OriginalReferenceId = normalized.OriginalReferenceId };
        var bridged = bridge.Resolve(normalized, authority);
        var claims = authority.Knowledge.KnowledgeAuthority.Claims.Where(c => bridged.CandidateClaimIds.Contains(c.ClaimId, StringComparer.Ordinal)).OrderBy(c => c.ClaimId, StringComparer.Ordinal).ToArray();
        var status = claims.Length > 0 ? Phase7KnowledgeReferenceStatus.Resolved : (request.Optional ? Phase7KnowledgeReferenceStatus.Deferred : Phase7KnowledgeReferenceStatus.Missing);
        var reason = claims.Length > 0 ? bridged.ReasonCode : (request.Optional ? "P7REF_APPROVED_PATH_UNMAPPED" : "P7REF_REFERENCE_UNRESOLVED");
        var eligible = claims.Where(c => Phase7KnowledgeReferenceIdentityBridge.IsRequiredEligible(c, authority)).Select(c => c.ClaimId).Order(StringComparer.Ordinal).ToArray();
        return new(request.ReferenceId, status, claims, reason) { OriginalReferenceId = normalized.OriginalReferenceId, NormalizedReferenceId = normalized.AuthorityNamespace + "#" + normalized.CanonicalJsonPointer, CanonicalJsonPointer = normalized.CanonicalJsonPointer, ResolutionMethod = bridged.ResolutionMethod, MatchedApprovedFieldPaths = bridged.MatchedApprovedFieldPaths, MatchedKnowledgeEntityIds = bridged.MatchedKnowledgeEntityIds, CandidateClaimIds = bridged.CandidateClaimIds, EligibleRequiredClaimIds = eligible, Evidence = bridged.Evidence };
    }
}
