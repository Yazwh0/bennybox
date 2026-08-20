using Android.App;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Android.Services;

// Context.FilesDir is private, app-scoped storage that survives app updates but is removed on
// uninstall - the Android equivalent of WindowsAppPaths' %AppData%\BennyBox root.
public class AndroidAppPaths : IAppPaths
{
    private static string Root => Application.Context.FilesDir!.AbsolutePath;

    public string DatabasePath => Path.Combine(Root, "iptv.db");

    public string DownloadsRoot => Path.Combine(Root, "Downloads");

    public string LogDirectory => Path.Combine(Root, "logs");

    public string LogoCacheDirectory => Path.Combine(Root, "logos");
}
