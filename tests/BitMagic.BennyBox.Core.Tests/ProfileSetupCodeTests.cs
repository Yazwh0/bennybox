using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Core.Tests;

public class ProfileSetupCodeTests
{
    // Each device has its own protector, so a code made on one must decrypt and re-encrypt cleanly
    // under a different one - two different fakes stand in for desktop (DPAPI) and phone (Keystore).
    private sealed class PrefixProtector(string prefix) : ICredentialProtector
    {
        public string? Protect(string? plaintext) => string.IsNullOrEmpty(plaintext) ? null : prefix + plaintext;
        public string? Unprotect(string? encrypted) => encrypted is null ? null : encrypted[prefix.Length..];
    }

    private static readonly PrefixProtector Desktop = new("desk:");
    private static readonly PrefixProtector Phone = new("phone:");

    [Fact]
    public void RoundTrip_CarriesCredentialsAndMintsNewIds()
    {
        var xtream = new ProfileSource
        {
            Name = "Provider",
            SourceType = ProfileSourceType.XtreamCodes,
            XtreamServerUrl = "http://example.com:8080",
            XtreamUsername = "user",
            XtreamPasswordEncrypted = Desktop.Protect("s3cret"),
            EpgSourceType = EpgSourceType.XtreamEmbedded,
            LastRefreshedUtc = DateTime.UtcNow
        };
        var jellyfin = new ProfileSource
        {
            Name = "Home",
            SourceType = ProfileSourceType.Jellyfin,
            JellyfinServerUrl = "http://nas:8096",
            JellyfinUsername = "me",
            JellyfinPasswordEncrypted = Desktop.Protect("pw"),
            JellyfinUserId = "abc",
            JellyfinAccessTokenEncrypted = Desktop.Protect("token"),
            EpgSourceType = EpgSourceType.JellyfinEmbedded
        };
        var local = new ProfileSource { Name = "Local", SourceType = ProfileSourceType.LocalFolder, LocalMoviesPath = @"C:\Movies" };

        var code = ProfileSetupCode.Encode([xtream, jellyfin, local], Desktop, "tmdb-key");

        Assert.StartsWith(ProfileSetupCode.Prefix, code);
        Assert.True(ProfileSetupCode.TryDecode(code, Phone, out var result, out var error), error);
        Assert.Equal("tmdb-key", result.TmdbApiKey);
        Assert.Equal(2, result.Profiles.Count);

        var x = result.Profiles[0];
        Assert.NotEqual(xtream.Id, x.Id);
        Assert.Equal("http://example.com:8080", x.XtreamServerUrl);
        Assert.Equal("phone:s3cret", x.XtreamPasswordEncrypted);
        Assert.Equal(EpgSourceType.XtreamEmbedded, x.EpgSourceType);
        Assert.Null(x.LastRefreshedUtc);

        var j = result.Profiles[1];
        Assert.Equal("phone:pw", j.JellyfinPasswordEncrypted);
        Assert.Null(j.JellyfinUserId);
        Assert.Null(j.JellyfinAccessTokenEncrypted);
        Assert.True(ProfileSetupCode.IsSameAccount(jellyfin, j));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://example.com")]
    [InlineData("BENNYBOX1:not-base64!!")]
    public void TryDecode_RejectsOtherText(string code)
    {
        Assert.False(ProfileSetupCode.TryDecode(code, Phone, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
