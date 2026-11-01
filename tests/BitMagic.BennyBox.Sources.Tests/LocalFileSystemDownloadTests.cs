using BitMagic.BennyBox.Sources.Folder;

namespace BitMagic.BennyBox.Sources.Tests;

public class LocalFileSystemDownloadTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bennybox-download-tests").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private byte[] CreateSource(int length)
    {
        var bytes = new byte[length];
        new Random(1234).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(_root, "source.bin"), bytes);
        return bytes;
    }

    [Fact]
    public async Task Copies_whole_file_and_reports_final_progress_synchronously()
    {
        var source = CreateSource(3 * 1024 * 1024 + 17);
        var destination = Path.Combine(_root, "nested", "copy.bin");
        var reports = new List<(long BytesTransferred, long? TotalBytes)>();

        await new LocalFileSystem(_root).DownloadToFileAsync("source.bin", destination, new CapturingProgress(reports));

        Assert.Equal(source, File.ReadAllBytes(destination));
        Assert.Equal((source.Length, (long?)source.Length), reports[^1]);
        Assert.Equal(reports.OrderBy(r => r.BytesTransferred), reports);
    }

    [Fact]
    public async Task Resumes_from_existing_partial_file()
    {
        var source = CreateSource(2 * 1024 * 1024 + 5);
        var destination = Path.Combine(_root, "copy.bin");
        const int partialLength = 1024 * 1024 + 3;
        File.WriteAllBytes(destination, source[..partialLength]);
        var reports = new List<(long BytesTransferred, long? TotalBytes)>();

        await new LocalFileSystem(_root).DownloadToFileAsync("source.bin", destination, new CapturingProgress(reports));

        Assert.Equal(source, File.ReadAllBytes(destination));
        Assert.True(reports[0].BytesTransferred > partialLength);
    }

    private sealed class CapturingProgress(List<(long, long?)> reports) : IProgress<(long BytesTransferred, long? TotalBytes)>
    {
        public void Report((long BytesTransferred, long? TotalBytes) value) => reports.Add(value);
    }
}
