using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Tests;

public sealed class SceneAssetsV3SceneContractTests
{
    public static TheoryData<string> LongEventFamilies => new()
    {
        "MeteorShower",
        "PlanetConjunction",
        "NamedFullMoon",
        "SolarEclipse"
    };

    [Theory]
    [MemberData(nameof(LongEventFamilies))]
    public void LongFormat_UsesCanonicalViewingTipsSceneId_ForEveryEventFamily(string eventFamily)
    {
        var sceneIds = SceneAssetsV3SceneContract.GetExpectedSceneIds("long");

        Assert.Contains("008-viewing-tips", sceneIds);
        Assert.DoesNotContain("008-viewing-tip", sceneIds);
        Assert.Equal(9, sceneIds.Count);
        Assert.Equal("008-viewing-tips", sceneIds[7]);
        Assert.False(string.IsNullOrWhiteSpace(eventFamily));
    }

    [Fact]
    public void ShortFormat_StillUsesSingularViewingTipSceneId()
    {
        var sceneIds = SceneAssetsV3SceneContract.GetExpectedSceneIds("short");

        Assert.Contains("004-viewing-tip", sceneIds);
        Assert.DoesNotContain("004-viewing-tips", sceneIds);
        Assert.Equal(5, sceneIds.Count);
        Assert.Equal("004-viewing-tip", sceneIds[3]);
    }
}
