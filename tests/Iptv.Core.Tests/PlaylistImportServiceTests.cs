using Iptv.Core.Models;
using Iptv.Core.Services;

namespace Iptv.Core.Tests;

public class PlaylistImportServiceTests
{
    [Fact]
    public async Task ImportAsync_NoMatchingSource_Throws()
    {
        var service = new PlaylistImportService(
            sources: [],
            channelRepository: new FakeChannelRepository(),
            profileRepository: new FakeProfileRepository());

        var profile = new ProfileSource { Name = "Test", SourceType = ProfileSourceType.M3u };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(profile));
    }

    [Fact]
    public async Task ImportAsync_PersistsResultAndUpdatesProfile()
    {
        var profile = new ProfileSource { Name = "Test", SourceType = ProfileSourceType.M3u };
        var category = new Category { Id = "cat", ProfileId = profile.Id, Name = "Cat" };
        var channel = new Channel { ProfileId = profile.Id, SourceChannelId = "1", Name = "Ch1", StreamUrl = "http://x" };

        var channelRepository = new FakeChannelRepository();
        var profileRepository = new FakeProfileRepository();
        var source = new FakeChannelSource(ProfileSourceType.M3u, new ChannelImportResult([category], [channel]));

        var service = new PlaylistImportService([source], channelRepository, profileRepository);

        var result = await service.ImportAsync(profile);

        Assert.Same(channel, Assert.Single(result.Channels));
        Assert.Equal(profile.Id, channelRepository.LastReplacedProfileId);
        Assert.NotNull(profileRepository.LastUpdatedProfile?.LastRefreshedUtc);
    }

    private sealed class FakeChannelSource(ProfileSourceType sourceType, ChannelImportResult result) : IChannelSource
    {
        public ProfileSourceType SourceType { get; } = sourceType;

        public Task<ChannelImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
            => Task.FromResult(result);

        public string? BuildTimeshiftUrl(ProfileSource profile, Channel channel, DateTime startUtc, TimeSpan duration) => null;
    }

    private sealed class FakeChannelRepository : IChannelRepository
    {
        public Guid? LastReplacedProfileId { get; private set; }

        public Task ReplaceChannelsAsync(Guid profileId, IReadOnlyList<Category> categories, IReadOnlyList<Channel> channels, CancellationToken cancellationToken = default)
        {
            LastReplacedProfileId = profileId;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid profileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Category>>([]);

        public Task<IReadOnlyList<Channel>> GetChannelsAsync(Guid profileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Channel>>([]);
    }

    private sealed class FakeProfileRepository : IProfileRepository
    {
        public ProfileSource? LastUpdatedProfile { get; private set; }

        public Task<IReadOnlyList<ProfileSource>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProfileSource>>([]);

        public Task<ProfileSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<ProfileSource?>(null);

        public Task AddAsync(ProfileSource profile, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(ProfileSource profile, CancellationToken cancellationToken = default)
        {
            LastUpdatedProfile = profile;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
