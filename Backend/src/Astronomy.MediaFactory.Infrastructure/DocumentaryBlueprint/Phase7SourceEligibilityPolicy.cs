using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>The single governed decision point for whether source evidence may support a claim.</summary>
public sealed class Phase7SourceEligibilityPolicy : IPhase7SourceEligibilityPolicy
{
    private const decimal RequiredConfidence = .70m;

    public Phase7SourceEligibilityResult Classify(Phase7SourceEligibilityRequest request)
    {
        var source = request.Source;
        if (source.Disposition.Equals("Rejected", StringComparison.OrdinalIgnoreCase) ||
            source.Disposition.Equals("RejectedReviewState", StringComparison.OrdinalIgnoreCase))
            return Result(Phase7SourceEligibility.Rejected, "P7KNOWLEDGE_SOURCE_REJECTED");
        if (!source.Language.Equals(request.Language, StringComparison.OrdinalIgnoreCase))
            return Result(Phase7SourceEligibility.AuditOnly, "P7KNOWLEDGE_SOURCE_LANGUAGE_MISMATCH");

        var precision = Precision(source, request);
        if (precision == Phase7ProvenancePrecision.None)
            return Result(Phase7SourceEligibility.AuditOnly, "P7KNOWLEDGE_SOURCE_EVIDENCE_MISMATCH");

        var approved = source.Reviewed || Approved(source.ReviewState);
        var authoritative = source.Certified || Verified(source.AuthorityState);
        if (request.Required)
        {
            if (!authoritative) return Result(Phase7SourceEligibility.AuditOnly, "P7KNOWLEDGE_REQUIRED_SOURCE_UNVERIFIED", precision);
            if (!approved) return Result(Phase7SourceEligibility.Rejected, "P7KNOWLEDGE_SOURCE_REVIEW_STATE_NOT_APPROVED", precision);
            if (source.Confidence < RequiredConfidence) return Result(Phase7SourceEligibility.AuditOnly, "P7KNOWLEDGE_REQUIRED_SOURCE_LOW_CONFIDENCE", precision);
            return new(Phase7SourceEligibility.EligibleForRequiredClaim, "P7KNOWLEDGE_SOURCE_REQUIRED_ELIGIBLE", true, precision);
        }

        if (authoritative && approved)
            return new(Phase7SourceEligibility.EligibleForOptionalClaim, "P7KNOWLEDGE_SOURCE_OPTIONAL_ELIGIBLE", true, precision);
        if (request.OptionalReviewedEvidenceAllowed && approved && request.RequiresHumanReview)
            return new(Phase7SourceEligibility.EligibleForOptionalClaim, "P7KNOWLEDGE_SOURCE_OPTIONAL_REVIEW_REQUIRED", false, precision);
        return Result(Phase7SourceEligibility.AuditOnly, "P7KNOWLEDGE_SOURCE_AUDIT_ONLY", precision);
    }

    private static Phase7SourceEligibilityResult Result(Phase7SourceEligibility value, string reason,
        Phase7ProvenancePrecision precision = Phase7ProvenancePrecision.None) => new(value, reason, false, precision);
    private static bool Approved(string value) => new[] { "Approved", "Reviewed", "Verified", "Certified" }.Contains(value, StringComparer.OrdinalIgnoreCase);
    private static bool Verified(string value) => value.Equals("Verified", StringComparison.OrdinalIgnoreCase) || value.Equals("Certified", StringComparison.OrdinalIgnoreCase);
    private static Phase7ProvenancePrecision Precision(CertifiedNarrationSource source, Phase7SourceEligibilityRequest request)
    {
        if (source.SupportedClaimIds.Contains(request.SemanticIdentity, StringComparer.OrdinalIgnoreCase)) return Phase7ProvenancePrecision.ExactClaim;
        if (source.SupportedKnowledgeIds.Contains(request.KnowledgeId, StringComparer.OrdinalIgnoreCase)) return Phase7ProvenancePrecision.ExactKnowledgeEntity;
        var field = Phase7CanonicalFieldPathPolicy.Canonicalize(request.ApprovedFieldPath);
        return source.SupportedApprovedFieldPaths.Any(path => Phase7CanonicalFieldPathPolicy.TryCanonicalize(path, out var canonical) && canonical == field)
            ? Phase7ProvenancePrecision.ExactApprovedField : Phase7ProvenancePrecision.None;
    }
}
