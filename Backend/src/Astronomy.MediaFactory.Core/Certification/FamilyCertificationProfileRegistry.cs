using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.Certification;

public sealed class FamilyCertificationProfileRegistry : IFamilyCertificationProfileRegistry
{
    private readonly IReadOnlyDictionary<string, IFamilyCertificationProfile> profiles;

    public FamilyCertificationProfileRegistry(IEnumerable<IFamilyCertificationProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var builder = new Dictionary<string, IFamilyCertificationProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles.OrderBy(p => p.FamilyId, StringComparer.OrdinalIgnoreCase))
        {
            ArgumentNullException.ThrowIfNull(profile);
            AddKey(builder, profile.FamilyId, profile);
            foreach (var alias in profile.SupportedEventTypeAliases.Order(StringComparer.OrdinalIgnoreCase)) AddKey(builder, alias, profile);
        }
        this.profiles = builder;
    }

    public IFamilyCertificationProfile Resolve(string eventType)
    {
        if (TryResolve(eventType, out var profile)) return profile;
        throw new KeyNotFoundException($"Unsupported certification family event type '{eventType}'. Register an IFamilyCertificationProfile for this EventType or alias.");
    }

    public bool TryResolve(string eventType, out IFamilyCertificationProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("EventType must be non-empty.", nameof(eventType));
        return profiles.TryGetValue(eventType.Trim(), out profile);
    }

    private static void AddKey(Dictionary<string, IFamilyCertificationProfile> builder, string key, IFamilyCertificationProfile profile)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException($"Certification profile '{profile.FamilyId}' contains an empty family id or event type alias.", nameof(profile));
        var normalized = key.Trim();
        if (builder.TryGetValue(normalized, out var existing) && !ReferenceEquals(existing, profile))
        {
            throw new InvalidOperationException($"Duplicate certification family event type alias '{normalized}' was registered by profiles '{existing.FamilyId}' and '{profile.FamilyId}'.");
        }
        builder[normalized] = profile;
    }
}

public static class CgA1CertificationFoundationServiceCollectionExtensions
{
    public static IServiceCollection AddCgA1CertificationFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IFamilyCertificationProfile, MeteorShowerCertificationProfile>();
        services.AddSingleton<IFamilyCertificationProfile, PlanetConjunctionCertificationProfile>();
        services.AddSingleton<IFamilyCertificationProfile, ConstellationCertificationProfile>();
        services.AddSingleton<IFamilyCertificationProfileRegistry, FamilyCertificationProfileRegistry>();
        services.AddCgA1PhaseCertification();
        services.AddSingleton<ICertificationPathService, CertificationPathService>();
        services.AddSingleton<ICertificationOutputLock, CertificationOutputLock>();
        services.AddSingleton<ICertificationSummaryAggregator, CertificationSummaryAggregator>();
        services.AddSingleton<ICertificationDashboardMapper, CertificationDashboardMapper>();
        services.AddSingleton<ICertificationReportWriter, CertificationReportWriter>();
        services.AddSingleton<ICertificationCoordinator, CertificationCoordinator>();
        return services;
    }
}
