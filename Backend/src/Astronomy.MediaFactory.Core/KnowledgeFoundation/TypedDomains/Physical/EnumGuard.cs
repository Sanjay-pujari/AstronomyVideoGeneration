namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;

internal static class EnumGuard
{
    public static AstronomyPhysicalPropertyCategory RequireDefined(
        AstronomyPhysicalPropertyCategory value,
        string parameterName)
    {
        return Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Astronomy physical property category is not defined.");
    }

    public static AstronomyPhysicalPropertyQualifier RequireDefined(
        AstronomyPhysicalPropertyQualifier value,
        string parameterName)
    {
        return Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Astronomy physical property qualifier is not defined.");
    }
}
