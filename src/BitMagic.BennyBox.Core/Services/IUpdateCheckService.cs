namespace BitMagic.BennyBox.Core.Services;

// Checked once at startup (see MainWindowViewModel) against the running app's own version - a
// background nicety, not something the app depends on, so a failed check (offline, GitHub down,
// rate-limited) is swallowed by the implementation and just returns null rather than surfacing
// anywhere as an error.
public interface IUpdateCheckService
{
    // Null means either already up to date, or the check itself failed - callers don't need to
    // (and can't) tell the two apart, since both just mean "nothing to show the user".
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    // The running app's own version, for display (Settings -> Updates) - e.g. "0.0.13" on an
    // official release, something like "1.0.0+<sha>" on a local dev build (see the implementation
    // for why a dev build never actually triggers an update banner).
    string CurrentVersion { get; }
}

public record UpdateInfo(string Version, string ReleaseUrl);
