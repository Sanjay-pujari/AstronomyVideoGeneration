namespace Astronomy.MediaFactory.Rendering;

public interface IFileSystem
{
    void CreateDirectory(string path);
    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken);
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(path, contents, cancellationToken);

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        => File.WriteAllBytesAsync(path, bytes, cancellationToken);
}
