using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// Series categories are stored separately from live-channel Categories (not sharing that table) -
// Xtream category IDs are only unique within one content type, so a live category "6" and a series
// category "6" on the same server are unrelated and would otherwise collide on the same primary key.
public interface ISeriesRepository
{
    Task ReplaceSeriesAsync(Guid profileId, IReadOnlyList<Category> categories, IReadOnlyList<Series> seriesList, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Series>> GetSeriesAsync(Guid profileId, CancellationToken cancellationToken = default);
}
