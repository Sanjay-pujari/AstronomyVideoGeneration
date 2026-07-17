using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Diagnostics;

public sealed record ServiceDescriptorDiagnostic(int RegistrationIndex, string ServiceType, string? ImplementationType, string Lifetime, bool ImplementationFactoryPresent, bool ImplementationInstancePresent, string SourceRegistrationMethod);
public sealed record ServiceRegistrationDiagnosticsSnapshot(IReadOnlyList<ServiceDescriptorDiagnostic> RequiredSemanticFactResolverDescriptors, IReadOnlyList<ServiceDescriptorDiagnostic> NarrationGeneratorDescriptors, IReadOnlyList<ServiceDescriptorDiagnostic> SemanticEngineDescriptors, IReadOnlyList<ServiceDescriptorDiagnostic> AdapterRegistryDescriptors, IReadOnlyList<ServiceDescriptorDiagnostic> SourcePolicyCatalogDescriptors);

public static class RuntimeCompositionDiagnostics
{
    public const string FileName = "runtime-composition-diagnostics.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static ServiceRegistrationDiagnosticsSnapshot CaptureServiceRegistrations(IServiceCollection services)
    {
        var indexed = services.Select((Descriptor, Index) => (Descriptor, Index)).ToArray();
        IReadOnlyList<ServiceDescriptorDiagnostic> For(params Type[] serviceTypes) => indexed.Where(x => serviceTypes.Contains(x.Descriptor.ServiceType)).Select(x => new ServiceDescriptorDiagnostic(x.Index, Friendly(x.Descriptor.ServiceType)!, Friendly(x.Descriptor.ImplementationType), x.Descriptor.Lifetime.ToString(), x.Descriptor.ImplementationFactory is not null, x.Descriptor.ImplementationInstance is not null, "IServiceCollection")).ToArray();
        return new(For(typeof(IRequiredSemanticFactResolver), typeof(RequiredSemanticFactResolver)), For(typeof(NarrationGeneratorV5)), For(typeof(ISemanticResolutionEngineV1)), For(typeof(ISemanticSourceAdapterRegistryV1), typeof(SemanticSourceAdapterRegistryV1)), For(typeof(ISemanticSourcePolicyCatalogV1), typeof(SemanticSourcePolicyCatalogV1)));
    }
    public static void ValidateServiceRegistrations(IServiceCollection services)
    {
        var resolverDescriptors = services.Where(d => d.ServiceType == typeof(IRequiredSemanticFactResolver)).ToArray();
        if (resolverDescriptors.Length != 1) throw new InvalidOperationException($"Expected one effective IRequiredSemanticFactResolver registration but found {resolverDescriptors.Length}.");
        var final = resolverDescriptors[^1];
        if (final.ImplementationType != typeof(RequiredSemanticFactResolver)) throw new InvalidOperationException($"Final IRequiredSemanticFactResolver implementation must be {typeof(RequiredSemanticFactResolver).FullName} but was {Friendly(final.ImplementationType) ?? "<factory/instance>"}.");
    }
    public static object Build(object phaseImplementation, NarrationGeneratorV5 generator, IRequiredSemanticFactResolver resolver, ISemanticResolutionEngineV1? engine, ISemanticSourceAdapterRegistryV1? registry, ISemanticSourcePolicyCatalogV1? catalog, ServiceRegistrationDiagnosticsSnapshot? registrations, object? resolverCall = null)
    {
        var phaseAssembly = phaseImplementation.GetType().Assembly;
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(a => (a.GetName().Name ?? string.Empty).Contains("Astronomy.MediaFactory", StringComparison.OrdinalIgnoreCase)).Select(a => new { fullName = a.FullName, location = SafeLocation(a), informationalVersion = Info(a), moduleVersionId = a.ManifestModule.ModuleVersionId.ToString() }).ToArray();
        var duplicateLoaded = loaded.GroupBy(a => a.fullName?.Split(',')[0] ?? string.Empty).Where(g => g.Select(x => x.location).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 || g.Select(x => x.moduleVersionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1).ToArray();
        var files = FindCandidateDlls().ToArray();
        return new { runtimeMarker = MediaFactoryRuntimeIdentity.SemanticArchitectureMarker, process = new { processId = Environment.ProcessId, processName = Process.GetCurrentProcess().ProcessName, baseDirectory = AppContext.BaseDirectory, currentDirectory = Environment.CurrentDirectory, environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? string.Empty }, phase7 = new { implementationType = Friendly(phaseImplementation.GetType()), assemblyFullName = phaseAssembly.FullName, assemblyLocation = SafeLocation(phaseAssembly), informationalVersion = Info(phaseAssembly), assemblyLastWriteUtc = LastWrite(SafeLocation(phaseAssembly)) }, narrationGenerator = AssemblyObject(generator), requiredSemanticFactResolver = new { serviceType = typeof(IRequiredSemanticFactResolver).FullName, runtimeType = Friendly(resolver.GetType()), assemblyLocation = SafeLocation(resolver.GetType().Assembly), informationalVersion = Info(resolver.GetType().Assembly) }, semanticEngine = AssemblyObject(engine), adapterRegistry = new { runtimeType = Friendly(registry?.GetType()), adapterCount = registry?.Adapters.Count ?? 0, meteorActivityAdapterIds = registry?.Adapters.Where(a => a.AdapterId.Contains("MeteorActivity", StringComparison.OrdinalIgnoreCase) || a.SupportedCapabilityId.Value.Contains("MeteorActivity", StringComparison.OrdinalIgnoreCase)).Select(a => a.AdapterId).ToArray() ?? [] }, sourcePolicyCatalog = new { runtimeType = Friendly(catalog?.GetType()), policyCount = catalog?.Policies.Count ?? 0, meteorActivityPolicyFound = catalog?.Policies.Any(p => p.SemanticCapabilityId.Value.Contains("MeteorActivity", StringComparison.OrdinalIgnoreCase)) ?? false }, serviceRegistrations = new { requiredSemanticFactResolverDescriptors = registrations?.RequiredSemanticFactResolverDescriptors ?? [], narrationGeneratorDescriptors = registrations?.NarrationGeneratorDescriptors ?? [], semanticEngineDescriptors = registrations?.SemanticEngineDescriptors ?? [], adapterRegistryDescriptors = registrations?.AdapterRegistryDescriptors ?? [], sourcePolicyCatalogDescriptors = registrations?.SourcePolicyCatalogDescriptors ?? [] }, resolverCall, loadedAssemblies = loaded, duplicateLoadedAssemblies = duplicateLoaded, duplicateDllCandidates = files.GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => new { fileName = g.Key, paths = g.ToArray() }).ToArray() };
    }
    public static Task WriteAsync(string outputRoot, object diagnostics, CancellationToken ct) { var dir = Path.Combine(outputRoot, "narration-v5"); Directory.CreateDirectory(dir); return File.WriteAllTextAsync(Path.Combine(dir, FileName), JsonSerializer.Serialize(diagnostics, JsonOptions), ct); }
    public static T? TryGetField<T>(object instance, string name) where T : class => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as T;
    private static object? AssemblyObject(object? value) => value is null ? null : new { runtimeType = Friendly(value.GetType()), assemblyLocation = SafeLocation(value.GetType().Assembly), informationalVersion = Info(value.GetType().Assembly) };
    private static string? Friendly(Type? t) => t?.FullName;
    private static string Info(Assembly a) => a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
    private static string SafeLocation(Assembly a) { try { return a.Location; } catch { return string.Empty; } }
    private static string LastWrite(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path).ToString("O") : string.Empty;
    private static IEnumerable<string> FindCandidateDlls() { foreach (var root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }) if (Directory.Exists(root)) foreach (var f in Directory.EnumerateFiles(root, "Astronomy.MediaFactory*.dll", SearchOption.AllDirectories)) yield return f; }
}
