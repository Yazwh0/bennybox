namespace BitMagic.BennyBox.ViewModels;

public class MovieCategoryGroupViewModel
{
    public string Name { get; }
    public IReadOnlyList<MovieListItemViewModel> Movies { get; }

    public MovieCategoryGroupViewModel(string name, IReadOnlyList<MovieListItemViewModel> movies)
    {
        Name = name;
        Movies = movies;
    }
}
