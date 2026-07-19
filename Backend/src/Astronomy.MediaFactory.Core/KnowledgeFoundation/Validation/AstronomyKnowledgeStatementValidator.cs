using Astronomy.MediaFactory.Core.AstronomyDomain.Validation;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

public sealed class AstronomyKnowledgeStatementValidator : IAstronomyKnowledgeStatementValidator
{
    public DomainValidationResult Validate(IAstronomyKnowledgeStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var issues = new List<DomainValidationIssue>();

        if (string.IsNullOrWhiteSpace(statement.Id.Value)) Add(issues, AstronomyKnowledgeValidationCodes.IdMissing, "Knowledge statement ID is required.", "id");
        if (statement.Version.Revision < 1) Add(issues, AstronomyKnowledgeValidationCodes.VersionInvalid, "Knowledge statement version must be positive.", "version.revision");
        if (!Enum.IsDefined(statement.Kind)) Add(issues, AstronomyKnowledgeValidationCodes.KindUndefined, "Knowledge statement kind is not defined.", "kind");
        if (!Enum.IsDefined(statement.Status)) Add(issues, AstronomyKnowledgeValidationCodes.StatusUndefined, "Knowledge foundation status is not defined.", "status");

        ValidateSubject(statement.PrimarySubject, issues);
        ValidateFamily(statement.FamilyContext, issues);
        if (statement.Payload is null) Add(issues, AstronomyKnowledgeValidationCodes.PayloadMissing, "Knowledge statement payload is required.", "payload");
        ValidateLocalizations(statement.LocalizationReferences, issues);
        ValidateTags(statement.Tags, issues);
        ValidateValidity(statement.Validity, issues);
        ValidateAudit(statement.Audit, issues);

        return DomainValidationResult.From(issues.ToArray());
    }

    private static void ValidateSubject(AstronomyEntityReference subject, List<DomainValidationIssue> issues)
    {
        if (subject is null) { Add(issues, AstronomyKnowledgeValidationCodes.SubjectMissing, "Primary subject is required.", "primarySubject"); return; }
        if (string.IsNullOrWhiteSpace(subject.EntityId)) Add(issues, AstronomyKnowledgeValidationCodes.SubjectInvalid, "Primary subject entity ID is required.", "primarySubject.entityId");
        if (subject.EntityKind.HasValue && !Enum.IsDefined(subject.EntityKind.Value)) Add(issues, AstronomyKnowledgeValidationCodes.SubjectInvalid, "Primary subject entity kind is not defined.", "primarySubject.entityKind");
    }

    private static void ValidateFamily(AstronomyFamilyReference? family, List<DomainValidationIssue> issues)
    {
        if (family is null) return;
        if (string.IsNullOrWhiteSpace(family.FamilyId)) Add(issues, AstronomyKnowledgeValidationCodes.FamilyContextInvalid, "Family context ID is required when family context is supplied.", "familyContext.familyId");
        if (family.FamilyKind.HasValue && !Enum.IsDefined(family.FamilyKind.Value)) Add(issues, AstronomyKnowledgeValidationCodes.FamilyContextInvalid, "Family context kind is not defined.", "familyContext.familyKind");
    }

    private static void ValidateLocalizations(IReadOnlyList<KnowledgeLocalizationReference> references, List<DomainValidationIssue> issues)
    {
        if (references is null) { Add(issues, AstronomyKnowledgeValidationCodes.LocalizationCollectionMissing, "Localization references collection is required.", "localizationReferences"); return; }
        var identities = new List<string>();
        for (var index = 0; index < references.Count; index++)
        {
            var reference = references[index];
            var path = $"localizationReferences[{index}]";
            if (reference is null) { Add(issues, AstronomyKnowledgeValidationCodes.LocalizationMissing, "Localization reference cannot be null.", path); continue; }
            if (reference.LanguageTag is null || string.IsNullOrWhiteSpace(reference.LanguageTag.Value)) Add(issues, AstronomyKnowledgeValidationCodes.LocalizationLanguageInvalid, "Localization language tag is required.", path + ".languageTag");
            if (string.IsNullOrWhiteSpace(reference.ResourceKey)) Add(issues, AstronomyKnowledgeValidationCodes.LocalizationResourceKeyMissing, "Localization resource key is required.", path + ".resourceKey");
            if (reference.LanguageTag is not null && !string.IsNullOrWhiteSpace(reference.LanguageTag.Value) && !string.IsNullOrWhiteSpace(reference.ResourceKey)) identities.Add(reference.LanguageTag.Value + "\u001f" + reference.ResourceKey);
        }
        if (identities.Distinct(StringComparer.Ordinal).Count() != identities.Count) Add(issues, AstronomyKnowledgeValidationCodes.DuplicateLocalization, "Localization references must be unique by language tag and resource key.", "localizationReferences");
    }

    private static void ValidateTags(IReadOnlyList<KnowledgeTag> tags, List<DomainValidationIssue> issues)
    {
        if (tags is null) { Add(issues, AstronomyKnowledgeValidationCodes.TagCollectionMissing, "Knowledge tags collection is required.", "tags"); return; }
        var values = new List<string>();
        for (var index = 0; index < tags.Count; index++)
        {
            var tag = tags[index];
            var path = $"tags[{index}]";
            if (tag is null) { Add(issues, AstronomyKnowledgeValidationCodes.TagMissing, "Knowledge tag cannot be null.", path); continue; }
            if (string.IsNullOrWhiteSpace(tag.Value)) Add(issues, AstronomyKnowledgeValidationCodes.TagInvalid, "Knowledge tag value is required.", path + ".value");
            else values.Add(tag.Value);
        }
        if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count) Add(issues, AstronomyKnowledgeValidationCodes.DuplicateTag, "Knowledge tags must be unique by normalized value.", "tags");
    }

    private static void ValidateValidity(KnowledgeValidityRange validity, List<DomainValidationIssue> issues)
    {
        if (validity is null) { Add(issues, AstronomyKnowledgeValidationCodes.ValidityMissing, "Knowledge validity range is required.", "validity"); return; }
        if (validity.EffectiveFromUtc.HasValue && validity.EffectiveFromUtc.Value.Offset != TimeSpan.Zero) Add(issues, AstronomyKnowledgeValidationCodes.ValidityInvalid, "Validity start must use UTC.", "validity.effectiveFromUtc");
        if (validity.EffectiveToUtc.HasValue && validity.EffectiveToUtc.Value.Offset != TimeSpan.Zero) Add(issues, AstronomyKnowledgeValidationCodes.ValidityInvalid, "Validity end must use UTC.", "validity.effectiveToUtc");
        if (validity.EffectiveFromUtc.HasValue && validity.EffectiveToUtc.HasValue && validity.EffectiveToUtc.Value < validity.EffectiveFromUtc.Value) Add(issues, AstronomyKnowledgeValidationCodes.ValidityInvalid, "Validity end cannot be earlier than validity start.", "validity.effectiveToUtc");
    }

    private static void ValidateAudit(KnowledgeAuditMetadata audit, List<DomainValidationIssue> issues)
    {
        if (audit is null) { Add(issues, AstronomyKnowledgeValidationCodes.AuditMissing, "Knowledge audit metadata is required.", "audit"); return; }
        if (audit.CreatedUtc.Offset != TimeSpan.Zero) Add(issues, AstronomyKnowledgeValidationCodes.AuditInvalid, "Audit creation time must use UTC.", "audit.createdUtc");
        if (audit.UpdatedUtc.HasValue && audit.UpdatedUtc.Value.Offset != TimeSpan.Zero) Add(issues, AstronomyKnowledgeValidationCodes.AuditInvalid, "Audit updated time must use UTC.", "audit.updatedUtc");
        if (audit.UpdatedUtc.HasValue && audit.UpdatedUtc.Value < audit.CreatedUtc) Add(issues, AstronomyKnowledgeValidationCodes.AuditInvalid, "Audit updated time cannot be earlier than creation time.", "audit.updatedUtc");
        if (audit.UpdatedUtc.HasValue != (audit.UpdatedBy is not null)) Add(issues, AstronomyKnowledgeValidationCodes.AuditInvalid, "Audit updated time and actor must be provided together.", "audit.updatedBy");
        if (audit.CreatedBy is not null && string.IsNullOrWhiteSpace(audit.CreatedBy)) Add(issues, AstronomyKnowledgeValidationCodes.AuditInvalid, "Audit created actor cannot be blank when supplied.", "audit.createdBy");
        if (audit.UpdatedBy is not null && string.IsNullOrWhiteSpace(audit.UpdatedBy)) Add(issues, AstronomyKnowledgeValidationCodes.AuditInvalid, "Audit updated actor cannot be blank when supplied.", "audit.updatedBy");
    }

    private static void Add(List<DomainValidationIssue> issues, string code, string message, string path)
        => issues.Add(new DomainValidationIssue(code, message, DomainValidationSeverity.Error, path));
}
