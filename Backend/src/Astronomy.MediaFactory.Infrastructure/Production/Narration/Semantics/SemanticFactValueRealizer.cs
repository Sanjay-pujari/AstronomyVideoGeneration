using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using ResolvedSemanticFact = Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts.ResolvedSemanticFact;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public interface ISemanticFactValueRealizer
{
    SemanticFactRealizationResult Realize(ResolvedSemanticFact fact, LanguageProfile language);
    SemanticFactRealizationResult Realize(ResolvedSemanticFactV1 fact, string? legacyFactType, string? structuredFieldPath, LanguageProfile language);
    string RealizeCandidateValue(object? value, string? speakableValue = null, string? localizedDisplayValue = null, string? capability = null, string? structuredFieldPath = null, string? languageCode = null);
}

public sealed record SemanticFactRealizationResult(string? DisplayValue, string? SpeakableValue, string StructuredValueKind, string RealizationSource, ImmutableArray<string> Warnings, bool Succeeded, bool BlocksNarration, string? DiagnosticCode, string? DiagnosticMessage);

public sealed class SemanticFactValueRealizer : ISemanticFactValueRealizer
{
    public static SemanticFactValueRealizer Instance { get; } = new();

    public SemanticFactRealizationResult Realize(ResolvedSemanticFact fact, LanguageProfile language)
        => RealizeValue(fact.CanonicalValue, fact.SpeakableValue, fact.LocalizedDisplayValue, fact.FactType, null, language.LanguageCode, fact.Requiredness.Equals("Required", StringComparison.OrdinalIgnoreCase));

    public SemanticFactRealizationResult Realize(ResolvedSemanticFactV1 fact, string? legacyFactType, string? structuredFieldPath, LanguageProfile language)
        => RealizeValue(fact.TypedValue?.Value ?? fact.CanonicalValue, fact.SpeakableValue, fact.SpeakableValue, legacyFactType ?? fact.CapabilityId.Value, structuredFieldPath, language.LanguageCode, fact.Required);

    public string RealizeCandidateValue(object? value, string? speakableValue = null, string? localizedDisplayValue = null, string? capability = null, string? structuredFieldPath = null, string? languageCode = null)
        => RealizeValue(value, speakableValue, localizedDisplayValue, capability, structuredFieldPath, languageCode, required: false).SpeakableValue ?? string.Empty;

    private static SemanticFactRealizationResult RealizeValue(object? value, string? speakable, string? display, string? capability, string? path, string? language, bool required)
    {
        if (!string.IsNullOrWhiteSpace(speakable)) return Ok(speakable.Trim(), speakable.Trim(), "ProvidedSpeakableValue", "SpeakableValue");
        if (!string.IsNullOrWhiteSpace(display)) return Ok(display.Trim(), display.Trim(), "ProvidedLocalizedDisplayValue", "LocalizedDisplayValue");
        if (value is null) return Fail("NullValue", "Semantic fact value was null.", required);
        var realized = RealizeTyped(value, capability, path, language);
        if (!string.IsNullOrWhiteSpace(realized.Text)) return Ok(realized.Text!, realized.Text!, realized.Kind, realized.Source);
        if (IsApprovedScalar(value)) return Ok(Convert.ToString(value, CultureInfo.InvariantCulture)!, Convert.ToString(value, CultureInfo.InvariantCulture)!, value.GetType().Name, "ScalarFormatter");
        return Fail("UnsupportedStructuredSemanticValue", $"No semantic value realizer is registered for structured type '{value.GetType().FullName}' on capability '{capability ?? "unknown"}'.", required);
    }

    private static (string? Text, string Kind, string Source) RealizeTyped(object value, string? capability, string? path, string? language)
    {
        if (value is ImmutableArray<AstronomicalObjectValue> immutableObjects) return (JoinNames(immutableObjects), "AstronomicalObjectArray", "TypedValueRealizer");
        if (value is IEnumerable<AstronomicalObjectValue> enumerableObjects) return (JoinNames(enumerableObjects), "AstronomicalObjectArray", "TypedValueRealizer");
        if (value is EventWindowValue w) return (w.LocalizedWindowDescription ?? FormatWindow(w, language), "EventWindow", "TypedValueRealizer");
        if (value is AngularSeparationValue a) return ($"{a.Degrees.ToString("0.##", CultureInfo.InvariantCulture)} degrees", "AngularSeparation", "TypedValueRealizer");
        if (value is ObservationLocationValue loc) return (First(loc.LocationName, loc.Latitude.HasValue && loc.Longitude.HasValue ? $"{loc.Latitude:0.####}, {loc.Longitude:0.####}" : null, loc.TimeZone), "ObservationLocation", "TypedValueRealizer");
        if (value is ObservationDirectionValue dir) return (First(dir.LocalizedDescription, dir.CardinalDirection, dir.AzimuthDegrees.HasValue ? $"azimuth {dir.AzimuthDegrees:0.#} degrees" : null), "ObservationDirection", "TypedValueRealizer");
        if (value is DomainScientificKnowledgeValue dk) return (First(dk.PerspectiveAlignmentExplanation, dk.Mechanism, dk.ScientificSignificance, dk.StableObservingPrinciples), "DomainScientificKnowledge", "TypedValueRealizer");
        if (value is MeteorActivityValue m) return (string.Join(", ", new[] { m.Zhr.HasValue ? $"ZHR {m.Zhr.Value.ToString(CultureInfo.InvariantCulture)}" : null, string.IsNullOrWhiteSpace(m.Radiant) ? null : $"radiant near {m.Radiant}", m.VelocityKmS.HasValue ? $"{m.VelocityKmS.Value:0.#} km/s" : null, m.MechanicsExplanation }.Where(x => !string.IsNullOrWhiteSpace(x))), "MeteorActivity", "TypedValueRealizer");
        if (value is EclipseCircumstancesValue e) return (string.Join(", ", new[] { e.EclipseType, e.Maximum.HasValue ? $"maximum at {e.Maximum.Value.ToUniversalTime():yyyy-MM-dd HH:mm} UTC" : null, e.Magnitude.HasValue ? $"magnitude {e.Magnitude:0.##}" : null, e.Obscuration.HasValue ? $"obscuration {(e.Obscuration.Value * 100):0.#}%" : null, e.VisibilityRegion }.Where(x => !string.IsNullOrWhiteSpace(x))), "EclipseCircumstances", "TypedValueRealizer");
        if (value is SafetyGuidanceValue s) return (string.Join(" ", new[] { s.Guidance, s.DirectSolarViewing ? "Use certified solar viewing protection for any direct solar viewing." : null, s.Standard }.Where(x => !string.IsNullOrWhiteSpace(x))), "SafetyGuidance", "TypedValueRealizer");
        return (null, value.GetType().Name, "NoTypedFormatter");
    }

    private static string? FormatWindow(EventWindowValue w, string? language)
        => First(w.PeakUtc.HasValue ? $"{w.PeakUtc.Value.ToUniversalTime():yyyy-MM-dd HH:mm} UTC" : null, w.StartUtc.HasValue ? $"starting {w.StartUtc.Value.ToUniversalTime():yyyy-MM-dd HH:mm} UTC" : null);
    private static string JoinNames(IEnumerable<AstronomicalObjectValue> objects) => JoinHuman(objects.Select(o => o.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    private static string JoinHuman(IReadOnlyList<string> names) => names.Count switch { 0 => string.Empty, 1 => names[0], 2 => $"{names[0]} and {names[1]}", _ => string.Join(", ", names.Take(names.Count - 1)) + ", and " + names[^1] };
    private static string? First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
    private static bool IsApprovedScalar(object value) => value is string or char or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or DateTime or DateTimeOffset or Guid or Enum;
    private static SemanticFactRealizationResult Ok(string display, string speak, string kind, string source) => new(display, speak, kind, source, [], true, false, null, null);
    private static SemanticFactRealizationResult Fail(string code, string message, bool required) => new(null, null, "UnsupportedStructuredValue", "ControlledFailure", [message], false, required, code, message);
}
