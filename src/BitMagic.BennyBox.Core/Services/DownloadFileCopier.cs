using System.Buffers;

namespace BitMagic.BennyBox.Core.Services;

// The copy loop behind every download that has a source Stream (HTTP and Local Folder) - see
// DownloadManager.DownloadHttpAsync/LocalFileSystem.DownloadToFileAsync.
public static class DownloadFileCopier
{
    // HTTP reads return whatever has arrived, often only a few KB, so the destination's own buffer is
    // what stops every one of those becoming a separate disk write. A partial file is still safe to
    // resume if the process dies with this unflushed, since resume goes by the file's actual length.
    public const int BufferSize = 1024 * 1024;

    // Without this a connection that died silently, typically across a sleep/resume, never completes
    // its read and the download sits there forever. Throws TimeoutException, which DownloadManager
    // answers by reconnecting and resuming.
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    public static FileStream OpenDestination(string destinationPath, bool append, FileOptions options = FileOptions.Asynchronous) =>
        new(destinationPath, new FileStreamOptions
        {
            Mode = append ? FileMode.Append : FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            BufferSize = BufferSize,
            Options = options
        });

    public static async Task CopyAsync(
        Stream source, string destinationPath, bool append, long startingBytes, long? totalBytes,
        IProgress<(long BytesTransferred, long? TotalBytes)>? progress, CancellationToken cancellationToken)
    {
        await using var destination = OpenDestination(destinationPath, append);

        using var stallTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var bytesTransferred = startingBytes;
            int read;
            while (true)
            {
                stallTimeout.CancelAfter(StallTimeout);
                read = await source.ReadAsync(buffer, stallTimeout.Token);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesTransferred += read;
                progress?.Report((bytesTransferred, totalBytes));
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No data received for {StallTimeout.TotalSeconds:0} seconds.", ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
