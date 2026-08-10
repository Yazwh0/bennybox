using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// Builds the right IMediaFileSystem for a profile's SourceType (LocalFolder or Sftp) and MediaKind -
// kept separate from FolderMovieSource/FolderSeriesSource so both can share one factory instance
// instead of each re-implementing the SourceType/path-selection switch. Returns null when the
// profile hasn't configured a root for that particular kind (e.g. an SFTP profile with only a movies
// path set has nothing to return for MediaKind.Series) - that's a normal "nothing to import for this
// kind", not an error.
public interface IMediaFileSystemFactory
{
    IMediaFileSystem? Create(ProfileSource profile, MediaKind kind);
}
