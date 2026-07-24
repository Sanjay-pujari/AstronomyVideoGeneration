using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using ResolvedSemanticFact = Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts.ResolvedSemanticFact;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public static class LegacyRequiredSemanticFactCompatibilityMapper
{
    public const string ObjectKnowledgeAggregateProjectionVersion = "ObjectKnowledgeAggregateProjectionV1";
    public static bool LastObjectKnowledgeAggregateProjectionBranchEntered { get; private set; }
    public static bool LastObjectKnowledgeAggregateProjectionSucceeded { get; private set; }

    public static ResolvedSemanticFact? Map(ResolvedSemanticFactV1 fact, string legacyFactType, string? beatId, string requiredness, string language)
    {
        if (fact.Status is not (SemanticResolutionStatusV1.Resolved or SemanticResolutionStatusV1.ResolvedByCombination)) return null;

        if (fact.CapabilityId.Value.Equals(SemanticCapabilityVocabularyV1.ObjectKnowledge, StringComparison.OrdinalIgnoreCase) &&
            legacyFactType.Equals(SemanticCapabilityVocabularyV1.ObjectKnowledge, StringComparison.OrdinalIgnoreCase))
            return MapObjectKnowledgeAggregate(fact, beatId, requiredness, language);

        var mapping = SemanticDefaults.LegacySemanticCapabilityResolverV1.Resolve(legacyFactType);
        var projected = ProjectStructuredValue(fact, mapping.StructuredFieldPath, legacyFactType);
        var realized = SemanticFactValueRealizer.Instance.Realize(fact, legacyFactType, mapping.StructuredFieldPath, LanguageProfileResolver.Resolve(language));
        if (projected.DisplayValue is null && realized.Succeeded) projected = (projected.Value, projected.Unit, realized.SpeakableValue, projected.SourcePropertyPath);
        if (projected.Value is null && realized.Succeeded) projected = (realized.SpeakableValue, projected.Unit, realized.SpeakableValue, projected.SourcePropertyPath);
        if (projected.Value is not null && !realized.Succeeded && fact.Required) return null;
        if (projected.Value is null) return null;

        var sourcePath = projected.SourcePropertyPath
            ?? fact.Provenance.FirstOrDefault().SourcePropertyPath
            ?? fact.WinningCandidateId
            ?? fact.CapabilityId.Value;
        var provenance = fact.Provenance.Select(p => p.SourcePropertyPath)
            .Append(sourcePath)
            .Append(mapping.StructuredFieldPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ResolvedSemanticFact(
            legacyFactType,
            legacyFactType,
            projected.Value,
            projected.Unit,
            fact.CapabilityId.Value,
            fact.WinningSourceId ?? fact.WinningAdapterId ?? "SemanticResolutionEngineV1",
            sourcePath,
            beatId,
            SemanticVerificationStatus.Verified,
            fact.Confidence,
            Enum.Parse<SemanticFactRequiredness>(requiredness, ignoreCase: true),
            projected.DisplayValue ?? realized.SpeakableValue ?? fact.SpeakableValue,
            projected.DisplayValue ?? realized.SpeakableValue ?? fact.SpeakableValue,
            language,
            true,
            string.Equals(legacyFactType, fact.CapabilityId.Value, StringComparison.OrdinalIgnoreCase) ? "Source" : "Derived",
            string.Equals(legacyFactType, fact.CapabilityId.Value, StringComparison.OrdinalIgnoreCase) ? null : $"V1Projection.{fact.CapabilityId.Value}.{legacyFactType}",
            provenance);
    }

    private static ResolvedSemanticFact? MapObjectKnowledgeAggregate(ResolvedSemanticFactV1 fact, string? beatId, string requiredness, string language)
    {
        LastObjectKnowledgeAggregateProjectionBranchEntered = true;
        LastObjectKnowledgeAggregateProjectionSucceeded = false;
        if ((fact.TypedValue?.Value ?? fact.CanonicalValue) is not ObjectKnowledgeValue value || value.Facts.IsDefaultOrEmpty) return null;
        var projection = ObjectKnowledgeNarrationFormatter.Format(value, LanguageProfileResolver.Resolve(language));
        if (projection.SafeFactKeys.IsDefaultOrEmpty || string.IsNullOrWhiteSpace(projection.SpeakableValue)) return null;
        LastObjectKnowledgeAggregateProjectionSucceeded = true;
        var sourceInputs = projection.SourceInputs.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new ResolvedSemanticFact(
            SemanticCapabilityVocabularyV1.ObjectKnowledge,
            SemanticCapabilityVocabularyV1.ObjectKnowledge,
            value,
            null,
            SemanticCapabilityVocabularyV1.ObjectKnowledge,
            fact.WinningSourceId ?? fact.WinningAdapterId ?? SemanticSourcePolicyVocabularyV1.AstronomyObjectKnowledgeProvider,
            "AstronomyObjectKnowledge.ObjectKnowledge",
            beatId,
            SemanticVerificationStatus.Verified,
            fact.Confidence,
            Enum.Parse<SemanticFactRequiredness>(requiredness, ignoreCase: true),
            projection.LocalizedDisplayValue,
            projection.SpeakableValue,
            language,
            projection.SafeFactKeys.Length > 0,
            "Source",
            null,
            sourceInputs);
    }

    private static ObjectKnowledgeFactV1? FindObjectKnowledgeFact(ObjectKnowledgeValue value, string requestedField)
        => value.Facts.IsDefaultOrEmpty || string.IsNullOrWhiteSpace(requestedField)
            ? null
            : value.Facts
                .Where(f => f.Field.Equals(requestedField, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Field, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Provenance.SourcePropertyPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

    private static (object? Value, string? Unit, string? DisplayValue, string? SourcePropertyPath) ProjectStructuredValue(ResolvedSemanticFactV1 fact, string? structuredFieldPath, string legacyFactType)
    {
        var value = fact.TypedValue?.Value ?? fact.CanonicalValue;
        if (value is null) return (null, null, null, null);
        var sourceRoot = fact.WinningSourceId switch
        {
            "ProductionEventIntelligence" => "ProductionEventIntelligence",
            "ObservationMetadata" => "ObservationMetadata",
            "DocumentaryContract" => "DocumentaryContract",
            "AstronomyObjectKnowledgeProvider" => "AstronomyObjectKnowledge",
            "AstronomyDomainKnowledgeProvider" => "AstronomyDomainKnowledge",
            _ => fact.Provenance.FirstOrDefault().SourceId ?? fact.WinningSourceId ?? fact.CapabilityId.Value
        };
        var path = structuredFieldPath?.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? legacyFactType;
        static string? S(DateTimeOffset? d) => d?.ToString("O", CultureInfo.InvariantCulture);
        if (value is CanonicalAstronomyEventIdentity eid)
        {
            var v = path.ToLowerInvariant() switch
            {
                "displayname" or "name" => eid.DisplayName ?? eid.ShortTitle ?? eid.Title ?? eid.SourceEventType ?? eid.CanonicalEventType,
                _ => eid.DisplayName ?? eid.ShortTitle ?? eid.Title ?? eid.CanonicalEventType
            };
            return (v, null, v, $"{sourceRoot}.EventIdentity.{path}");
        }
        if (value is EventWindowValue w)
        {
            return path.ToLowerInvariant() switch
            {
                "peakutc" => (S(w.PeakUtc), "UTC", S(w.PeakUtc), $"{sourceRoot}.EventWindow.PeakUtc"),
                "localpeaktime" => (w.LocalizedWindowDescription ?? S(w.PeakUtc), null, w.LocalizedWindowDescription ?? S(w.PeakUtc), $"{sourceRoot}.EventWindow.LocalizedWindowDescription"),
                "bestviewingwindowlocal" or "viewingwindow" or "window" or "observationtiming" or "peaktime" or "peakwindow" or "timing" or "date" => (w.LocalizedWindowDescription ?? S(w.PeakUtc) ?? S(w.StartUtc), null, w.LocalizedWindowDescription ?? S(w.PeakUtc) ?? S(w.StartUtc), $"{sourceRoot}.EventWindow.{(w.LocalizedWindowDescription is not null ? nameof(EventWindowValue.LocalizedWindowDescription) : w.PeakUtc.HasValue ? nameof(EventWindowValue.PeakUtc) : nameof(EventWindowValue.StartUtc))}"),
                "starttime" => (S(w.StartUtc), "UTC", S(w.StartUtc), $"{sourceRoot}.EventWindow.StartUtc"),
                "endtime" => (S(w.EndUtc), "UTC", S(w.EndUtc), $"{sourceRoot}.EventWindow.EndUtc"),
                _ => (w.LocalizedWindowDescription ?? S(w.PeakUtc) ?? S(w.StartUtc), null, w.LocalizedWindowDescription, $"{sourceRoot}.EventWindow")
            };
        }
        if (value is MeteorActivityValue m)
        {
            return path.ToLowerInvariant() switch
            {
                "zhr" => (m.Zhr, "meteors/hour", m.Zhr?.ToString(CultureInfo.InvariantCulture), $"{sourceRoot}.MeteorActivity.zhr"),
                "radiant" => (FirstNonEmpty(m.Radiant, m.RadiantConstellation), null, FirstNonEmpty(m.Radiant, m.RadiantConstellation), $"{sourceRoot}.MeteorActivity.Radiant"),
                "peakwindow" => (m.PeakWindow?.LocalizedWindowDescription ?? m.ActivityWindow?.LocalizedWindowDescription ?? m.PeakWindow?.PeakUtc?.ToString("O", CultureInfo.InvariantCulture), null, m.PeakWindow?.LocalizedWindowDescription ?? m.ActivityWindow?.LocalizedWindowDescription ?? m.PeakWindow?.PeakUtc?.ToString("O", CultureInfo.InvariantCulture), $"{sourceRoot}.MeteorActivity.PeakWindow"),
                _ => (value, null, fact.SpeakableValue, $"{sourceRoot}.MeteorActivity")
            };
        }
        if (value is ObjectKnowledgeValue ok)
        {
            var f = FindObjectKnowledgeFact(ok, path);
            return f is null ? (null, null, null, null) : (f.Value, null, f.Value, f.Provenance.SourcePropertyPath);
        }
        if (value is DomainScientificKnowledgeValue dk)
        {
            var v = path.ToLowerInvariant() switch { "apparentalignment" or "apparentpairingscience" or "perspective" or "physicalproximityclarification" => dk.PerspectiveAlignmentExplanation, "mechanism" => dk.Mechanism, "scientificimportance" => dk.ScientificSignificance, _ => dk.StableObservingPrinciples ?? dk.PerspectiveAlignmentExplanation ?? dk.Mechanism };
            return (v, null, v, $"{sourceRoot}.DomainKnowledge.{path}");
        }
        if (value is ObservationEquipmentValue eq)
        {
            object? v = path.ToLowerInvariant() switch { "binocularguidance" => eq.BinocularSuitable, "nakedeye" => eq.NakedEyeSuitable, "telescopeguidance" => eq.TelescopeSuitable, _ => value };
            return (v, null, v?.ToString(), $"{sourceRoot}.EquipmentGuidance.{path}");
        }
        if (value is ObservationLocationValue loc)
        {
            var v = path.ToLowerInvariant() switch
            {
                "latitude" => loc.Latitude?.ToString(CultureInfo.InvariantCulture),
                "longitude" => loc.Longitude?.ToString(CultureInfo.InvariantCulture),
                "timezone" => loc.TimeZone,
                _ => loc.LocationName ?? loc.TimeZone
            };
            return (v, null, v, $"{sourceRoot}.ObservationLocation.{(path.Equals("timezone", StringComparison.OrdinalIgnoreCase) ? nameof(ObservationLocationValue.TimeZone) : nameof(ObservationLocationValue.LocationName))}");
        }
        if (value is AngularSeparationValue a) return (a.Degrees, "degrees", $"{a.Degrees} degrees", $"{sourceRoot}.AngularSeparation.Degrees");
        var fallback = SemanticFactValueRealizer.Instance.RealizeCandidateValue(value, fact.SpeakableValue, fact.SpeakableValue, legacyFactType, structuredFieldPath);
        return string.IsNullOrWhiteSpace(fallback) ? (null, null, null, fact.Provenance.FirstOrDefault().SourcePropertyPath) : (value, null, fallback, fact.Provenance.FirstOrDefault().SourcePropertyPath);
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

public sealed record ObjectKnowledgeNarrationProjection(string SpeakableValue, string LocalizedDisplayValue, ImmutableArray<string> SafeFactKeys, ImmutableArray<string> OmittedFactKeys, ImmutableArray<string> SourceInputs);

public static class ObjectKnowledgeNarrationFormatter
{
    private static readonly string[] PreferredOrder = ["Name", "ObjectType", "ScientificIdentity", "IdentificationPattern", "MajorStars", "ScientificImportance", "ObservationAdvice"];

    public static ObjectKnowledgeNarrationProjection Format(ObjectKnowledgeValue value, LanguageProfile languageProfile)
    {
        if (value.Facts.IsDefaultOrEmpty) return new(string.Empty, string.Empty, [], [], []);
        var facts = value.Facts
            .Where(f => !string.IsNullOrWhiteSpace(f.Field) && !string.IsNullOrWhiteSpace(f.Value) && f.Provenance.Verified)
            .GroupBy(f => f.Field, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(f => f.Provenance.SourcePropertyPath, StringComparer.OrdinalIgnoreCase).First())
            .ToDictionary(f => f.Field, f => f, StringComparer.OrdinalIgnoreCase);
        var safeKeys = PreferredOrder.Where(facts.ContainsKey).ToImmutableArray();
        var omitted = value.Facts.Select(f => f.Field).Where(k => !safeKeys.Contains(k, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        var inputs = value.Facts.Select(f => f.Provenance.SourcePropertyPath).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        var text = languageProfile.LanguageCode.Equals("hi", StringComparison.OrdinalIgnoreCase)
            ? string.Join(" ", safeKeys.Select(k => $"{k}: {facts[k].Value}"))
            : BuildEnglish(facts, safeKeys);
        return new(text, text, safeKeys, omitted, inputs);
    }

    private static string BuildEnglish(IReadOnlyDictionary<string, ObjectKnowledgeFactV1> facts, ImmutableArray<string> safeKeys)
    {
        var sentences = new List<string>();
        string? V(string key) => facts.TryGetValue(key, out var f) ? f.Value.Trim().TrimEnd('.') : null;
        var name = V("Name"); var type = V("ObjectType");
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(type)) sentences.Add($"{name} is {Article(type)} {type}.");
        else if (!string.IsNullOrWhiteSpace(name)) sentences.Add(name + ".");
        foreach (var key in PreferredOrder.Skip(2))
        {
            var value = V(key);
            if (!string.IsNullOrWhiteSpace(value)) sentences.Add(value + ".");
        }
        return string.Join(" ", sentences);
    }

    private static string Article(string text) => Regex.IsMatch(text, "^[aeiou]", RegexOptions.IgnoreCase) ? "an" : "a";
}
