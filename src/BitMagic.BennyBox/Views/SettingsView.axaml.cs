using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using BitMagic.BennyBox.ViewModels;
using BitMagic.BennyBox.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BitMagic.BennyBox.Views;

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

        // PrimaryButtonClick + Cancel, not a ShowAsync-in-a-loop retry - closing and immediately
        // re-showing the same FAContentDialog instance on validation failure left its TextBoxes
        // displaying correctly but no longer accepting input (e.g. the Name field became stuck after
        // one failed Add attempt). Setting Cancel=true here keeps the dialog genuinely open instead
        // of closing and reopening it, which sidesteps that entirely - ShowAsync is now called
        // exactly once per dialog instance.
        ProfileSource? builtProfile = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (addProfileViewModel.TryBuildProfile(out var profile) && profile is not null)
            {
                builtProfile = profile;
            }
            else
            {
                args.Cancel = true;
            }
        };

        var result = await dialog.ShowAsync();
        if (result == FAContentDialogResult.Primary && builtProfile is not null)
        {
            await viewModel.AddProfileAsync(builtProfile);
        }
    }

    private async void OnEditProfileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel ||
            sender is not Button { CommandParameter: ProfileSource profile })
        {
            return;
        }

        var editProfileViewModel = App.Services!.GetRequiredService<AddProfileViewModel>();
        editProfileViewModel.LoadForEdit(profile);

        var dialog = new FAContentDialog
        {
            Title = "Edit Profile",
            Content = new AddProfileView { DataContext = editProfileViewModel },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary
        };

        // See OnAddProfileClick's comment - same fix, same reason.
        ProfileSource? updatedProfile = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (editProfileViewModel.TryBuildProfile(out var profile) && profile is not null)
            {
                updatedProfile = profile;
            }
            else
            {
                args.Cancel = true;
            }
        };

        var result = await dialog.ShowAsync();
        if (result == FAContentDialogResult.Primary && updatedProfile is not null)
        {
            await viewModel.EditProfileAsync(updatedProfile);
        }
    }
}
