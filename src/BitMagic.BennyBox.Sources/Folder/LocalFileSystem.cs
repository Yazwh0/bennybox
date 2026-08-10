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

    // A buffered copy loop rather than File.Copy - File.Copy has no progress callback, and
    // DownloadManager wants the same (bytes transferred, total) progress shape it gets from the
    // Sftp/Xtream download paths.
    //
    // Resumable: if destinationPath already has bytes on disk (a previous attempt that got
    // interrupted - see DownloadManager, which deliberately leaves a genuinely-failed download's
    // partial file in place rather than deleting it), this seeks the source to that offset and
    // appends rather than starting over - see SftpFileSystem's equivalent for the same rationale.
    public async Task DownloadToFileAsync(string relativePath, string destinationPath, IProgress<(long BytesTransferred, long? TotalBytes)>? progress = null, CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.Combine(_rootPath, relativePath);
        var totalBytes = new FileInfo(sourcePath).Length;

        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var startingBytes = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;
        if (startingBytes >= totalBytes)
        {
            // Nothing left to fetch, or a stale/corrupt leftover from an unrelated attempt - restart
            // clean rather than risk seeking past the end of the source file.
            File.Delete(destinationPath);
            startingBytes = 0;
        }

        await using var source = File.OpenRead(sourcePath);
        if (startingBytes > 0)
        {
            source.Seek(startingBytes, SeekOrigin.Begin);
        }

        await using var destination = new FileStream(destinationPath, startingBytes > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write);

        var buffer = new byte[81920];
        var bytesTransferred = startingBytes;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            bytesTransferred += read;
            progress?.Report((bytesTransferred, totalBytes));
        }
    }

    // Plain file I/O has no connection to hold open, unlike SftpFileSystem.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
