using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public sealed class AstronomyCrossDomainValidationArchitectureTests
{
    private static readonly string[] ForbiddenTokens =
    {
        "DbContext", "Repository", "Controller", "HttpClient", "BackgroundService", "IHostedService",
        "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow",
        "Activator.CreateInstance", "FormatterServices", "BinaryFormatter", "Skyfield", "SPICE", "Horizons",
        "JPL", "Stellarium", "Ephemeris", "TransformCoordinate", "ConvertCoordinate", "ConvertFrame",
        "ConvertUnit", "PropagateOrbit", "PredictEvent", "ExpandRecurrence", "GenerateOccurrences", "ScheduleJob"
    };

    [Fact]
    public void CrossDomainProductionSurface_IsCompleteAndPure()
    {
        var project = FindProjectDirectory();
        var crossDomain = Path.Combine(project, "KnowledgeFoundation", "Validation", "CrossDomain");
        Assert.True(Directory.Exists(crossDomain), crossDomain);
        var productionFiles = Directory.GetFiles(crossDomain, "*.cs", SearchOption.AllDirectories).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(productionFiles);
        Assert.Contains(productionFiles, f => Path.GetFileName(f) == "AstronomyCrossDomainValidation.cs");

        var texts = productionFiles.ToDictionary(f => f, File.ReadAllText);
        foreach (var file in productionFiles)
        {
            Assert.False(string.IsNullOrWhiteSpace(texts[file]));
            AssertForbiddenTokensAreAbsent(file, texts[file]);
        }

        var allText = string.Join("\n", texts.Values);
        foreach (var file in productionFiles)
        {
            Assert.Contains(Path.GetFileName(file) == "AstronomyCrossDomainValidation.cs" ? "AddAstronomyCrossDomainValidation" : Path.GetFileNameWithoutExtension(file), allText);
        }

        foreach (var typedDomainFile in Directory.GetFiles(Path.Combine(project, "KnowledgeFoundation", "TypedDomains"), "*.cs", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain", File.ReadAllText(typedDomainFile), StringComparison.Ordinal);
        }
        Assert.DoesNotContain("DisplayName", allText);
        Assert.Contains("LeftPayloadIndex", allText);
        Assert.Contains("RightPayloadIndex", allText);
        Assert.Contains("OriginBody", allText);
        Assert.Contains("CentralBody", allText);

        var services = new ServiceCollection().AddAstronomyCrossDomainValidation().AddAstronomyCrossDomainValidation();
        using var provider = services.BuildServiceProvider();
        var descriptors = provider.GetRequiredService<IAstronomyCrossDomainValidationRuleRegistry>().Descriptors;
        var rules = provider.GetServices<IAstronomyCrossDomainValidationRule>().ToArray();
        var publicIds = typeof(AstronomyEntityConsistencyValidationRule).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(AstronomyEntityConsistencyValidationRule).Namespace && typeof(IAstronomyCrossDomainValidationRule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .Select(t => (Type: t, Rule: (IAstronomyCrossDomainValidationRule)ActivatorUtilities.CreateInstance(provider, t)))
            .OrderBy(x => x.Rule.Order).ToArray();
        Assert.Equal(publicIds.Select(x => x.Rule.RuleId), publicIds.Select(x => x.Rule.RuleId).Distinct(StringComparer.Ordinal));
        Assert.Equal(publicIds.Length, descriptors.Count);
        Assert.Equal(publicIds.Length, rules.Select(r => r.RuleId).Distinct(StringComparer.Ordinal).Count());
        foreach (var (type, rule) in publicIds)
        {
            var descriptor = Assert.Single(descriptors.Where(d => d.RuleId == rule.RuleId));
            Assert.Equal(type, descriptor.RuleType);
            Assert.Equal(rule.Order, descriptor.Order);
            Assert.Contains(type.Name, allText);
            Assert.Contains("yield return Issue", GetTypeSlice(allText, type.Name));
        }
    }

    private static string FindProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Astronomy.MediaFactory.Core", "Astronomy.MediaFactory.Core.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Astronomy.MediaFactory.Core project folder was not found.");
    }

    private static void AssertForbiddenTokensAreAbsent(string file, string text)
    {
        foreach (var token in ForbiddenTokens)
        {
            Assert.False(Regex.IsMatch(text, $@"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])"), $"Forbidden token {token} found in {file}.");
        }
    }

    private static string GetTypeSlice(string text, string typeName)
    {
        var start = text.IndexOf($"class {typeName}", StringComparison.Ordinal);
        Assert.True(start >= 0, typeName);
        var next = text.IndexOf("public sealed class ", start + 1, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }
}
