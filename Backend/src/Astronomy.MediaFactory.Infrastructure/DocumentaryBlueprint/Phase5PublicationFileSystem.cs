namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>The deliberately narrow I/O boundary for the Phase 5 publication transaction.</summary>
public interface IPhase5PublicationFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken token);
    Task<string> ReadAllTextAsync(string path, CancellationToken token);
    Task WriteAllTextAsync(string path, string contents, CancellationToken token);
    void CopyFile(string source, string destination, bool overwrite);
    void MoveFile(string source, string destination, bool overwrite = false);
    void MoveDirectory(string source, string destination);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
    string[] GetFiles(string path, string searchPattern);
    long GetFileLength(string path);
}

public sealed class Phase5PublicationFileSystem : IPhase5PublicationFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken token) => File.ReadAllBytesAsync(path, token);
    public Task<string> ReadAllTextAsync(string path, CancellationToken token) => File.ReadAllTextAsync(path, token);
    public Task WriteAllTextAsync(string path, string contents, CancellationToken token) => File.WriteAllTextAsync(path, contents, token);
    public void CopyFile(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);
    public void MoveFile(string source, string destination, bool overwrite = false) => File.Move(source, destination, overwrite);
    public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    public string[] GetFiles(string path, string searchPattern) => Directory.GetFiles(path, searchPattern);
    public long GetFileLength(string path) => new FileInfo(path).Length;
}
