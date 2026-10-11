namespace BitMagic.BennyBox.Sources.Jellyfin;

// Only the fields this app actually reads - Jellyfin's DTOs are huge. Jellyfin's JSON is PascalCase,
// matched case-insensitively (see JellyfinClient.JsonOptions).

public class JellyfinAuthResponse
{
    public JellyfinUser? User { get; set; }
    public string? AccessToken { get; set; }
}

public class JellyfinUser
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public JellyfinUserPolicy? Policy { get; set; }
}

public class JellyfinUserPolicy
{
    public bool IsAdministrator { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDisabled { get; set; }
}

public class JellyfinPublicSystemInfo
{
    public string? ServerName { get; set; }
    public string? Version { get; set; }
}

public sealed record JellyfinAuthResult(string UserId, string AccessToken, string? UserName);

public class JellyfinItemsResponse
{
    public List<JellyfinItem> Items { get; set; } = [];
    public int TotalRecordCount { get; set; }
}

public class JellyfinItem
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? CollectionType { get; set; }
    public string? Overview { get; set; }
    public List<string>? Genres { get; set; }
    public DateTime? PremiereDate { get; set; }
    public int? ProductionYear { get; set; }
    public double? CommunityRating { get; set; }
    public long? RunTimeTicks { get; set; }
    public string? Container { get; set; }
    public Dictionary<string, string>? ImageTags { get; set; }

    // Episodes
    public int? IndexNumber { get; set; }
    public int? ParentIndexNumber { get; set; }

    // Live TV channels
    public string? Number { get; set; }
    public string? ChannelType { get; set; }

    // Live TV programmes
    public string? ChannelId { get; set; }
    public string? EpisodeTitle { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public string? PrimaryImageTag => ImageTags is not null && ImageTags.TryGetValue("Primary", out var tag) ? tag : null;
}

public class JellyfinAuthException : Exception
{
    public JellyfinAuthException(string message) : base(message)
    {
    }
}
