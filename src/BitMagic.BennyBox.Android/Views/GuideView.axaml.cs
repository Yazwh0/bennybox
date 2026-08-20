using Avalonia.Controls;
using Avalonia.Interactivity;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Views;

public partial class GuideView : UserControl
{
    public GuideView() => InitializeComponent();

    // Mirrors desktop's EpgRowControl.OnPointerPressed/IsCatchupAvailable (BitMagic.BennyBox.Controls,
    // desktop-only project) - same three-way tap behaviour, just triggered by a Button.Click on a list
    // item instead of a pixel-position hit test. Not shared via a common helper because EpgRowControl
    // lives in the desktop head project, which Android doesn't (and shouldn't) reference.
    private void OnProgrammeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Button { DataContext: ProgrammeViewModel programme, Tag: GuideRowViewModel row })
        {
            return;
        }

        if (programme.StartUtc <= row.NowUtc && row.NowUtc < programme.EndUtc)
        {
            row.TuneRequested?.Invoke(row.Channel);
        }
        else if (IsCatchupAvailable(row, programme))
        {
            row.CatchupRequested?.Invoke(row.Channel, programme.Programme);
        }
        else if (programme.StartUtc > row.NowUtc)
        {
            programme.HasReminder = !programme.HasReminder;
            row.ReminderToggleRequested?.Invoke(row.Channel, programme);
        }
    }

    private static bool IsCatchupAvailable(GuideRowViewModel row, ProgrammeViewModel programme) =>
        programme.EndUtc <= row.NowUtc &&
        row.Channel.HasCatchup &&
        row.NowUtc - programme.StartUtc <= TimeSpan.FromDays(Math.Max(row.Channel.CatchupDays, 1));
}
