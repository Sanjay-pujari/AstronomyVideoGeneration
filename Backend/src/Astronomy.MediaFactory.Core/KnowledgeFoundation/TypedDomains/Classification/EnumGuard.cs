using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

internal static class EnumGuard
{
    public static AstronomyClassificationQualifier RequireDefined(
        AstronomyClassificationQualifier value,
        string parameterName)
    {
        return Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Astronomy classification qualifier is not defined.");
    }

    public static AstronomyEntityKind RequireDefined(AstronomyEntityKind value, string parameterName)
    {
        return Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Astronomy entity kind is not defined.");
    }
}
