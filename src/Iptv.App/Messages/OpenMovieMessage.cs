using Iptv.Core.Models;

namespace Iptv.App.Messages;

// Sent when a movie is selected from a page other than Movies itself (currently: Favorites) - lets
// MainWindowViewModel both switch to the Movies page and open that movie's detail view, without
// FavoritesViewModel needing a direct reference to MoviesViewModel or MainWindowViewModel.
public sealed class OpenMovieMessage
{
    public Movie Movie { get; }

    public OpenMovieMessage(Movie movie)
    {
        Movie = movie;
    }
}
