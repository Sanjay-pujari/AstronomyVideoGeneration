namespace Astronomy.MediaFactory.Contracts;

public sealed class CelestialAssetPackOptions
{
    public const string SectionName = "CelestialAssetPack";

    public bool Enabled { get; init; } = true;
    public string SourceSheetPath { get; init; } = "assets/celestial/source/celestial-object-sheet.png";
    public string OutputRootPath { get; init; } = "assets/celestial";
    public bool OverwriteExisting { get; init; }
    public string SheetMapPath { get; init; } = "assets/celestial/source/celestial-object-sheet-map.json";
}
