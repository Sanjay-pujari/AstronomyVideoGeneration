using System.Reflection;

namespace Astronomy.MediaFactory.Infrastructure;

public static class MediaFactoryRuntimeIdentity
{
    public const string SemanticArchitectureMarker = "Sprint4G4-MeteorActivity-CanonicalProjection";

    public static string AssemblyName => typeof(MediaFactoryRuntimeIdentity).Assembly.FullName ?? string.Empty;
    public static string AssemblyLocation => typeof(MediaFactoryRuntimeIdentity).Assembly.Location;
    public static string InformationalVersion => typeof(MediaFactoryRuntimeIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
    public static string FileVersion => typeof(MediaFactoryRuntimeIdentity).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? string.Empty;
    public static DateTime AssemblyLastWriteUtc => File.Exists(AssemblyLocation) ? File.GetLastWriteTimeUtc(AssemblyLocation) : DateTime.MinValue;
}
