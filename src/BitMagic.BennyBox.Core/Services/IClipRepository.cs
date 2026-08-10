using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// Mirrors IMovieRepository, backed by its own Clips/ClipCategories tables rather than sharing
// Movies' - see IClipSource for why Clips needs its own repository/source pair rather than reusing
// IMovieRepository/IMovieSource despite the identical Movie payload shape.
public interface IClipRepository
{
    Task ReplaceClipsAsync(Guid profileId, IReadOnlyList<Category> categories, IReadOnlyList<Movie> clips, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Movie>> GetClipsAsync(Guid profileId, CancellationToken cancellationToken = default);
}
