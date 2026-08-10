using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Messages;

// Mirrors OpenMovieMessage - sent when a clip is selected from a page other than Clips itself
// (Favorites, Search).
public sealed class OpenClipMessage
{
    public Movie Clip { get; }
    public string SourceName { get; }

    public OpenClipMessage(Movie clip, string sourceName)
    {
        Clip = clip;
        SourceName = sourceName;
    }
}
