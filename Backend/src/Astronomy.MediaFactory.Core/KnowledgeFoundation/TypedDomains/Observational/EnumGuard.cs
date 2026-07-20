namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

internal static class EnumGuard
{
    public static T RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, $"{typeof(T).Name} is not defined.");
}
