using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Folder;

public class LocalFileSystem : IMediaFileSystem
{
    private readonly string _rootPath;

    public LocalFileSystem(string rootPath)
    {
        _rootPath = rootPath;
    }

    public async IAsyncEnumerable<MediaFileEntry> EnumerateFilesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootPath))
        {
            yield break;
        }

        // Directory.EnumerateFiles itself isn't async, but a large library (thousands of files, maybe
        // on slower storage) walking the tree is real I/O work - keep it off whatever thread is
        // driving the refresh, same rationale as the SQLite repositories' Task.Run usage.
        var files = await Task.Run(
            () => Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories).ToList(),
            cancellationToken);

        foreach (var fullPath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(_rootPath, fullPath);
            long length;
            try
            {
                length = new FileInfo(fullPath).Length;
            }
            catch (IOException)
            {
                // File vanished/locked between enumeration and stat - skip it rather than fail the
                // whole scan over one file.
                continue;
            }

            yield return new MediaFileEntry(relativePath, length);
        }
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(File.OpenRead(Path.Combine(_rootPath, relativePath)));

    public string BuildStreamUrl(string relativePath) =>
        new Uri(Path.Combine(_rootPath, relativePath)).AbsoluteUri;

    // Plain file I/O has no connection to hold open, unlike SftpFileSystem.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
