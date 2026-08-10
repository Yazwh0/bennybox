namespace BitMagic.BennyBox.Data.Sqlite;

internal static class SqliteSchema
{
    public const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS Profiles (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            SourceType INTEGER NOT NULL,
            M3uUrl TEXT,
            XtreamServerUrl TEXT,
            XtreamUsername TEXT,
            XtreamPasswordEncrypted TEXT,
            EpgSourceType INTEGER NOT NULL,
            EpgUrl TEXT,
            LastRefreshedUtc TEXT,
            SortOrder INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Categories (
            Id TEXT NOT NULL,
            ProfileId TEXT NOT NULL,
            Name TEXT NOT NULL,
            SortOrder INTEGER NOT NULL,
            PRIMARY KEY (ProfileId, Id)
        );

        CREATE TABLE IF NOT EXISTS Channels (
            Id TEXT PRIMARY KEY,
            ProfileId TEXT NOT NULL,
            SourceChannelId TEXT NOT NULL,
            CategoryId TEXT,
            Name TEXT NOT NULL,
            LogoUrl TEXT,
            StreamUrl TEXT NOT NULL,
            TvgId TEXT,
            Number INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Channels_ProfileId ON Channels(ProfileId);

        CREATE TABLE IF NOT EXISTS EpgProgramme (
            Id TEXT PRIMARY KEY,
            ProfileId TEXT NOT NULL,
            ChannelTvgId TEXT NOT NULL,
            Title TEXT NOT NULL,
            Description TEXT,
            StartUtc TEXT NOT NULL,
            EndUtc TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_EpgProgramme_Channel ON EpgProgramme(ChannelTvgId, StartUtc);
        CREATE INDEX IF NOT EXISTS IX_EpgProgramme_ProfileId ON EpgProgramme(ProfileId);

        CREATE TABLE IF NOT EXISTS SeriesCategories (
            Id TEXT NOT NULL,
            ProfileId TEXT NOT NULL,
            Name TEXT NOT NULL,
            SortOrder INTEGER NOT NULL,
            PRIMARY KEY (ProfileId, Id)
        );

        CREATE TABLE IF NOT EXISTS Series (
            Id TEXT PRIMARY KEY,
            ProfileId TEXT NOT NULL,
            SourceSeriesId TEXT NOT NULL,
            CategoryId TEXT,
            Name TEXT NOT NULL,
            CoverUrl TEXT,
            Plot TEXT,
            Genre TEXT,
            ReleaseDate TEXT,
            Rating REAL
        );

        CREATE INDEX IF NOT EXISTS IX_Series_ProfileId ON Series(ProfileId);

        CREATE TABLE IF NOT EXISTS Favorites (
            ChannelId TEXT PRIMARY KEY,
            ProfileId TEXT NOT NULL,
            AddedUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS SeriesFavorites (
            SeriesId TEXT PRIMARY KEY,
            ProfileId TEXT NOT NULL,
            AddedUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS MovieCategories (
            Id TEXT NOT NULL,
            ProfileId TEXT NOT NULL,
            Name TEXT NOT NULL,
            SortOrder INTEGER NOT NULL,
            PRIMARY KEY (ProfileId, Id)
        );

        CREATE TABLE IF NOT EXISTS Movies (
            Id TEXT PRIMARY KEY,
            ProfileId TEXT NOT NULL,
            SourceMovieId TEXT NOT NULL,
            CategoryId TEXT,
            Name TEXT NOT NULL,
            CoverUrl TEXT,
            StreamUrl TEXT NOT NULL,
            Rating REAL
        );

        CREATE INDEX IF NOT EXISTS IX_Movies_ProfileId ON Movies(ProfileId);

        -- See IEpisodeCacheRepository - persists a LocalFolder/Sftp show's episode scan so opening it
        -- again (even after an app restart) doesn't repeat a full directory walk plus a fresh NFO read
        -- per episode. SeriesId is Series.SourceSeriesId (a normalized show title for folder sources,
        -- not a row id - see FolderMediaScanner.ScanSeriesAsync), not a foreign key to Series.Id.
        CREATE TABLE IF NOT EXISTS CachedEpisodes (
            ProfileId TEXT NOT NULL,
            SeriesId TEXT NOT NULL,
            SourceEpisodeId TEXT NOT NULL,
            Title TEXT NOT NULL,
            Season INTEGER NOT NULL,
            EpisodeNumber INTEGER NOT NULL,
            PlotSummary TEXT,
            StreamUrl TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_CachedEpisodes_Profile_Series ON CachedEpisodes(ProfileId, SeriesId);

        -- See IMetadataEnrichmentCacheRepository - persists TMDb lookup results (both real matches and
        -- confirmed misses) keyed by a normalized title, not by profile/item - the same title from two
        -- different libraries shares one lookup rather than paying for it twice. Not scoped to a
        -- profile at all, so it's never cleared by a profile refresh or deletion.
        CREATE TABLE IF NOT EXISTS MetadataEnrichmentCache (
            MediaType TEXT NOT NULL,
            LookupKey TEXT NOT NULL,
            Found INTEGER NOT NULL,
            Plot TEXT,
            Genre TEXT,
            ReleaseDate TEXT,
            PosterUrl TEXT,
            Rating REAL,
            PRIMARY KEY (MediaType, LookupKey)
        );

        CREATE TABLE IF NOT EXISTS MovieFavorites (
            MovieId TEXT PRIMARY KEY,
            ProfileId TEXT NOT NULL,
            AddedUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Settings (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS WatchProgress (
            ProfileId TEXT NOT NULL,
            ContentType INTEGER NOT NULL,
            ContentKey TEXT NOT NULL,
            Title TEXT NOT NULL,
            CoverUrl TEXT,
            StreamUrl TEXT NOT NULL,
            PositionSeconds REAL NOT NULL,
            DurationSeconds REAL NOT NULL,
            UpdatedUtc TEXT NOT NULL,
            PRIMARY KEY (ProfileId, ContentType, ContentKey)
        );

        CREATE INDEX IF NOT EXISTS IX_WatchProgress_UpdatedUtc ON WatchProgress(UpdatedUtc);

        CREATE TABLE IF NOT EXISTS Reminders (
            ProfileId TEXT NOT NULL,
            ChannelTvgId TEXT NOT NULL,
            StartUtc TEXT NOT NULL,
            EndUtc TEXT NOT NULL,
            ChannelName TEXT NOT NULL,
            ProgrammeTitle TEXT NOT NULL,
            PRIMARY KEY (ProfileId, ChannelTvgId, StartUtc)
        );

        CREATE INDEX IF NOT EXISTS IX_Reminders_StartUtc ON Reminders(StartUtc);

        CREATE TABLE IF NOT EXISTS WatchedItems (
            ProfileId TEXT NOT NULL,
            ContentType INTEGER NOT NULL,
            ContentKey TEXT NOT NULL,
            WatchedUtc TEXT NOT NULL,
            PRIMARY KEY (ProfileId, ContentType, ContentKey)
        );

        -- One-off/uncategorized media (sports broadcasts, TV specials) - see IClipRepository. A
        -- column-for-column copy of MovieCategories/Movies/MovieFavorites, kept in its own tables
        -- rather than sharing Movies' so Clips never mixes into the Movies list. Unlike Movies,
        -- Plot/Genre/ReleaseDate are part of the initial create (no migration needed) since this
        -- table is new - Clips never gets these from TMDb, only from an NFO sidecar if one exists.
        CREATE TABLE IF NOT EXISTS ClipCategories (
            Id TEXT NOT NULL,
            ProfileId TEXT NOT NULL,
            Name TEXT NOT NULL,
            SortOrder INTEGER NOT NULL,
            PRIMARY KEY (ProfileId, Id)
        );

        CREATE TABLE IF NOT EXISTS Clips (
            Id TEXT PRIMARY KEY,
            ProfileId TEXT NOT NULL,
            SourceClipId TEXT NOT NULL,
            CategoryId TEXT,
            Name TEXT NOT NULL,
            CoverUrl TEXT,
            StreamUrl TEXT NOT NULL,
            Rating REAL,
            Plot TEXT,
            Genre TEXT,
            ReleaseDate TEXT
        );

        CREATE INDEX IF NOT EXISTS IX_Clips_ProfileId ON Clips(ProfileId);

        CREATE TABLE IF NOT EXISTS ClipFavorites (
            ClipId TEXT PRIMARY KEY,
            ProfileId TEXT NOT NULL,
            AddedUtc TEXT NOT NULL
        );

        -- See IDownloadRepository/DownloadManager - tracks a download's lifecycle, keyed by the
        -- ORIGINAL item's identity (OriginalProfileId/ContentType/OriginalSourceId), not the eventual
        -- downloaded copy's (there isn't one yet while Status is Queued/Downloading/Failed).
        CREATE TABLE IF NOT EXISTS Downloads (
            Id TEXT PRIMARY KEY,
            OriginalProfileId TEXT NOT NULL,
            ContentType INTEGER NOT NULL,
            OriginalSourceId TEXT NOT NULL,
            Title TEXT NOT NULL,
            CoverUrl TEXT,
            Status INTEGER NOT NULL,
            BytesDownloaded INTEGER NOT NULL,
            TotalBytes INTEGER,
            LocalRelativePath TEXT,
            ErrorMessage TEXT,
            StartedUtc TEXT NOT NULL,
            CompletedUtc TEXT
        );

        CREATE INDEX IF NOT EXISTS IX_Downloads_Original ON Downloads(OriginalProfileId, ContentType, OriginalSourceId);
        """;
}
