using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// Movie categories are stored separately from live-channel and series Categories (not sharing either
// table) - Xtream category IDs are only unique within one content type, so a live/series/VOD category
// "6" on the same server are unrelated and would otherwise collide on the same primary key.
public interface IMovieRepository
{
    Task ReplaceMoviesAsync(Guid profileId, IReadOnlyList<Category> categories, IReadOnlyList<Movie> movies, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Movie>> GetMoviesAsync(Guid profileId, CancellationToken cancellationToken = default);
}
