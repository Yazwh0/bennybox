using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using BitMagic.BennyBox.Android.Views;
using BitMagic.BennyBox.ViewModels;
using BitMagic.BennyBox.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BitMagic.BennyBox.Views;

// Mirrors desktop's SettingsView.axaml.cs - see its comments for the FAContentDialog
// PrimaryButtonClick/Cancel rationale and the TopLevel/StorageProvider folder-picker split. Avalonia's
// StorageProvider abstraction routes OpenFolderPickerAsync through Android's Storage Access Framework
// on this platform, so no Android-specific picker code is needed here.
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async void OnChangeDownloadsLocationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Downloads Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0 && (folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath) is { } path)
        {
            await viewModel.SetDownloadsLocationAsync(path);
        }
    }

    private async void OnAddProfileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var addProfileViewModel = global::BitMagic.BennyBox.Android.App.Services!.GetRequiredService<AddProfileViewModel>();
        var dialog = new FAContentDialog
        {
            Title = "Add Profile",
            Content = new AddProfileView { DataContext = addProfileViewModel },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary
        };

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
            sender is not Avalonia.Controls.Button { CommandParameter: ProfileSource profile })
        {
            return;
        }

        var editProfileViewModel = global::BitMagic.BennyBox.Android.App.Services!.GetRequiredService<AddProfileViewModel>();
        editProfileViewModel.LoadForEdit(profile);

        var dialog = new FAContentDialog
        {
            Title = "Edit Profile",
            Content = new AddProfileView { DataContext = editProfileViewModel },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary
        };

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
