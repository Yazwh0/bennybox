using Avalonia.Controls;
using Avalonia.Input.Platform;
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

    private async void OnScanSetupQrClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        string? code;
        try
        {
            code = await global::BitMagic.BennyBox.Android.Services.QrCodeScanner.ScanAsync();
        }
        catch (Exception ex)
        {
            // Play services downloads its scanner on first use (see the manifest's barcode_ui hint) - that
            // shows up here as "Waiting for the Barcode UI module to be downloaded".
            viewModel.StatusMessage = ex.Message.Contains("download", StringComparison.OrdinalIgnoreCase)
                ? "The scanner is still being downloaded by Google Play services - try again in a moment, or use Paste code."
                : $"Couldn't open the scanner ({ex.Message}). Use Paste code instead.";
            return;
        }

        if (code is not null)
        {
            await viewModel.ImportSetupCodeCommand.ExecuteAsync(code);
        }
    }

    private async void OnPasteSetupCodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var textBox = new TextBox { Watermark = "BENNYBOX1:...", AcceptsReturn = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 160 };
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard &&
            await clipboard.TryGetTextAsync() is { } clipboardText &&
            clipboardText.TrimStart().StartsWith(BitMagic.BennyBox.Core.Services.ProfileSetupCode.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            textBox.Text = clipboardText.Trim();
        }

        var dialog = new FAContentDialog
        {
            Title = "Paste setup code",
            Content = textBox,
            PrimaryButtonText = "Set up",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == FAContentDialogResult.Primary)
        {
            await viewModel.ImportSetupCodeCommand.ExecuteAsync(textBox.Text);
        }
    }

    // "Share setup": a QR code another phone scans with its own "Scan QR code" button, carrying any
    // mix of this phone's settings, favourites and sources (see SettingsViewModel.BuildShareCodeAsync).
    // Sources are off by default because they include passwords. The code rebuilds as the switches change.
    private async void OnShareSetupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var includeSettings = new ToggleSwitch { IsChecked = true };
        var includeFavorites = new ToggleSwitch { IsChecked = true };
        var includeSources = new ToggleSwitch { IsChecked = false };
        var image = new Avalonia.Controls.Image { Width = 280, Height = 280 };
        var gathering = new StackPanel
        {
            Width = 280, Height = 280, Spacing = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children =
            {
                new FAProgressRing { IsActive = true, Width = 40, Height = 40, Margin = new Avalonia.Thickness(0, 100, 0, 0) },
                new TextBlock { Text = "Finding favourites...", Foreground = Avalonia.Media.Brushes.Black, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
            },
        };
        IReadOnlyList<BitMagic.BennyBox.Core.Services.SharedFavorite>? favorites = null;
        var warning = new TextBlock
        {
            Text = "This code includes passwords. Don't screenshot or share it.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7,
            IsVisible = false,
        };
        string? code = null;

        static Grid Row(string label, ToggleSwitch toggle)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
            return grid;
        }

        void Refresh()
        {
            var wantFavorites = includeFavorites.IsChecked == true;
            gathering.IsVisible = wantFavorites && favorites is null;
            image.IsVisible = !gathering.IsVisible;
            warning.IsVisible = includeSources.IsChecked == true;
            if (gathering.IsVisible)
            {
                code = null;
                return;
            }

            code = viewModel.BuildShareCode(includeSettings.IsChecked == true, wantFavorites ? favorites : null, includeSources.IsChecked == true);
            using var generator = new QRCoder.QRCodeGenerator();
            var data = generator.CreateQrCode(code, QRCoder.QRCodeGenerator.ECCLevel.M);
            using var stream = new MemoryStream(new QRCoder.PngByteQRCode(data).GetGraphic(10));
            var old = image.Source as IDisposable;
            image.Source = new Avalonia.Media.Imaging.Bitmap(stream);
            old?.Dispose();
        }

        foreach (var toggle in new[] { includeSettings, includeFavorites, includeSources })
        {
            toggle.IsCheckedChanged += (_, _) => Refresh();
        }

        var content = new StackPanel { Spacing = 8, MaxWidth = 340 };
        content.Children.Add(new Border
        {
            Background = Avalonia.Media.Brushes.White,
            Padding = new Avalonia.Thickness(8),
            CornerRadius = new Avalonia.CornerRadius(8),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Child = new Panel { Children = { image, gathering } },
        });
        content.Children.Add(new TextBlock
        {
            Text = "On the other phone: Benny Box → Settings → Scan QR code.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        content.Children.Add(Row("Settings", includeSettings));
        content.Children.Add(Row("Favourites", includeFavorites));
        content.Children.Add(Row("Sources (with passwords)", includeSources));
        content.Children.Add(warning);

        Refresh();
        _ = GatherFavoritesAsync();

        async Task GatherFavoritesAsync()
        {
            try
            {
                favorites = await viewModel.ExportFavoritesAsync();
            }
            catch (Exception)
            {
                favorites = [];
            }

            Refresh();
        }

        var dialog = new FAContentDialog
        {
            Title = "Share setup",
            Content = new ScrollViewer { Content = content },
            PrimaryButtonText = "Copy as text",
            CloseButtonText = "Done",
            DefaultButton = FAContentDialogButton.Close
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (code is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(code);
                dialog.PrimaryButtonText = "Copied";
            }
        };

        await dialog.ShowAsync();
        (image.Source as IDisposable)?.Dispose();
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
