using Android.Content;
using Android.OS;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Sources.GitHub;

namespace BitMagic.BennyBox.Android.Services;

// The same APK/AAB build serves two channels: sideloaded from GitHub releases (keeps the GitHub
// update check) and Google Play (Play owns updates, and its policy forbids pointing users at updates
// from anywhere else). Which one this install is gets decided at runtime from the installer package,
// so one build covers both and nothing needs a separate "Play flavour".
public class PlayAwareUpdateCheckService : IUpdateCheckService
{
    private const string PlayStorePackage = "com.android.vending";

    private readonly GitHubUpdateCheckService _gitHub;
    private readonly Lazy<bool> _isFromPlayStore;

    public PlayAwareUpdateCheckService(GitHubUpdateCheckService gitHub, Context context)
    {
        _gitHub = gitHub;
        _isFromPlayStore = new Lazy<bool>(() => GetInstallerPackage(context) == PlayStorePackage);
    }

    public string CurrentVersion => _gitHub.CurrentVersion;

    public bool IsAvailable => !_isFromPlayStore.Value;

    public Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default) =>
        IsAvailable ? _gitHub.CheckForUpdateAsync(cancellationToken) : Task.FromResult<UpdateInfo?>(null);

    private static string? GetInstallerPackage(Context context)
    {
        try
        {
            var packageName = context.PackageName!;
            var packageManager = context.PackageManager!;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                return packageManager.GetInstallSourceInfo(packageName).InstallingPackageName;
            }

#pragma warning disable CA1422 // GetInstallerPackageName is the only option below API 30
            return packageManager.GetInstallerPackageName(packageName);
#pragma warning restore CA1422
        }
        catch (Exception)
        {
            // Unknown installer - treat as sideloaded, which keeps the GitHub check (the safe default
            // for anyone who isn't on Play).
            return null;
        }
    }
}
