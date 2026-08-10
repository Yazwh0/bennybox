using System.Runtime.CompilerServices;
using BitMagic.BennyBox.Core.Services;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace BitMagic.BennyBox.Sources.Folder;

public class SftpFileSystem : IMediaFileSystem
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly string _rootPath;

    // Connected lazily on first use (see GetConnectedClientAsync) and reused for every subsequent
    // call on this instance - listing, then however many NFO/poster reads follow - rather than a
    // fresh login per operation. Only closed when this instance itself is disposed (see DisposeAsync).
    private SftpClient? _client;

    public SftpFileSystem(string host, int port, string username, string password, string rootPath)
    {
        _host = host;
        _port = port;
        _username = username;
        _password = password;
        _rootPath = rootPath.TrimEnd('/');
    }

    private async Task<SftpClient> GetConnectedClientAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            var client = new SftpClient(_host, _port, _username, _password);
            await client.ConnectAsync(cancellationToken);
            _client = client;
        }

        return _client;
    }

    public async IAsyncEnumerable<MediaFileEntry> EnumerateFilesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = await GetConnectedClientAsync(cancellationToken);

        await foreach (var entry in WalkAsync(client, _rootPath, cancellationToken))
        {
            yield return entry;
        }
    }

    // SSH_FX_PERMISSION_DENIED is what a compliant SFTP server returns both for "you can't see this"
    // and for "this path doesn't exist" - servers deliberately don't distinguish the two, so a client
    // can't probe for paths it has no access to. SSH.NET surfaces both as SftpPermissionDeniedException,
    // so the raw "Permission denied" message is misleading whenever the real problem is just a wrong
    // path - rephrase it to cover both rather than asserting it's a real access problem.
    private static InvalidOperationException WrapPermissionDenied(string path, string host, Exception inner) =>
        new($"Couldn't access \"{path}\" on {host} - the server says permission denied, which usually " +
            "means either the path doesn't exist or this account can't read it. Double-check the remote path.", inner);

    private async IAsyncEnumerable<MediaFileEntry> WalkAsync(
        SftpClient client, string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // yield return can't appear inside a try that has a catch, so MoveNextAsync (where the SFTP
        // protocol work, and thus the exception, actually happens) is called in its own try/catch and
        // the yield happens afterward, outside it.
        await using var enumerator = client.ListDirectoryAsync(path, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            ISftpFile item;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }
                item = enumerator.Current;
            }
            catch (SftpPermissionDeniedException ex)
            {
                throw WrapPermissionDenied(path, client.ConnectionInfo.Host, ex);
            }

            if (item.Name is "." or "..")
            {
                continue;
            }

            if (item.IsDirectory)
            {
                await foreach (var nested in WalkAsync(client, item.FullName, cancellationToken))
                {
                    yield return nested;
                }
            }
            else if (item.IsRegularFile)
            {
                // +1 strips the separating slash between root and the rest of the path.
                yield return new MediaFileEntry(item.FullName[(_rootPath.Length + 1)..], item.Length);
            }
        }
    }

    // Used for NFO/poster reads (small files, one per movie/show unit, not per video) - reuses the
    // same connection EnumerateFilesAsync already opened for this instance rather than logging in
    // again (SFTP happily multiplexes a read request over the same connection a listing is/was using).
    // Still buffers into memory rather than returning a live SftpFileStream, so the caller doesn't
    // need to worry about the read outliving the connection.
    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var client = await GetConnectedClientAsync(cancellationToken);

        var fullPath = $"{_rootPath}/{relativePath}";
        var bytes = await Task.Run(() => client.ReadAllBytes(fullPath), cancellationToken);
        return new MemoryStream(bytes);
    }

    // Unlike OpenReadAsync, this genuinely streams - SSH.NET's DownloadFile writes directly to the
    // output stream as data arrives, never buffering the whole file in memory, which OpenReadAsync's
    // ReadAllBytes does (fine for a small NFO, not for a multi-GB video - see DownloadManager). Reuses
    // the same connection every other call on this instance shares (see GetConnectedClientAsync).
    //
    // NOT byte-level resumable, unlike LocalFileSystem/DownloadManager's HTTP path - confirmed by
    // testing directly against a real server: SSH.NET's read-side SftpFileStream (both OpenRead() and
    // Open(path, FileMode.Open, FileAccess.Read)) reports CanSeek = false and throws
    // NotSupportedException on Seek/Position, and Open(..., FileAccess.ReadWrite) requires write
    // permission on the remote file, which a typical read-only media account doesn't have (confirmed
    // via SftpPermissionDeniedException). There's no public SSH.NET API for an offset-based read. A
    // leftover partial file from an earlier interrupted attempt is therefore just discarded and
    // re-fetched from the start - still correct (DownloadManager's retry/row-reuse logic works
    // regardless of whether the underlying transfer can resume), just not bandwidth-saving for SFTP
    // specifically.
    public async Task DownloadToFileAsync(string relativePath, string destinationPath, IProgress<(long BytesTransferred, long? TotalBytes)>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = await GetConnectedClientAsync(cancellationToken);
        var fullPath = $"{_rootPath}/{relativePath}";

        long? totalBytes = null;
        try
        {
            var attributes = await Task.Run(() => client.GetAttributes(fullPath), cancellationToken);
            totalBytes = attributes.Size;
        }
        catch
        {
            // Non-fatal - progress just won't have a known total to report against.
        }

        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        await Task.Run(() =>
        {
            using var output = File.Create(destinationPath);
            client.DownloadFile(fullPath, output, bytesTransferred =>
            {
                // DownloadFile's callback is SSH.NET's only hook into an in-progress transfer - there's
                // no separate cancellation parameter, so an OperationCanceledException thrown from here
                // is how a cancellation request actually stops the transfer.
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(((long)bytesTransferred, totalBytes));
            });
        }, cancellationToken);
    }

    // libVLC's bundled sftp access module (confirmed present - see the plan discussion) accepts
    // credentials directly in the MRL. Each path segment is escaped separately so slashes stay
    // literal while spaces/special characters in filenames don't break the URL.
    public string BuildStreamUrl(string relativePath)
    {
        var fullPath = $"{_rootPath}/{relativePath}".Replace('\\', '/');
        var escapedPath = string.Join('/', fullPath.Split('/').Select(Uri.EscapeDataString));
        return $"sftp://{Uri.EscapeDataString(_username)}:{Uri.EscapeDataString(_password)}@{_host}:{_port}{escapedPath}";
    }

    // Closes the one connection this instance may have opened (see GetConnectedClientAsync) - a no-op
    // if nothing was ever actually connected (e.g. a scan that found zero files to list).
    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _client = null;
        return ValueTask.CompletedTask;
    }
}
