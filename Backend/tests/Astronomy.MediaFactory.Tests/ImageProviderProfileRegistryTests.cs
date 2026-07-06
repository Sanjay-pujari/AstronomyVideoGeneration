using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ImageProviderProfileRegistryTests
{
    [Fact]
    public void GenericProviderProfile_UsesConservativeDefaults()
    {
        var profile = new GenericImageProviderProfile();

        Assert.Equal("generic", profile.ProviderName);
        Assert.Equal(ImageProviderType.Unknown, profile.ProviderType);
        Assert.False(profile.Capabilities.SupportsNegativePrompt);
        Assert.False(profile.Capabilities.SupportsStructuredInput);
        Assert.False(profile.Capabilities.SupportsJsonInput);
        Assert.False(profile.Capabilities.SupportsImageEditing);
        Assert.False(profile.Capabilities.SupportsImageReferences);
        Assert.False(profile.Capabilities.SupportsMultipleImages);
        Assert.Equal("plainText", profile.DefaultPromptStrategy);
        Assert.Equal(VisualIntelligenceContractVersions.GenericProviderProfileVersion, profile.ProviderProfileVersion);
        Assert.Equal(VisualIntelligenceContractVersions.ProviderCapabilitiesVersion, profile.Capabilities.CapabilitiesVersion);
        Assert.True((bool)profile.Capabilities.ProviderMetadata["plainTextPromptSupported"]!);
    }

    [Fact]
    public void AzureImageProviderProfile_CapabilitiesSerializeCorrectly()
    {
        var profile = new AzureImageProviderProfile();

        Assert.Equal("AzureImage", profile.ProviderName);
        Assert.Equal(ImageProviderType.AzureImage, profile.ProviderType);
        Assert.Equal(VisualIntelligenceContractVersions.AzureImageProviderProfileVersion, profile.ProviderProfileVersion);
        Assert.False(profile.Capabilities.SupportsNegativePrompt);
        Assert.False(profile.Capabilities.SupportsStructuredInput);
        Assert.False(profile.Capabilities.SupportsJsonInput);
        Assert.True(profile.Capabilities.SupportsTypography);
        Assert.True(profile.Capabilities.SupportsAspectRatio);
        Assert.False(profile.Capabilities.SupportsQualityOptions);
        Assert.False(profile.Capabilities.SupportsMultipleImages);

        var json = JsonSerializer.Serialize(profile.Capabilities, VisualIntelligenceJson.CreateSerializerOptions());
        var roundTrip = JsonSerializer.Deserialize<ImageProviderCapabilities>(json, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.NotNull(roundTrip);
        Assert.True(roundTrip!.SupportsAspectRatio);
        Assert.Contains("16:9", roundTrip.SupportedAspectRatios);
        Assert.Equal(VisualIntelligenceContractVersions.ProviderCapabilitiesVersion, roundTrip.CapabilitiesVersion);
    }

    [Fact]
    public void Resolver_ReturnsRegisteredProfile_ByTypeAndName()
    {
        var registered = new TestImageProviderProfile();
        var registry = new ImageProviderProfileRegistry([new GenericImageProviderProfile(), registered]);

        var byType = registry.Resolve(ImageProviderType.ExternalProvider);
        var byName = registry.Resolve("TestProvider");

        Assert.False(byType.FallbackUsed);
        Assert.Same(registered, byType.Profile);
        Assert.Contains(byType.Diagnostics, d => d.Code == "image_provider_profile.resolved");
        Assert.False(byName.FallbackUsed);
        Assert.Same(registered, byName.Profile);
    }

    [Fact]
    public void Resolver_ReturnsAzureImageProfile_ByType()
    {
        var registry = new ImageProviderProfileRegistry([new GenericImageProviderProfile(), new AzureImageProviderProfile()]);

        var result = registry.Resolve(ImageProviderType.AzureImage);

        Assert.False(result.FallbackUsed);
        Assert.IsType<AzureImageProviderProfile>(result.Profile);
        Assert.Contains(result.Diagnostics, d => d.Code == "image_provider_profile.resolved");
    }

    [Fact]
    public void Resolver_FallsBackForUnknownProvider_WithoutThrowing()
    {
        var registry = new ImageProviderProfileRegistry([new GenericImageProviderProfile()]);

        var result = registry.Resolve(ImageProviderType.AzureImage2);

        Assert.True(result.FallbackUsed);
        Assert.IsType<GenericImageProviderProfile>(result.Profile);
        Assert.Contains(result.Diagnostics, d => d.Code == "image_provider_profile.missing");
        Assert.Contains(result.Diagnostics, d => d.Code == "image_provider_profile.generic_fallback_used");
        Assert.Contains(result.Diagnostics, d => d.Code == "image_provider_profile.unsupported_provider_requested");
    }

    [Fact]
    public void Resolver_CanThrowForUnknownProvider_WhenExplicitlyRequested()
    {
        var registry = new ImageProviderProfileRegistry([new GenericImageProviderProfile()]);

        Assert.Throws<KeyNotFoundException>(() => registry.Resolve("missing-provider", throwIfUnknown: true));
    }

    [Fact]
    public void Capabilities_SerializeAndDeserializeCorrectly()
    {
        var capabilities = new ImageProviderCapabilities
        {
            SupportsNegativePrompt = true,
            SupportsAspectRatio = true,
            SupportsQualityOptions = true,
            MaxPromptLength = 4000,
            MaxNegativePromptLength = 1000,
            SupportedAspectRatios = ["16:9", "9:16"],
            SupportedOutputFormats = ["png"],
            SupportedQualityLevels = ["standard", "high"],
            ProviderMetadata = new Dictionary<string, object?> { ["model"] = "unit-test" }
        };

        var json = JsonSerializer.Serialize(capabilities, VisualIntelligenceJson.CreateSerializerOptions());
        var roundTrip = JsonSerializer.Deserialize<ImageProviderCapabilities>(json, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.NotNull(roundTrip);
        Assert.True(roundTrip!.SupportsNegativePrompt);
        Assert.True(roundTrip.SupportsAspectRatio);
        Assert.Equal(4000, roundTrip.MaxPromptLength);
        Assert.Contains("9:16", roundTrip.SupportedAspectRatios);
        Assert.Contains("high", roundTrip.SupportedQualityLevels);
        Assert.Equal(VisualIntelligenceContractVersions.ProviderCapabilitiesVersion, roundTrip.CapabilitiesVersion);
    }

    [Fact]
    public void ServiceRegistration_AddsRegistryAndGenericProfileWithoutImageGenerationServices()
    {
        var services = new ServiceCollection();
        services.AddVisualIntelligenceOrchestration();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IImageProviderProfileRegistry>();
        var result = registry.Resolve(ImageProviderType.AzureImage);

        Assert.False(result.FallbackUsed);
        Assert.IsType<AzureImageProviderProfile>(result.Profile);
    }

    private sealed class TestImageProviderProfile : IImageProviderProfile
    {
        public string ProviderName => "TestProvider";
        public ImageProviderType ProviderType => ImageProviderType.ExternalProvider;
        public string ProviderProfileVersion => "test-profile-v1";
        public ImageProviderCapabilities Capabilities { get; } = new() { SupportsNegativePrompt = true, SupportedOutputFormats = ["png"] };
        public string DefaultPromptStrategy => "testPlainText";
        public IReadOnlyList<string> ProviderNotes => ["Test-only profile; does not generate images."];
        public IReadOnlyList<DiagnosticMessage> Diagnostics => [];
    }
}
