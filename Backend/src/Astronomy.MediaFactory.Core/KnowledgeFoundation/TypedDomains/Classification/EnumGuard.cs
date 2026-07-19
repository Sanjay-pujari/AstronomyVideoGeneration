namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

internal static class EnumGuard
{
    public static AstronomyClassificationQualifier RequireDefined(AstronomyClassificationQualifier value, string parameterName)
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy classification qualifier is not defined.");

    public static Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy.AstronomyEntityKind RequireDefined(Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy.AstronomyEntityKind value, string parameterName)
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy entity kind is not defined.");
}
