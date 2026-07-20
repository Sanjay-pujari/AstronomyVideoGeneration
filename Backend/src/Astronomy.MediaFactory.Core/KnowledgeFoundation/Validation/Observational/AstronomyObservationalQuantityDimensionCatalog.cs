using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;

internal static class AstronomyObservationalQuantityDimensionCatalog
{
    public static bool TryGetExpectedDimension(AstronomyObservationalQuantityCategory category, out AstronomyMeasurementDimension dimension)
    {
        dimension = category switch
        {
            AstronomyObservationalQuantityCategory.Brightness => AstronomyMeasurementDimension.Magnitude,
            AstronomyObservationalQuantityCategory.AngularSize => AstronomyMeasurementDimension.Angle,
            AstronomyObservationalQuantityCategory.AngularSeparation => AstronomyMeasurementDimension.Angle,
            AstronomyObservationalQuantityCategory.HorizontalPosition => AstronomyMeasurementDimension.Angle,
            AstronomyObservationalQuantityCategory.Illumination => AstronomyMeasurementDimension.Percentage,
            _ => default
        };
        return category is AstronomyObservationalQuantityCategory.Brightness or AstronomyObservationalQuantityCategory.AngularSize or AstronomyObservationalQuantityCategory.AngularSeparation or AstronomyObservationalQuantityCategory.HorizontalPosition or AstronomyObservationalQuantityCategory.Illumination;
    }
}
