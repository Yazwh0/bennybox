using CommunityToolkit.Mvvm.ComponentModel;
using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

public sealed record JellyfinLibraryTargetOption(JellyfinLibraryTarget Target, string Label)
{
    public override string ToString() => Label;
}

// One library in the Jellyfin profile dialog's "Libraries" list, with which section it imports into.
public partial class JellyfinLibraryRowViewModel : ObservableObject
{
    public static IReadOnlyList<JellyfinLibraryTargetOption> Options { get; } =
    [
        new(JellyfinLibraryTarget.Movies, "Movies"),
        new(JellyfinLibraryTarget.Series, "TV Shows"),
        new(JellyfinLibraryTarget.Clips, "Clips"),
        new(JellyfinLibraryTarget.Skip, "Don't import")
    ];

    public JellyfinLibraryRowViewModel(string id, string name, JellyfinLibraryTarget target)
    {
        Id = id;
        Name = name;
        _selectedOption = Options.First(o => o.Target == target);
    }

    public string Id { get; }

    public string Name { get; }

    // Instance passthrough so a row's DataTemplate can bind ItemsSource without an x:Static.
    public IReadOnlyList<JellyfinLibraryTargetOption> TargetOptions => Options;

    [ObservableProperty]
    private JellyfinLibraryTargetOption _selectedOption;

    public JellyfinLibraryMapping ToMapping() => new(Id, Name, SelectedOption.Target);
}
