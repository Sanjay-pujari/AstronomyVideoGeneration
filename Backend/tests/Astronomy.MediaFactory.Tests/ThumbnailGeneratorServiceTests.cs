using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ThumbnailGeneratorServiceTests
{
    [Fact]
    public async Task Generates_Three_Thumbnails_And_Diagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), $"thumb-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var screenshots = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var p = Path.Combine(root, $"scene-{i}.png");
            using var img = new Image<Rgba32>(1920, 1080, i == 0 ? Color.Gray : Color.DarkBlue);
            await img.SaveAsPngAsync(p);
            screenshots.Add(p);
        }

        var context = new AstronomyContext
        {
            SceneObservationContexts =
            [
                new SceneObservationContext { SceneId = "s1", ObjectName = "Moon", AltitudeDegrees = 30, DirectionLabel = "West" },
                new SceneObservationContext { SceneId = "s2", ObjectName = "Jupiter", AltitudeDegrees = 45, DirectionLabel = "South" },
                new SceneObservationContext { SceneId = "s3", ObjectName = "Sky", ObjectType = "Overview", AltitudeDegrees = 10 }
            ]
        };

        var svc = new ThumbnailGeneratorService(Options.Create(new ThumbnailOptions()), NullLogger<ThumbnailGeneratorService>.Instance);
        var outputs = await svc.GenerateAsync(context, screenshots, root, "Narration context", CancellationToken.None);

        Assert.Equal(3, outputs.Count);
        Assert.All(outputs, x => Assert.True(File.Exists(x)));
        Assert.True(File.Exists(Path.Combine(root, "thumbnails", "thumbnail-selection.json")));

        using var generated = await Image.LoadAsync<Rgba32>(outputs.First());
        Assert.Equal(1280, generated.Width);
        Assert.Equal(720, generated.Height);
        var nonBlack = 0;
        generated.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y += 40)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x += 40)
                {
                    if (row[x].R > 0 || row[x].G > 0 || row[x].B > 0) nonBlack++;
                }
            }
        });

        Assert.True(nonBlack > 0);
    }
}
