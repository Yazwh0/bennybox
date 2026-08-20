using CommunityToolkit.Mvvm.ComponentModel;
using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

// Wraps a raw EpgProgramme with the one bit of UI state it needs (whether a reminder is set) - see
// GuideRowViewModel.CatchupRequested/ReminderToggleRequested and EpgRowControl's click handling.
public partial class ProgrammeViewModel : ObservableObject
{
    public EpgProgramme Programme { get; }
    public string Title => Programme.Title;
    public DateTime StartUtc => Programme.StartUtc;
    public DateTime EndUtc => Programme.EndUtc;

    [ObservableProperty]
    private bool _hasReminder;

    public ProgrammeViewModel(EpgProgramme programme, bool hasReminder = false)
    {
        Programme = programme;
        _hasReminder = hasReminder;
    }
}
