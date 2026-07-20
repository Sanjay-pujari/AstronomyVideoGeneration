using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
internal static class TemporalGuards
{
    public const int MaxIdLength = 128; public const int MaxTextLength = 512; public const int MaxNameLength = 160;
    public static string Token(string value, string parameterName, string displayName, int maxLength = MaxIdLength) => KnowledgeId.NormalizeToken(value, parameterName, displayName, maxLength).ToLowerInvariant();
    public static T Defined<T>(T value, string parameterName) where T : struct, Enum => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, $"{typeof(T).Name} is not defined.");
    public static DateTimeOffset Utc(DateTimeOffset value, string parameterName) => value.Offset == TimeSpan.Zero ? value : throw new ArgumentException("Temporal instants must use UTC (zero offset).", parameterName);
    public static AstronomyMeasurement RequireDimension(AstronomyMeasurement? value, AstronomyMeasurementDimension dimension, string parameterName)
    { ArgumentNullException.ThrowIfNull(value, parameterName); if (value.Unit.Dimension != dimension) throw new ArgumentException($"Measurement must use the {dimension} dimension.", parameterName); return value; }
    public static AstronomyMeasurement Positive(AstronomyMeasurement? value, AstronomyMeasurementDimension dimension, string parameterName)
    { var measurement = RequireDimension(value, dimension, parameterName); if (measurement.Value <= 0m) throw new ArgumentOutOfRangeException(parameterName, measurement.Value, "Measurement value must be greater than zero."); return measurement; }
    public static string? OptionalText(string? value, int maxLength, string parameterName, string displayName) => Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.TypedKnowledgeTextGuards.NormalizeOptionalText(value, maxLength, parameterName, displayName);
}
