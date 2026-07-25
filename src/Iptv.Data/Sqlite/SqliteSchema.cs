namespace Iptv.Data.Sqlite;

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

        CREATE TABLE IF NOT EXISTS Settings (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );
        """;
}
