namespace BitMagic.BennyBox.Core.Services;

// %AppData%\BennyBox\... - matches this app's original, Windows-only layout.
public class WindowsAppPaths : IAppPaths
{
    private static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BennyBox");

    public string DatabasePath => Path.Combine(Root, "iptv.db");

    public string DownloadsRoot => Path.Combine(Root, "Downloads");

    public string LogDirectory => Path.Combine(Root, "logs");

    public string LogoCacheDirectory => Path.Combine(Root, "logos");
}
