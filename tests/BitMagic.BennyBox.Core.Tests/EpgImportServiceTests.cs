using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Core.Tests;

public class EpgImportServiceTests
{
    [Fact]
    public async Task ImportAsync_ReplacesStaleProgrammesWithFreshOnes()
    {
        // Mirrors the real-world bug: a profile whose previously-imported EPG window has expired
        // (the channel has rows in the DB, but none cover "now") gets replaced with a fresh window
        // that does. This is what the Guide page's Refresh button now triggers - see GuideViewModel.
        var profile = new ProfileSource { Name = "Test", EpgSourceType = EpgSourceType.XtreamEmbedded };
        var repository = new FakeEpgRepository();
        var freshPogramme = new EpgProgramme
        {
            ProfileId = profile.Id,
            ChannelTvgId = "BBCOne.uk",
            Title = "Fresh Programme",
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddHours(1)
        };
        var source = new FakeEpgSource(EpgSourceType.XtreamEmbedded,
            new EpgFetchResult(NotModified: false, ETag: null, LastModified: null, Programmes: AsAsync(freshPogramme)));

        var service = new EpgImportService([source], repository, new FakeProfileRepository());

        await service.ImportAsync(profile);

        Assert.Equal(profile.Id, repository.LastReplacedProfileId);
        Assert.Same(freshPogramme, Assert.Single(repository.LastReplacedProgrammes!));
    }

    [Fact]
    public async Task ImportAsync_NotModified_SkipsReplacingStoredProgrammes()
    {
        var profile = new ProfileSource { Name = "Test", EpgSourceType = EpgSourceType.XtreamEmbedded };
        var repository = new FakeEpgRepository();
        var source = new FakeEpgSource(EpgSourceType.XtreamEmbedded,
            new EpgFetchResult(NotModified: true, ETag: null, LastModified: null, Programmes: null));

        var service = new EpgImportService([source], repository, new FakeProfileRepository());

        await service.ImportAsync(profile);

        Assert.Null(repository.LastReplacedProfileId);
    }

    [Fact]
    public async Task ImportAsync_NoMatchingSource_Throws()
    {
        var profile = new ProfileSource { Name = "Test", EpgSourceType = EpgSourceType.XtreamEmbedded };
        var service = new EpgImportService([], new FakeEpgRepository(), new FakeProfileRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(profile));
    }

    private static async IAsyncEnumerable<EpgProgramme> AsAsync(params EpgProgramme[] programmes)
    {
        foreach (var programme in programmes)
        {
            yield return programme;
        }

        await Task.CompletedTask;
    }

    private sealed class FakeEpgSource(EpgSourceType sourceType, EpgFetchResult result) : IEpgSource
    {
        public EpgSourceType SourceType { get; } = sourceType;

        public Task<EpgFetchResult> GetProgrammesAsync(ProfileSource profile, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class FakeEpgRepository : IEpgRepository
    {
        public Guid? LastReplacedProfileId { get; private set; }
        public List<EpgProgramme>? LastReplacedProgrammes { get; private set; }

        public async Task ReplaceProgrammesAsync(Guid profileId, IAsyncEnumerable<EpgProgramme> programmes, CancellationToken cancellationToken = default)
        {
            LastReplacedProfileId = profileId;
            LastReplacedProgrammes = await ToListAsync(programmes);
        }

        public Task<IReadOnlyDictionary<string, EpgNowNext>> GetNowNextAsync(Guid profileId, DateTime nowUtc, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, EpgNowNext>>(new Dictionary<string, EpgNowNext>());

        public Task<IReadOnlyList<EpgProgramme>> GetProgrammesInRangeAsync(Guid profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EpgProgramme>>([]);

        private static async Task<List<EpgProgramme>> ToListAsync(IAsyncEnumerable<EpgProgramme> source)
        {
            var list = new List<EpgProgramme>();
            await foreach (var item in source)
            {
                list.Add(item);
            }

            return list;
        }
    }

    private sealed class FakeProfileRepository : IProfileRepository
    {
        public Task<IReadOnlyList<ProfileSource>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProfileSource>>([]);

        public Task<ProfileSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<ProfileSource?>(null);

        public Task AddAsync(ProfileSource profile, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(ProfileSource profile, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
