namespace Astronomy.MediaFactory.Contracts;

public sealed class CelestialAssetsOptions
{
    public const string SectionName = "CelestialAssets";

    public bool Enabled { get; set; } = true;
    public string RootPath { get; set; } = "assets/celestial";
    public bool PreferLocalCache { get; set; } = true;
    public bool DownloadIfMissing { get; set; } = false;
    public int MaxImagesPerObject { get; set; } = 5;
    public bool RefreshExistingAssets { get; set; }
    public List<string> AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png"];
    public List<string> RequiredObjects { get; set; } =
    [
        "jupiter",
        "saturn",
        "mars",
        "venus",
        "moon",
        "mercury",
        "uranus",
        "neptune",
        "meteor-shower",
        "lunar-eclipse",
        "solar-eclipse",
        "orion-nebula",
        "andromeda-galaxy",
        "ring-nebula",
        "milky-way",
        "earth",
        "sun"
    ];
}

public sealed class NasaImagesOptions
{
    public const string SectionName = "NasaImages";

    public string SearchBaseUrl { get; set; } = "https://images-api.nasa.gov";
    public string SearchEndpoint { get; set; } = "/search";
    public string AssetEndpoint { get; set; } = "/asset/{nasaId}";
}
