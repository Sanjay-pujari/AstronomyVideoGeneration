using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyFamilyProfileCatalogV1Tests
{
    private readonly AstronomyFamilyProfileCatalogV1 _catalog = new();

    [Fact] public void Exactly10ActiveV1ProfilesExist() => Assert.Equal(10, _catalog.Profiles.Count(p => p.ActiveInV1));

    [Fact]
    public void EveryActiveV1FamilyResolvesToItself()
    {
        foreach (var id in AstronomyFamilyVocabularyV1.ActiveFamilyIds)
        {
            var result = _catalog.ResolveEventType(id);
            Assert.Equal(AstronomyFamilyResolutionStatusV1.Resolved, result.Status);
            Assert.Equal(id, result.CanonicalFamilyId);
            Assert.Equal(id, result.ProfileId);
            Assert.False(result.AliasApplied);
        }
    }

    [Fact] public void PlanetGroupingProfileExistsAndRequiresThreeObjects() => Assert.Equal(3, _catalog.GetRequired(AstronomyFamilyVocabularyV1.PlanetGrouping).Policy.MinimumObjectCount);
    [Fact] public void SolarAndLunarEclipsesResolveToSeparateProfiles() { Assert.Equal("SolarEclipse", _catalog.ResolveEventType("Solar Eclipse").ProfileId); Assert.Equal("LunarEclipse", _catalog.ResolveEventType("Lunar Eclipse").ProfileId); }
    [Fact] public void NoActiveGenericEclipseProfileExists() => Assert.False(_catalog.IsActiveV1Family("Eclipse"));
    [Fact] public void FutureFamiliesAreClassifiedButNotActive() { foreach (var id in AstronomyFamilyVocabularyV1.FutureFamilyIds) { Assert.True(_catalog.IsFutureFamily(id)); Assert.False(_catalog.IsActiveV1Family(id)); Assert.Equal(AstronomyFamilyResolutionStatusV1.FutureFamily, _catalog.ResolveEventType(id).Status); } }
    [Fact] public void UnknownFamiliesReturnUnsupported() => Assert.Equal(AstronomyFamilyResolutionStatusV1.Unsupported, _catalog.ResolveEventType("Unknown").Status);
    [Fact] public void CatalogValidationSucceeds() => Assert.True(_catalog.Validate().IsValid, string.Join("; ", _catalog.Validate().Errors));
    [Fact] public void ContractsJsonRoundTrip() { var p = _catalog.Profiles.First(); Assert.Equal(p, JsonSerializer.Deserialize<AstronomyFamilyProfileV1>(JsonSerializer.Serialize(p))); var r = _catalog.ResolveEventType("DeepSky"); Assert.Equal(r, JsonSerializer.Deserialize<AstronomyFamilyResolutionV1>(JsonSerializer.Serialize(r))); }
    [Fact] public void CollectionsAreImmutableAndStructurallyEqual() { Assert.IsAssignableFrom<IReadOnlyCollection<AstronomyFamilyProfileV1>>(_catalog.Profiles); Assert.False(_catalog.Profiles is ICollection<AstronomyFamilyProfileV1> { IsReadOnly: false }); Assert.Equal(new AstronomyFamilyProfileCatalogV1().Profiles.ToArray(), _catalog.Profiles.ToArray()); }
    [Fact] public void BothLongAndShortStructuresExistForEveryActiveProfile() { foreach (var p in _catalog.Profiles) { Assert.NotEmpty(p.LongFormStructure.Beats); Assert.NotEmpty(p.ShortFormStructure.Beats); } }
}
