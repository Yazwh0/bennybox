using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Iptv.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Iptv.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async void OnAddProfileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var addProfileViewModel = App.Services!.GetRequiredService<AddProfileViewModel>();
        var dialog = new FAContentDialog
        {
            Title = "Add Profile",
            Content = new AddProfileView { DataContext = addProfileViewModel },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary
        };

        while (true)
        {
            var result = await dialog.ShowAsync();
            if (result != FAContentDialogResult.Primary)
            {
                return;
            }

            if (addProfileViewModel.TryBuildProfile(out var profile) && profile is not null)
            {
                await viewModel.AddProfileAsync(profile);
                return;
            }
            // Validation failed - the error is shown in the dialog content; loop to let the user fix it.
        }
    }
}
