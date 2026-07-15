using System.Globalization;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using ResolvedSemanticFact = Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts.ResolvedSemanticFact;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public static class LegacyRequiredSemanticFactCompatibilityMapper
{
    public static ResolvedSemanticFact? Map(ResolvedSemanticFactV1 fact, string legacyFactType, string? beatId, string requiredness, string language)
    {
        if (fact.Status is not (SemanticResolutionStatusV1.Resolved or SemanticResolutionStatusV1.ResolvedByCombination)) return null;

        var mapping = LegacySemanticCapabilityMapV1.Entries.FirstOrDefault(e => e.LegacyTerm.Equals(legacyFactType, StringComparison.OrdinalIgnoreCase));
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
            "Source",
            null,
            provenance);
    }
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
            return path.Equals("zhr", StringComparison.OrdinalIgnoreCase) ? (m.Zhr, "meteors/hour", m.Zhr?.ToString(CultureInfo.InvariantCulture), $"{sourceRoot}.MeteorActivity.zhr") : (value, null, fact.SpeakableValue, $"{sourceRoot}.MeteorActivity");
        if (value is ObjectKnowledgeValue ok)
        {
            var f = ok.Facts.FirstOrDefault(x => x.Field.Equals(path, StringComparison.OrdinalIgnoreCase));
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

}
