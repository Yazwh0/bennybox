using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// See ChannelImportResult for why NotModified/ETag/LastModified exist. Programmes is null exactly
// when NotModified is true - there is nothing new to stream.
public record EpgFetchResult(bool NotModified, string? ETag, string? LastModified, IAsyncEnumerable<EpgProgramme>? Programmes);

public interface IEpgSource
{
    EpgSourceType SourceType { get; }

    Task<EpgFetchResult> GetProgrammesAsync(ProfileSource profile, CancellationToken cancellationToken = default);
}
