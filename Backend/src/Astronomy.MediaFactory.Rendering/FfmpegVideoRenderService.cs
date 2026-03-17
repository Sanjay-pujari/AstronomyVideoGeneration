using System.Diagnostics;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.Rendering;
public sealed class FfmpegVideoRenderService : IVideoRenderService
{
    private readonly RenderingOptions _options;
    public FfmpegVideoRenderService(IOptions<RenderingOptions> options) { _options = options.Value; }
    public async Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifest.OutputPath)!);
        var args = $"-y -f lavfi -i color=c=black:s={_options.VideoWidth}x{_options.VideoHeight}:d=5 -c:v libx264 -pix_fmt yuv420p \"{manifest.OutputPath}\"";
        try
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = _options.FfmpegPath, Arguments = args, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            if (process is null) throw new InvalidOperationException("FFmpeg process did not start.");
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 && !File.Exists(manifest.OutputPath)) await File.WriteAllBytesAsync(manifest.OutputPath, Array.Empty<byte>(), cancellationToken);
        }
        catch { await File.WriteAllBytesAsync(manifest.OutputPath, Array.Empty<byte>(), cancellationToken); }
        return manifest.OutputPath;
    }
}
